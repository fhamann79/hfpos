using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;

// Verification-only console. It never seeds, signs, submits, sends mail or exposes HTTP.
internal static class Program
{
    private sealed record Login(string Username, string Password);
    private sealed record Input(Dictionary<string, string> Keys, string ApplicationName,
        string[] Payloads, Login? Platform, Login? Tenant);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("STOP");
    }

    public static async Task<int> Main()
    {
        Process? backend = null;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("STOP");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            var line = await Console.In.ReadLineAsync(deadline.Token);
            Require(line is { Length: > 0 and <= 16777216 });
            var input = JsonSerializer.Deserialize<Input>(line!) ?? throw new InvalidOperationException("STOP");
            Require(input.Keys.Count is > 0 and <= 128
                && input.Payloads.Length <= 128 && input.ApplicationName.Length is > 0 and <= 200);
            await NetworkProof(deadline.Token);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)))
                .Equals(Environment.GetEnvironmentVariable("HFPOS_VERIFY_PROBE_SHA256"), StringComparison.OrdinalIgnoreCase));
            foreach (var (name, encoded) in input.Keys)
            {
                Require(System.Text.RegularExpressions.Regex.IsMatch(name, @"^key-[a-zA-Z0-9-]+\.xml$"));
                var bytes = Convert.FromBase64String(encoded);
                Require(bytes.Length is > 0 and <= 65536);
                using (var xml = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
                       { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    var document = XDocument.Load(xml);
                    Require(document.Descendants().All(e => e.Name.LocalName is not ("encryptedSecret" or "encryptedKey")
                        && !e.Name.LocalName.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                        && e.Attributes().All(a => a.Name.LocalName != "decryptorType")));
                }
                var path = Path.Combine("/keys", name);
                using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(bytes);
                File.SetUnixFileMode(path, UnixFileMode.UserRead);
                Require(CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(File.ReadAllBytes(path))));
                CryptographicOperations.ZeroMemory(bytes);
            }
            var keyHashes = KeyHashes();
            var assembly = Assembly.LoadFrom("/app/Pos.Backend.Api.dll");
            var purpose = (string)assembly.GetType("Pos.Backend.Api.Core.Security.CompanyEmailProtectionPurposes", true)!
                .GetField("SmtpPasswordV1", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
            Require(purpose == "hfpos.company-email.smtp-password.v1");
            var protection = DataProtectionProvider.Create(new DirectoryInfo("/keys"), b =>
                b.SetApplicationName(input.ApplicationName).DisableAutomaticKeyGeneration());
            foreach (var payload in input.Payloads)
            {
                Require(payload.Length is > 0 and <= 65536);
                var plaintext = protection.CreateProtector(purpose).Unprotect(payload);
                Require(!string.IsNullOrEmpty(plaintext));
                MustNotDecrypt(protection.CreateProtector(purpose + ".verification-wrong"), payload);
                var wrong = DataProtectionProvider.Create(new DirectoryInfo("/keys"), b =>
                    b.SetApplicationName(input.ApplicationName + ".verification-wrong").DisableAutomaticKeyGeneration());
                MustNotDecrypt(wrong.CreateProtector(purpose), payload);
            }

            var start = new ProcessStartInfo("dotnet", "/app/Pos.Backend.Api.dll")
            { WorkingDirectory = "/app", RedirectStandardOutput = true, RedirectStandardError = true };
            backend = Process.Start(start)!;
            // Child logs are drained and discarded, never buffered, returned or persisted.
            _ = Discard(backend.StandardOutput);
            _ = Discard(backend.StandardError);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
                { BaseAddress = new Uri("http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(5) };
            var ready = false;
            for (var attempt = 0; attempt < 45; attempt++)
            {
                Require(!backend.HasExited);
                try { ready = (await client.GetAsync("/health/ready", deadline.Token)).StatusCode == HttpStatusCode.OK; }
                catch (HttpRequestException) { }
                if (ready) break;
                await Task.Delay(500, deadline.Token);
            }
            Require(ready);
            var platform = await Authenticate(client, "/api/platform/auth", input.Platform, deadline.Token);
            var tenant = await Authenticate(client, "/api/Auth", input.Tenant, deadline.Token);
            await Task.Delay(2500, deadline.Token); // Cover more than one unconditional worker polling interval.
            Require(!backend.HasExited && keyHashes.SequenceEqual(KeyHashes()) && Directory.GetDirectories("/keys").Length == 0);
            Console.WriteLine(JsonSerializer.Serialize(new { network = "PASS_IPV4_IPV6", keyring = "PASS_BYTES",
                backend = "PASS_HTTP", platform, tenant, protectedValue = input.Payloads.Length == 0 ? "NOT_PRESENT" : "PASS_EXISTING_SMTP",
                worker = "REGISTERED_DB_CLAIMS_DENIED", authWrites = "NONE_ALLOWED" }));
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine("STOP_APP_VERIFICATION");
            return 1;
        }
        finally
        {
            if (backend is not null)
            {
                if (!backend.HasExited) backend.Kill(entireProcessTree: true);
                await backend.WaitForExitAsync();
                backend.Dispose();
            }
        }
    }

    private static async Task<string> Authenticate(HttpClient client, string path, Login? login, CancellationToken ct)
    {
        if (login is null) return "EMPTY";
        using var body = new StringContent(JsonSerializer.Serialize(new { username = login.Username, password = login.Password }),
            System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path + "/login", body, ct);
        Require(response.StatusCode == HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        Require(bytes.Length <= 32768);
        using var json = JsonDocument.Parse(bytes);
        var token = json.RootElement.GetProperty("token").GetString();
        Require(!string.IsNullOrWhiteSpace(token));
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "/me");
        request.Headers.Authorization = new("Bearer", token);
        using var me = await client.SendAsync(request, ct);
        Require(me.StatusCode == HttpStatusCode.OK);
        CryptographicOperations.ZeroMemory(bytes);
        return "PASS_HTTP_LOGIN_ME";
    }

    private static string[] KeyHashes() => Directory.GetFiles("/keys").Order()
        .Select(path => Path.GetFileName(path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static void MustNotDecrypt(IDataProtector protector, string payload)
    {
        try { protector.Unprotect(payload); }
        catch (CryptographicException) { return; }
        throw new InvalidOperationException("STOP");
    }

    private static async Task Discard(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) != 0) Array.Clear(buffer);
    }

    private static async Task NetworkProof(CancellationToken ct)
    {
        Require(NetworkInterface.GetAllNetworkInterfaces().All(n => n.Name == "lo"));
        Require(Directory.GetDirectories("/sys/class/net").Select(Path.GetFileName).SequenceEqual(new[] { "lo" }));
        var ipv4 = File.ReadAllLines("/proc/net/route").Skip(1).Where(s => !string.IsNullOrWhiteSpace(s));
        Require(ipv4.All(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0] == "lo"));
        var ipv6 = File.ReadAllLines("/proc/net/ipv6_route").Where(s => !string.IsNullOrWhiteSpace(s));
        Require(ipv6.All(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1] == "lo"));
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(address, 5432), ct);
            Require(socket.Connected);
        }
        foreach (var address in new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("2001:db8::1") })
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, 5432), ct); }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.HostUnreachable)
            { continue; }
            throw new InvalidOperationException("STOP");
        }
        using var unused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try { await unused.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8080), ct); }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused) { return; }
        throw new InvalidOperationException("STOP");
    }
}
