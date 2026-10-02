using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Middleware;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SecurityBoundaryTests
{
    [Fact]
    public void Key_ring_survives_provider_recreation_and_isolates_application_name()
    {
        var directory = Directory.CreateTempSubdirectory("hfpos-519-keyring-");
        try
        {
            string encrypted;
            using (var a = Provider(directory, "HFPOS"))
                encrypted = a.GetRequiredService<IDataProtectionProvider>().CreateProtector("synthetic-purpose").Protect("synthetic-only-value");
            using (var b = Provider(directory, "HFPOS"))
                Assert.Equal("synthetic-only-value", b.GetRequiredService<IDataProtectionProvider>().CreateProtector("synthetic-purpose").Unprotect(encrypted));
            using var c = Provider(directory, "OtherApplication");
            Assert.Throws<CryptographicException>(() => c.GetRequiredService<IDataProtectionProvider>().CreateProtector("synthetic-purpose").Unprotect(encrypted));
        }
        finally { directory.Delete(true); }
    }

    private static ServiceProvider Provider(DirectoryInfo keys, string name)
    {
        var services = new ServiceCollection().AddLogging();
        var settings = ProductionConfigurationTests.Valid(keys.FullName);
        settings["DataProtection:ApplicationName"] = name;
        services.AddSecurityConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new SyntheticEnvironment("Production"));
        var provider = services.BuildServiceProvider();
        provider.ValidateBeforeStartup();
        return provider;
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void New_password_policy_boundaries(int length, bool valid)
        => Assert.Equal(valid, PasswordPolicy.IsValid(new string('a', length)));

    [Fact]
    public void Blank_new_password_is_rejected() => Assert.False(PasswordPolicy.IsValid(new string(' ', 12)));

    [Theory]
    [InlineData("unknown", 500, "INTERNAL_SERVER_ERROR")]
    [InlineData("operation", 500, "INTERNAL_SERVER_ERROR")]
    [InlineData("uppercase", 500, "INTERNAL_SERVER_ERROR")]
    [InlineData("db", 500, "DB_UPDATE_ERROR")]
    [InlineData("fk", 409, "FOREIGN_KEY_VIOLATION")]
    [InlineData("domain", 400, "INSUFFICIENT_STOCK")]
    [InlineData("sri", 400, "SRI_RECEPTION_COMMUNICATION_FAILED")]
    [InlineData("missing", 404, "SALE_NOT_FOUND")]
    public async Task Errors_preserve_known_codes_but_never_expose_internal_details(string kind, int status, string code)
    {
        const string secret = "INTERNAL synthetic password /path SQL table detail";
        Exception exception = kind switch
        {
            "operation" => new InvalidOperationException(secret),
            "uppercase" => new InvalidOperationException("UNRECOGNIZED_SECRET_MESSAGE"),
            "db" => new DbUpdateException(secret, new Exception(secret)),
            "fk" => new DbUpdateException(secret, new PostgresException(secret, "ERROR", "ERROR", "23503", detail: secret)),
            "domain" => new InvalidOperationException(code, new Exception(secret)),
            "sri" => new InvalidOperationException(code, new Exception(secret)),
            "missing" => new KeyNotFoundException(code, new Exception(secret)),
            _ => new Exception(secret)
        };
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw exception, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(status, context.Response.StatusCode);
        Assert.DoesNotContain(secret, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
    }

    [Theory]
    [InlineData(0, "INVALID_SRI_ENVIRONMENT")]
    [InlineData(3, "INVALID_SRI_ENVIRONMENT")]
    [InlineData(2, "SRI_PRODUCTION_SUBMISSION_DISABLED")]
    public async Task Sri_environment_guard_rejects_before_any_http_call(int environment, string code)
    {
        var handler = new NeverSend();
        using var http = new HttpClient(handler);
        var client = new SriWebServiceClient(http, Options.Create(new SriOptions()), NullLogger<SriWebServiceClient>.Instance);
        Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync("synthetic", environment))).Message);
        Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(() => client.CheckAuthorizationAsync("synthetic", environment))).Message);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class NeverSend : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Tests must not contact external services."); }
    }
}
