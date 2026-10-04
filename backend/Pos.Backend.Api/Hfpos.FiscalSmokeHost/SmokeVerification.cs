using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Xunit;

namespace Hfpos.FiscalSmokeHost;

internal static class SmokeVerification
{
    // CI only: an in-process TestServer, actual HTTP authentication, worker and SOAP parser.
    // Human mode uses the same startup/filter against the disposable loopback database.
    internal static async Task RunAsync(AutonomousIssuingFixture fixture, string connection)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").UseContentRoot(Directory.GetCurrentDirectory()));
        using var admin = app.CreateClient(); using var cashier = app.CreateClient();
        async Task Login(HttpClient client, string username)
        {
            using var response = await client.PostAsJsonAsync("/api/Auth/login", new { username, password = "synthetic-only-528-password" });
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.GetProperty("token").GetString());
        }
        await Login(admin, fixture.Admin.Username); await Login(cashier, fixture.Tenant.OperationalContext.Username);
        using (var enabled = await admin.PutAsJsonAsync("/api/FiscalSettings/sri", new
        { environment = 1, emissionType = 1, isEnabled = true, automaticProcessingEnabled = true }))
            enabled.EnsureSuccessStatusCode();
        SaleDto sale;
        using (var result = await cashier.PostAsJsonAsync("/api/Sales", fixture.Request()))
        {
            result.EnsureSuccessStatusCode(); sale = (await result.Content.ReadFromJsonAsync<SaleDto>())!;
        }
        foreach (var action in new[] { "sign", "submit", "check-authorization" })
            using (var forbidden = await cashier.PostAsync($"/api/Sales/{sale.Id}/sri/{action}", null))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        cashier.Dispose(); // No browser/session is kept alive by the worker.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        while (true)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            var job = await db.ElectronicIssuingJobs.AsNoTracking().SingleAsync(j => j.SaleId == sale.Id, timeout.Token);
            if (job.State == ElectronicIssuingJobState.Authorized) break;
            Assert.NotEqual(ElectronicIssuingJobState.ManualAttention, job.State);
            await Task.Delay(200, timeout.Token);
        }
        using (var scope = app.Services.CreateScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<ISriWebServiceClient>();
            Assert.IsType<SriWebServiceClient>(client); // The actual SOAP client, not an interface bypass.
            Assert.Equal("SRI_PRODUCTION_SUBMISSION_DISABLED", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.SubmitAsync("synthetic", 2))).Message);
        }
        await using var remote = new NpgsqlConnection(connection); await remote.OpenAsync();
        await using var counter = new NpgsqlCommand("SELECT submissions,queries FROM synthetic_remote.receipts WHERE access_key=@key", remote);
        counter.Parameters.AddWithValue("key", sale.AccessKey!);
        await using var reader = await counter.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(1, reader.GetInt32(1)); Assert.False(await reader.ReadAsync());
        Assert.Null(typeof(PosDbContext).Assembly.GetType("Hfpos.FiscalSmokeHost.SmokeControlController"));
        Assert.DoesNotContain(typeof(PosDbContext).Assembly.GetReferencedAssemblies(), a => a.Name == "Hfpos.FiscalSmokeHost");
        Console.WriteLine("Synthetic host verification PASS: real auth/worker/XAdES/SOAP/PG, cashier403, one reception/query, production blocked, harness outside production assembly.");
    }
}
