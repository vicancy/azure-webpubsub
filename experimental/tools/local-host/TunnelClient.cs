using System;
using System.Buffers;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

using Azure.Core;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.HttpTunnel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;

using static Microsoft.AspNetCore.WebSockets.Internal.Constants;
using static Microsoft.Azure.SignalR.HttpTunnel.TunnelMessageProtocol;

namespace local_host
{
    public sealed class TunnelClient : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<WebSocketCloseStatus?> _closeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ClientWebSocket _webSocket;

        private readonly ILogger _logger;
        private string _tunnelEndpoint;
        private readonly Uri _urlWithToken;

        public Task ReceiveTask { get; }

        public Task ConnectTask { get; }

        public Task<WebSocketCloseStatus?> CloseTask => _closeTcs.Task;

        public string SubProtocol => _webSocket.SubProtocol;

        public string UserId { get; }

        public TunnelClient(TokenCredential token, string hub, ILoggerFactory loggerFactory)
        {
            _token = token;
            _hub = hub;
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<TunnelClient>();
            _tunnelEndpoint = $"ws://localhost:8080/api/tunnel/:connect?hub={hub}";

            _urlWithToken = new Uri(_tunnelEndpoint);

            _logger = loggerFactory?.CreateLogger<TunnelClient>() ?? NullLogger<TunnelClient>.Instance;

            var ws = new ClientWebSocket();
            _webSocket = ws;
            ConnectTask = ConnectAsync();
            ReceiveTask = ReceiveLoop(_cts.Token);
        }

        public TunnelClient(string uri,
                                  string signingKey = null,
                                  ILoggerFactory loggerFactory = null,
                                  string userId = null,
                                  string queryString = null,
                                  string audience = null,
                                  Claim[] claims = null,
                                  IEnumerable<string> subProtocols = null,
                                  bool anonymous = false,
                                  string accessToken = null,
                                  Dictionary<string, string[]> headers = null) :
            this(GenerateUri(uri, signingKey, userId, queryString, audience, claims, anonymous, accessToken), loggerFactory, subProtocols, headers)
        {
            UserId = userId;
        }

        public TunnelClient(string uri, ILoggerFactory loggerFactory, IEnumerable<string> subProtocols = null)
            : this(new Uri(uri), loggerFactory, subProtocols, null)
        {
        }

        public TunnelClient(Uri fullUri, ILoggerFactory loggerFactory, IEnumerable<string> subProtocols = null, Dictionary<string, string[]> headers = null)
        {
            _urlWithToken = fullUri;

            _logger = loggerFactory?.CreateLogger<TunnelClient>() ?? NullLogger<TunnelClient>.Instance;

            var ws = new ClientWebSocket();
            if (subProtocols != null)
            {
                foreach (var p in subProtocols)
                {
                    ws.Options.AddSubProtocol(p);
                }
            }
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    foreach (var val in header.Value)
                    {
                        ws.Options.SetRequestHeader(header.Key, val);
                    }
                }
            }
            _webSocket = ws;
            ConnectTask = ConnectAsync();
            ReceiveTask = ReceiveLoop(_cts.Token);
        }

        private static Uri GenerateUri(string uri, string signingKey, string userId, string queryString, string audience, Claim[] claims, bool anonymous, string accessToken)
        {
            accessToken = anonymous || signingKey == null ? "" : accessToken ?? GetAccessToken(audience ?? Regex.Replace(uri, @"\:\d+\/", "/"), signingKey, userId, claims);
            var qs = queryString == null ? $"access_token={accessToken}" : $"{queryString}&access_token={accessToken}";
            qs = uri.Contains("?") ? "&" + qs : "?" + qs;
            return new Uri($"{uri}{qs}");
        }

        public void Dispose()
        {
            _webSocket.Abort();
        }

        public void Abort()
        {
            _webSocket.Abort();
        }

        public async Task StopAsync()
        {
            try
            {
                // Block a Start from happening until we've finished capturing the connection state.
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default);
            }
            catch { }

            // Wait for the receiving tast to end
            await ReceiveTask;
        }

        public Func<HttpRequestMessage, Task<HttpResponseMessage>> RequestHandler { get; set; }

        private async Task ConnectAsync()
        {
            _logger.LogInformation($"Connecting to {_urlWithToken}");
            try
            {
                await _webSocket.ConnectAsync(_urlWithToken, _cts.Token);
            }
            catch (Exception e)
            {
                _logger.LogError($"Error connecting to {_urlWithToken}: {e.Message}");
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            await ConnectTask;
            // Don't use thread static instance here because writer is used with async
            using var buffer = new MemoryBufferWriter();
            while (!token.IsCancellationRequested)
            {
                buffer.Reset();
                while (!token.IsCancellationRequested)
                {
                    var memory = buffer.GetMemory();
                    var receiveResult = await _webSocket.ReceiveAsync(memory, token);

                    // Need to check again for NetCoreApp2.2 because a close can happen between a 0-byte read and the actual read
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            _closeTcs.TrySetResult(_webSocket.CloseStatus);
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, default);
                        }
                        catch
                        {
                            // It is possible that the remote is already closed
                        }
                        return;
                    }

                    // always binary
                    if (receiveResult.MessageType != WebSocketMessageType.Binary)
                    {
                        throw new InvalidDataException("Only support binary");
                    }

                    var current = new ReadOnlySequence<byte>(memory[..receiveResult.Count]);
                    while (Instance.TryParse(ref current, out var message))
                    {
                        if (message.Type == TunnelMessageType.HttpRequest)
                        {
                            // invoke remote
                            var request = (RequestMessage)message;
                            var response = await RequestHandler.Invoke(ToHttp(request));
                            using var writer = new MemoryBufferWriter();

                            // TODO: write directly to memory instead of convert
                            var result = await ToResponse(request.AckId, response);
                            Instance.Write(result, writer);
                            using var owner = writer.CreateMemoryOwner();
                            await _webSocket.SendAsync(owner.Memory, WebSocketMessageType.Binary, true, token);
                        }
                    }

                    // advance to current parsed
                    buffer.Advance(receiveResult.Count - (int)current.Length);
                }
            }
        }

        private static HttpRequestMessage ToHttp(RequestMessage request)
        {
            var http = new HttpRequestMessage()
            {
                RequestUri = new Uri(request.Url),
                Method = new HttpMethod(request.HttpMethod),
            };
            if (request.Content.Length > 0)
            {
                var streamContent = new StreamContent(new MemoryStream(request.Content.ToArray()));
                http.Content = streamContent;
            }

            // Copy the request headers
            foreach (var (header, value) in request.Headers)
            {
                if (!http.Headers.TryAddWithoutValidation(header, value) && http.Content != null)
                {
                    http.Content?.Headers.TryAddWithoutValidation(header, value);
                }
            }
            return http;
        }

        private async Task<ResponseMessage> ToResponse(int ackId, HttpResponseMessage message)
        {
            _logger.LogInformation($"Received response status code: {message.StatusCode}");
            var bytes = new ResponseMessage
            {
                AckId = ackId,
                Content = await message.Content.ReadAsByteArrayAsync(),
                StatusCode = (int)message.StatusCode,
                Headers = new Dictionary<string, string[]>()
            };

            foreach (var (key, header) in message.Headers)
            {
                //if (string.Equals("Connection", key, StringComparison.OrdinalIgnoreCase)) continue;
                //if (string.Equals("Keep-Alive", key, StringComparison.OrdinalIgnoreCase)) continue;
                bytes.Headers.Add(key, header.ToArray());
            }

            if (message.Content != null)
            {
                foreach (var (key, header) in message.Content.Headers)
                {
                    bytes.Headers.Add(key, header.ToArray());
                }
            }
            return bytes;
        }

        private static readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();
        private TokenCredential _token;
        private string _hub;
        private readonly ILoggerFactory _loggerFactory;

        public static string GetAccessToken(string url,
                                            string signingKey,
                                            string userId = null,
                                            Claim[] additionalClaims = null,
                                            DateTime? expires = null,
                                            string kid = null)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)) { KeyId = kid ?? signingKey.GetHashCode().ToString() };
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>();
            if (additionalClaims != null)
            {
                claims.AddRange(additionalClaims);
            }

            if (!string.IsNullOrEmpty(userId))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }
            var identity = claims.Count > 0 ? new ClaimsIdentity(claims) : null;
            var token = JwtTokenHandler.CreateJwtSecurityToken(
                subject: identity,
                audience: url,
                expires: expires ?? DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }
    }
}
