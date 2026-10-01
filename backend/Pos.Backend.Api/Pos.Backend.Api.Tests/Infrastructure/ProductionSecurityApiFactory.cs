using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Backend.Api.Tests.Unit;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed class ProductionSecurityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _keys = Directory.CreateTempSubdirectory("hfpos-519-http-").FullName;
    private readonly Dictionary<string, string?> _overrides;
    private readonly IPAddress? _remoteIp;
    public ProductionSecurityApiFactory(Dictionary<string, string?>? overrides = null, IPAddress? remoteIp = null)
    { _overrides = overrides ?? []; _remoteIp = remoteIp; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = ProductionConfigurationTests.Valid(_keys);
            foreach (var (key, value) in _overrides) settings[key] = value;
            configuration.AddInMemoryCollection(settings);
        });
        if (_remoteIp is not null)
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new SyntheticRemoteIp(_remoteIp)));
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing && Directory.Exists(_keys)) Directory.Delete(_keys, true); }

    private sealed class SyntheticRemoteIp(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) => { context.Connection.RemoteIpAddress = ip; return nextMiddleware(); });
            next(app);
        };
    }
}
