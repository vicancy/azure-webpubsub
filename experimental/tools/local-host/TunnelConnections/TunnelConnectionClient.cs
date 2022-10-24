using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;
using System.Threading.Tasks;

using Azure.Core;

using local_host.ServiceProtocols;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace local_host.TunnelConnections
{
    internal static class ServerName
    {
        public static readonly string Name = $"{Environment.MachineName}_{Guid.NewGuid():N}";
    }
    internal delegate void Callback();

    internal class TunnelConnectionClient
    {
        private readonly TokenCredential _token;
        private readonly string _hub;
        private readonly ILoggerFactory _loggerFactory;
        private ConcurrentDictionary<string, RawWebSocketClient> _innerClients = new ConcurrentDictionary<string, RawWebSocketClient>();
        private readonly string _tunnelEndpoint;
        private ILogger _logger;
        private RawWebSocketClient _client;
        public TunnelConnectionClient(TokenCredential token, string hub, ILoggerFactory loggerFactory)
        {
            _token = token;
            _hub = hub;
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<TunnelConnectionClient>();
            _tunnelEndpoint = $"http://localhost/api/hubs/{hub}/upstreamtunnel/:connect";

        }

        public event Callback Connected;
        public event Callback Disconnected;
        public event Callback Connecting;

        public Func<HttpRequestMessage, Task<HttpResponseMessage>> RequestHandler { get; set; }
        public Task StartAsync()
        {
            // var token = await credential.GetTokenAsync(new TokenRequestContext(scopes: new string[] { "https://webpubsub.azure.net/.default" }));
            _client = new RawWebSocketClient(_tunnelEndpoint, "", NullLoggerFactory.Instance, accessToken: "");
            return _client.ConnectTask;
        }

        public Task StopAsync()
        {
            return _client.StopAsync();
        }

        private void StartConnection(CancellationToken cancellationToken)
        {
            _ = StartConnectionCore(_tunnelEndpoint, cancellationToken);
        }

        private async Task StartConnectionCore(string url, CancellationToken cancellationToken)
        {
            try
            {
                var id = Guid.NewGuid().ToString("N");
                using var ws = new RawWebSocketClient(url, userId: id, loggerFactory: _loggerFactory, claims: new Claim[]
                {
                new Claim("webpubsub.tunnel.server.name", ServerName.Name)
                });

                _innerClients[id] = ws;
                _ = DispatchPayload(ws, cancellationToken);
                await ws.CloseTask;
                // clean up it from the dictionary
                _innerClients.TryRemove(id, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error with the connection {ex.Message}");
            }
            finally
            {
                // restart another connection
                _ = StartConnectionCore(url, cancellationToken);
            }
        }

        private async Task DispatchPayload(RawWebSocketClient ws, CancellationToken cancellationToken)
        {
            await ws.ConnectTask;
            while (await ws.ReceivedMessageReader.WaitToReadAsync(cancellationToken))
            {
                while (ws.ReceivedMessageReader.TryRead(out var received))
                {
                    if (TunnelMessageProtocol.Instance.TryParseOnDemandConnectionMessage(received.Buffer, out var message))
                    {
                        foreach (var target in message.Targets)
                        {
                            _ = StartConnectionCore(GetOnDemandConnectionEndpoint(target), cancellationToken);
                        }
                    }
                }
            }
        }

        private string GetOnDemandConnectionEndpoint(string target)
        {
            return $"{_tunnelEndpoint}?targetId={target}";
        }
    }
}
