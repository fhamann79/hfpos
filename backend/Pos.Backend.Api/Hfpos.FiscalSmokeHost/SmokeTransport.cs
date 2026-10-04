using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Hfpos.FiscalSmokeHost;

// This assembly is not referenced or published by the production API/container.
public sealed class SmokeTransport : ISriWebServiceClient
{
    private readonly string _connection;
    public SmokeTransport() => _connection = Environment.GetEnvironmentVariable("HF_POS_TEST_CONNECTION_STRING")!;
    public static async Task InitializeAsync(string connection)
    {
        await using var db = new NpgsqlConnection(connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SCHEMA IF NOT EXISTS synthetic_remote;
            CREATE TABLE IF NOT EXISTS synthetic_remote.settings (id int PRIMARY KEY, mode text NOT NULL);
            INSERT INTO synthetic_remote.settings VALUES (1,'authorize') ON CONFLICT (id) DO UPDATE SET mode='authorize';
            CREATE TABLE IF NOT EXISTS synthetic_remote.receipts
                (access_key text PRIMARY KEY, mode text NOT NULL, signed_xml text, submissions int NOT NULL DEFAULT 0, queries int NOT NULL DEFAULT 0);
            TRUNCATE synthetic_remote.receipts;
            """, db);
        await command.ExecuteNonQueryAsync();
    }
    public async Task<SriReceptionResponse> SubmitAsync(string xml, int environment, CancellationToken ct = default)
    {
        var key = XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "claveAcceso").Value;
        await using var db = new NpgsqlConnection(_connection); await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO synthetic_remote.receipts(access_key, mode, signed_xml, submissions)
              SELECT @key, mode, @xml, 1 FROM synthetic_remote.settings WHERE id=1
            ON CONFLICT (access_key) DO UPDATE SET submissions=synthetic_remote.receipts.submissions+1
            RETURNING mode
            """, db);
        command.Parameters.AddWithValue("key", key); command.Parameters.AddWithValue("xml", xml);
        var mode = (string)(await command.ExecuteScalarAsync(ct))!;
        if (mode == "hold")
        {
            // The admitted reception is held before the next protected authorization stage.
            // Only synthetic remote behavior changes; no application sale/job state is written here.
            while (mode == "hold")
            {
                await Task.Delay(200, ct);
                await using var poll = new NpgsqlCommand("SELECT mode FROM synthetic_remote.settings WHERE id=1", db);
                mode = (string)(await poll.ExecuteScalarAsync(ct))!;
            }
            await using var release = new NpgsqlCommand("UPDATE synthetic_remote.receipts SET mode=@mode WHERE access_key=@key", db);
            release.Parameters.AddWithValue("key", key); release.Parameters.AddWithValue("mode", mode);
            await release.ExecuteNonQueryAsync(ct);
        }
        // Remote acceptance has committed independently before simulating a lost response.
        if (mode == "ambiguous") throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED");
        return new() { Estado = mode == "reject" ? "DEVUELTA" : "RECIBIDA", RawResponseXml = "<syntheticReception/>" };
    }
    public async Task<SriAuthorizationResponse> CheckAuthorizationAsync(string key, int environment, CancellationToken ct = default)
    {
        await using var db = new NpgsqlConnection(_connection); await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE synthetic_remote.receipts SET queries=queries+1 WHERE access_key=@key RETURNING mode,signed_xml
            """, db);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new() { Estado = "NOT_FOUND" };
        var mode = reader.GetString(0); var xml = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (mode is "pending" or "ambiguous" or "hold" || xml is null) return new() { Estado = "NOT_FOUND" };
        var date = DateTime.UtcNow;
        var response = new XElement("autorizacion", new XElement("estado", "AUTORIZADO"),
            new XElement("numeroAutorizacion", key), new XElement("fechaAutorizacion", date.ToString("O")),
            new XElement("comprobante", new XCData(xml)));
        return new() { Estado = "AUTORIZADO", AuthorizationNumber = key, AuthorizationDate = date,
            AuthorizedXml = xml, RawResponseXml = response.ToString(SaveOptions.DisableFormatting) };
    }
}

[ApiController]
[Route("api/synthetic-fiscal")]
[Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
[RequireOperationalContext]
public sealed class SmokeControlController : ControllerBase
{
    private static string Connection => Environment.GetEnvironmentVariable("HF_POS_TEST_CONNECTION_STRING")!;
    [HttpPut("mode/{mode}")]
    public async Task<IActionResult> Mode(string mode)
    {
        if (mode is not ("authorize" or "pending" or "ambiguous" or "reject" or "hold")) return BadRequest();
        await using var db = new NpgsqlConnection(Connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE synthetic_remote.settings SET mode=@mode WHERE id=1", db);
        command.Parameters.AddWithValue("mode", mode); await command.ExecuteNonQueryAsync(); return NoContent();
    }
    [HttpPost("resolve/{key}")]
    public async Task<IActionResult> Resolve(string key)
    {
        await using var db = new NpgsqlConnection(Connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE synthetic_remote.receipts SET mode='authorize' WHERE access_key=@key AND signed_xml IS NOT NULL", db);
        command.Parameters.AddWithValue("key", key); return await command.ExecuteNonQueryAsync() == 1 ? NoContent() : NotFound();
    }
    [HttpGet]
    public async Task<IActionResult> State()
    {
        await using var db = new NpgsqlConnection(Connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT access_key,mode,submissions,queries FROM synthetic_remote.receipts ORDER BY access_key", db);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync()) rows.Add(new { accessKey = reader.GetString(0), mode = reader.GetString(1), submissions = reader.GetInt32(2), queries = reader.GetInt32(3) });
        return Ok(rows);
    }
}
