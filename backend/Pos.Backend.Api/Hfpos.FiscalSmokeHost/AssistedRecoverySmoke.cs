using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.Tests.Integration;
using Xunit;

namespace Hfpos.FiscalSmokeHost;

internal static class AssistedRecoverySmoke
{
    internal static async Task RunAsync(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HF_POS_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("HF_POS_TEST_CONNECTION_STRING is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database != "hfpos_test_530_smoke" || parsed.Username != "hfpos_test" || parsed.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Assisted recovery host requires its dedicated loopback hfpos_test_530_smoke database / hfpos_test user.");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("HF_POS_SYNTHETIC_FISCAL_HOST", "1");
        Environment.SetEnvironmentVariable("HF_POS_SYNTHETIC_RECOVERY_HOST", "1");
        Environment.SetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(SmokeMain).Assembly.GetName().Name!);
        Environment.SetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");
        SmokeConfiguration.VerifyStartup();
        if (args.Contains("--verify-configuration")) return;
        if (args.Contains("--initialize"))
        {
            var database = new PostgresDatabaseFixture();
            await database.RecreateDatabaseAsync();
            await AssistedPasswordRecoveryTests.SeedAsync(database);
        }
        if (args.Contains("--verify"))
        {
            if (!args.Contains("--initialize")) throw new InvalidOperationException("Verification requires explicit initialization of its disposable database.");
            // Real Production-security HTTP pipeline; no listener or fiscal fixture.
            using var factory = new ProductionSecurityApiFactory(new() {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["AuthRateLimit:PermitLimit"] = "100" });
            using var client = factory.Client(preventHostingStartup: true);
            var admin = await AssistedPasswordRecoveryTests.Login(client, false, "cashier-recovery530", AssistedPasswordRecoveryTests.Legacy);
            await using var db = new PostgresDatabaseFixture().CreateDbContext();
            var owner = await db.Users.SingleAsync(u => u.Username == "owner-530");
            var old = await AssistedPasswordRecoveryTests.Login(client, false, owner.Username, AssistedPasswordRecoveryTests.Legacy);
            var issuedResponse = await AssistedPasswordRecoveryTests.Send(client, HttpMethod.Post, $"/api/account/recovery/users/{owner.Id}", admin, AssistedPasswordRecoveryTests.IssueBody());
            Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);
            var issued = (await issuedResponse.Content.ReadFromJsonAsync<RecoveryIssuedDto>())!;
            var complete = await client.PostAsJsonAsync("/api/account/recovery/complete", new CompleteRecoveryDto(issued.Token, AssistedPasswordRecoveryTests.NewPassword));
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await AssistedPasswordRecoveryTests.Send(client, HttpMethod.Get, "/api/Auth/me", old)).StatusCode);
            await AssistedPasswordRecoveryTests.Login(client, false, owner.Username, AssistedPasswordRecoveryTests.NewPassword);
            Assert.Equal(1, await db.PasswordSecurityAudits.CountAsync(a => a.Event == "Consumed"));
            Console.WriteLine("Assisted recovery synthetic HTTP verification PASS: generated link, owner password, prior JWT rejected, new login, audit. No SRI/SMTP/signing certificates.");
            return;
        }
        Console.WriteLine("Synthetic HF One access host https://localhost:7096. Admin cashier-recovery530 / old-only; owner owner-530 / old-only. Platform platform-530-a and platform-530-b / synthetic platform password 530. No real data/SRI/SMTP/signing certificates.");
        var result = typeof(PosDbContext).Assembly.EntryPoint!.Invoke(null,
            new object?[] { args.Where(a => a is not ("--initialize" or "--assisted-recovery")).ToArray() });
        if (result is Task task) await task;
    }
}
