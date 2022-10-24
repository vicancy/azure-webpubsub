using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace local_host.TunnelConnections
{
    public sealed class RawWebSocketClient : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<WebSocketCloseStatus?> _closeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ClientWebSocket _webSocket;

        private readonly Channel<WebSocketReceivedResult> _channel;
        private readonly ILogger _logger;
        private readonly Uri _urlWithToken;

        public ChannelReader<WebSocketReceivedResult> ReceivedMessageReader { get; }

        public Task ReceiveTask { get; }

        public Task ConnectTask { get; }

        public Task<WebSocketCloseStatus?> CloseTask => _closeTcs.Task;

        public string SubProtocol => _webSocket.SubProtocol;

        public string UserId { get; }

        public RawWebSocketClient(string uri,
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

        public RawWebSocketClient(string uri, ILoggerFactory loggerFactory, IEnumerable<string> subProtocols = null)
            : this(new Uri(uri), loggerFactory, subProtocols, null)
        {
        }

        public RawWebSocketClient(Uri fullUri, ILoggerFactory loggerFactory, IEnumerable<string> subProtocols = null, Dictionary<string, string[]> headers = null)
        {
            _urlWithToken = fullUri;

            _logger = loggerFactory?.CreateLogger<RawWebSocketClient>() ?? NullLogger<RawWebSocketClient>.Instance;

            _channel = Channel.CreateUnbounded<WebSocketReceivedResult>(new UnboundedChannelOptions()
            {
                SingleWriter = true,
                SingleReader = true
            });

            ReceivedMessageReader = _channel.Reader;

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
            ReceiveTask = ReceiveLoop(_cts.Token).ContinueWith(t =>
            {
                _channel.Writer.Complete(t.Exception);
            });
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

        public async Task<List<WebSocketReceivedResult>> StopAsync()
        {
            try
            {
                // Block a Start from happening until we've finished capturing the connection state.
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default);
            }
            catch { }
            // Wait for the receiving tast to end
            await ReceiveTask;


            var messages = new List<WebSocketReceivedResult>();
            try
            {
                // read the remaining
                while (await ReceivedMessageReader.WaitToReadAsync())
                {
                    while (ReceivedMessageReader.TryRead(out var m))
                    {
                        messages.Add(m);
                    }
                }
            }
            catch { }
            return messages;
        }

        public Task SendMessageAsync(string message, WebSocketMessageType messageType)
        {
            return _webSocket.SendAsync(Encoding.UTF8.GetBytes(message), messageType, true, default);
        }

        private Task ConnectAsync()
        {
            return _webSocket.ConnectAsync(_urlWithToken, _cts.Token);
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

                    buffer.Advance(receiveResult.Count);
                    if (receiveResult.EndOfMessage)
                    {
                        await _channel.Writer.WriteAsync(new WebSocketReceivedResult(buffer.ToArray(), receiveResult.MessageType));
                        break;
                    }
                }
            }
        }

        private static readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();

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
