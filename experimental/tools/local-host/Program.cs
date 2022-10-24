
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Relay;
using Azure.ResourceManager.Relay.Models;
using Azure.ResourceManager.Resources;

using local_host;

using Microsoft.Azure.Relay;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

using System.Net;
using System.Text.Json;

var app = new CommandLineApplication();
app.Name = "awaps-local-debug";
app.Description = "The local tunnel tool to help localhost accessible by Azure Web PubSub service";
app.HelpOption("-h|--help");
// local-host login
// local-host list
// local-host bind -g <resourcegroup> -n <instancename>
// local-host unbind -g <resourcegroup> -n <instancename>
// local-host reset
// local-host show (show bind resources)
// local-host -p 8080
var portOptions = app.Option("-p|--port", "Specify the port of localhost server", CommandOptionType.SingleValue);
var hubOption = app.Option("--hub", "Specify the hub to connect to", CommandOptionType.MultipleValue);
var schemeOption = app.Option("--scheme", "Specify the scheme used to invoke the upstream, supported options are http and https, default is http", CommandOptionType.SingleValue);
app.Command("bind", command =>
{
    command.Description = "Bind to the Web PubSub service";
    command.HelpOption("-h|--help");
    var resourceGroupOption = command.Option("-g|--reourceGroup", "Specify the resourceGroup of the instance.", CommandOptionType.SingleValue);
    var resourceNameOption = command.Option("-n|--name", "Specify the name of the instance.", CommandOptionType.SingleValue);
    command.OnExecute(() =>
    {
        return 0;
    });
});

app.OnExecute(async () =>
{
    var port = 8080;
    // TODO: read multiple hubs from hubOptions
    var hub = "chat"; 
    SupportedScheme scheme = SupportedScheme.Http;
    _ = int.TryParse(portOptions.Value(), out port);
    _ = Enum.TryParse<SupportedScheme>(schemeOption.Value(), out scheme);
    if (true)
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient();
                services.AddSingleton<TunnelService>();
                services.Configure<TunnelServiceOptions>(o =>
                {
                    o.Hub = hub;
                    o.LocalScheme = scheme.ToString().ToLower();
                    o.LocalPort = port;
                });
            }).ConfigureLogging(o => o.AddConsole())
            .Build();

        await host.Services.GetRequiredService<TunnelService>().StartAsync();
    }
    else
    {
        app.ShowHelp();
    }
    return 0;
});

try
{
    app.Execute(args);
}
catch (Exception e)
{
    Console.WriteLine($"Error occured: {e.Message}.");
}
enum SupportedScheme
{
    Http,
    Https
}

internal class TunnelServiceOptions
{
    public string Hub { get; set; }
    public int LocalPort { get; set; }
    public string LocalScheme { get; set; }
}

static class HttpExtensions
{
    public static string GetDisplayUrl(this RelayedHttpListenerContext context)
    {
        var request = context.Request;
        var uri = request.Url;
        var method = request.HttpMethod;
        var body = request.Headers[HttpResponseHeader.ContentLength];
        return $"{method} {uri} {body}";
    }

    public static string GetDisplayUrl(this HttpRequestMessage request)
    {
        var uri = request.RequestUri?.OriginalString ?? string.Empty;
        var method = request.Method;
        var body = request.Content?.Headers.ContentLength;
        return $"{method} {uri} {body}";
    }

}

internal class TunnelService
{
    private readonly TunnelServiceOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TunnelService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public TunnelService(IOptions<TunnelServiceOptions> options, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TunnelService>();
    }

    private static Dictionary<string, string> ParsedConnectionString(string conn)
    {
        return conn.Split(";").Select(s => s.Split("=")).Where(i => i.Length == 2).ToDictionary(j => j[0], j => j[1], StringComparer.OrdinalIgnoreCase);
    }

    private static (string Endpoint, string EntityPath) Parse(string conn)
    {
        if (conn == null)
        {
            throw new ArgumentException("RelayServiceConnectionString is not set");
        }
        var parsed = ParsedConnectionString(conn);
        if (!parsed.TryGetValue("entityPath", out var entityPath))
        {
            throw new ArgumentException("EntityPath is expected to be found in connectionString");
        }

        if (!parsed.TryGetValue("endpoint", out var endpoint))
        {
            throw new ArgumentException("endpoint is expected to be found in connectionString");
        }

        return (endpoint, entityPath);
    }

    public async Task StartAsync()
    {
        var credential = new DefaultAzureCredential(true);

        var listener = new TunnelClient(credential, _options.Hub, _loggerFactory);

        // Provide an HTTP request handler
        listener.RequestHandler = async (request) =>
        {
            using var httpClient = _httpClientFactory.CreateClient();
            _logger.LogInformation($"Received request from: '{request.GetDisplayUrl()}'");
            var targetUri = new UriBuilder("http", "localhost", _options.LocalPort).Uri;

            // Invoke local http server
            // Or self-host a server?
            var proxiedRequest = CreateProxyHttpRequest(request, targetUri);
            _logger.LogInformation($"Proxied request to '{proxiedRequest.GetDisplayUrl()}'");
            try
            {
                var response = await httpClient.SendAsync(proxiedRequest);
                _logger.LogInformation($"Received proxied response from '{proxiedRequest.GetDisplayUrl()}: {response.StatusCode}'");
                return response;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error forwarding request '{proxiedRequest.GetDisplayUrl()}': {e.Message}");
                var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                response.Content = new StringContent(e.Message);
                return response;
            }
        };

        // Opening the listener establishes the control channel to
        // the Azure Relay service. The control channel is continuously 
        // maintained, and is reestablished when connectivity is disrupted.
        try
        {
            await listener.ConnectTask;
            Console.WriteLine("Server listening");
        }
        catch (Exception e)
        {
            Console.WriteLine("Error starting the listener: " + e.Message);
        }

        // Start a new thread that will continuously read the console.
        await Console.In.ReadLineAsync();

        // Close the listener after you exit the processing loop.
        await listener.StopAsync();
    }


    public static HttpRequestMessage CreateProxyHttpRequest(HttpRequestMessage request, Uri uri)
    {
        if (request.RequestUri == null)
        {
            throw new ArgumentNullException(nameof(request.RequestUri));
        }

        var uriBuilder = new UriBuilder(request.RequestUri);
        // uri.Scheme, uri.Host, uri.Port, request.RequestUri.LocalPath, request.RequestUri.quer
        uriBuilder.Scheme = uri.Scheme;
        uriBuilder.Host = uri.Host;
        uriBuilder.Port = uri.Port;

        request.RequestUri = uriBuilder.Uri;

        return request;
    }

    public static HttpRequestMessage CreateProxyHttpRequest(RelayedHttpListenerContext context, Uri uri, string pathBase)
    {
        var requestMessage = new HttpRequestMessage();
        var requestMethod = context.Request.HttpMethod;
        if (context.Request.HasEntityBody)
        {
            var streamContent = new StreamContent(context.Request.InputStream);
            requestMessage.Content = streamContent;
        }

        // Copy the request headers
        foreach (var header in context.Request.Headers.AllKeys)
        {
            if (!requestMessage.Headers.TryAddWithoutValidation(header, context.Request.Headers[header]) && requestMessage.Content != null)
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header, context.Request.Headers[header]);
            }
        }

        requestMessage.Headers.Host = uri.Host;
        requestMessage.Method = new HttpMethod(requestMethod);
        var uriBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, context.Request.Url.LocalPath.Substring(pathBase.Length), context.Request.Url.Query);
        requestMessage.RequestUri = uriBuilder.Uri;
        
        return requestMessage;
    }

    internal static class TokenHelper
    {
        public static (string ClientId, string TenantId, string Upn, string ObjectId) ParseAccountInfoFromToken(string token)
        {
            var parts = token?.Split('.');
            if (parts?.Length != 3)
            {
                throw new ArgumentException("Invalid token", nameof(token));
            }

            (string ClientId, string TenantId, string Upn, string ObjectId) result = default;

            string convertedToken = parts[1].Replace('_', '/').Replace('-', '+');
            switch (parts[1].Length % 4)
            {
                case 2:
                    convertedToken += "==";
                    break;
                case 3:
                    convertedToken += "=";
                    break;
            }
            Utf8JsonReader reader = new Utf8JsonReader(Convert.FromBase64String(convertedToken));
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    switch (reader.GetString())
                    {
                        case "appid":
                            reader.Read();
                            result.ClientId = reader.GetString();
                            break;

                        case "tid":
                            reader.Read();
                            result.TenantId = reader.GetString();
                            break;

                        case "upn":
                            reader.Read();
                            result.Upn = reader.GetString();
                            break;

                        case "oid":
                            reader.Read();
                            result.ObjectId = reader.GetString();
                            break;
                        default:
                            reader.Read();
                            break;
                    }
                }
            }

            return result;
        }
    }
}