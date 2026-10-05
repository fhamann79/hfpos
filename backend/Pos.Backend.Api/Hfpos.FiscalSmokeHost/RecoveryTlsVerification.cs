using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Hfpos.FiscalSmokeHost;

internal static class RecoveryTlsVerification
{
    internal static async Task RunAsync()
    {
        // Same guarded HostingStartup/Kestrel/certificate as preview, but no API,
        // database services, data initialization or business endpoints.
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        await using var app = builder.Build();
        app.MapGet("/synthetic-tls-ready", () => "synthetic-tls-only");
        var certificate = app.Services.GetRequiredService<RecoveryTlsCertificate>().Certificate;
        var expected = certificate.GetCertHash(HashAlgorithmName.SHA256);
        string? ownedKeyName = null;
        CngProvider? ownedProvider = null;
        if (OperatingSystem.IsWindows())
        {
            using var key = certificate.GetRSAPrivateKey();
            var cng = Assert.IsType<RSACng>(key);
            ownedKeyName = cng.Key.KeyName;
            ownedProvider = cng.Key.Provider;
            Assert.False(string.IsNullOrEmpty(ownedKeyName));
            Assert.NotNull(ownedProvider);
            Assert.True(CngKey.Exists(ownedKeyName, ownedProvider));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, presented, _, _) =>
                    request.RequestUri is { IsLoopback: true, Host: "localhost", Port: 7096 }
                    && presented is not null
                    && CryptographicOperations.FixedTimeEquals(expected, presented.GetCertHash(HashAlgorithmName.SHA256))
            };
            using var client = new HttpClient(handler);
            Assert.Equal("synthetic-tls-only", await client.GetStringAsync("https://localhost:7096/synthetic-tls-ready", timeout.Token));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
        if (OperatingSystem.IsWindows()) Assert.False(CngKey.Exists(ownedKeyName!, ownedProvider!));
        Console.WriteLine("Synthetic recovery TLS listener PASS: loopback HTTPS handshake, exact generated certificate pin, owned Windows key deleted on disposal, no trust installation, no business API/DB started.");
    }
}
