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
using Microsoft.AspNetCore.WebUtilities;

// Verification-only console. It never seeds, signs, submits, sends mail or exposes HTTP.
internal static class Program
{
    private sealed record Login(string Username, string Password);
    private sealed record CertificatePayload(string Certificate, string Password);
    private sealed record Input(Dictionary<string, string> Keys, string ApplicationName,
        string[] Payloads, CertificatePayload[] Certificates, Login? Platform, Login? Tenant, JsonElement? Business);

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
            // Independent of cooperative async cancellation. PID1 exit also ends this container's child process.
            using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(140), Timeout.InfiniteTimeSpan);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            var line = await Console.In.ReadLineAsync(deadline.Token);
            Require(line is { Length: > 0 and <= 16777216 });
            var input = JsonSerializer.Deserialize<Input>(line!) ?? throw new InvalidOperationException("STOP");
            Require(input.Keys.Count is > 0 and <= 128
                && input.Payloads.Length <= 128 && input.Certificates.Length <= 128 && input.ApplicationName.Length is > 0 and <= 200);
            await NetworkProof(deadline.Token);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)))
                .Equals(Environment.GetEnvironmentVariable("HFPOS_VERIFY_PROBE_SHA256"), StringComparison.OrdinalIgnoreCase));
            var totalKeyBytes = 0;
            foreach (var (name, encoded) in input.Keys)
            {
                Require(System.Text.RegularExpressions.Regex.IsMatch(name, @"^key-[a-zA-Z0-9-]+\.xml$"));
                var bytes = Convert.FromBase64String(encoded);
                totalKeyBytes += bytes.Length;
                Require(bytes.Length is > 0 and <= 1048576 && totalKeyBytes <= 4 * 1024 * 1024);
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
            var certificatePurpose = (string)assembly.GetType("Pos.Backend.Api.Core.Security.SriCertificateProtectionPurposes", true)!
                .GetField("CertificateV1", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
            Require(certificatePurpose == "hfpos.sri.certificate.v1");
            var protection = DataProtectionProvider.Create(new DirectoryInfo("/keys"), b =>
                b.SetApplicationName(input.ApplicationName).DisableAutomaticKeyGeneration());
            var wrong = DataProtectionProvider.Create(new DirectoryInfo("/keys"), b =>
                b.SetApplicationName(input.ApplicationName + ".verification-wrong").DisableAutomaticKeyGeneration());
            foreach (var payload in input.Payloads)
            {
                Require(payload.Length is > 0 and <= 65536);
                var bytes = WebEncoders.Base64UrlDecode(payload);
                try { VerifyProtected(protection, wrong, purpose, bytes, 65536); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            foreach (var certificate in input.Certificates)
            {
                Require(certificate.Certificate.Length is > 0 and <= 3000000 && certificate.Password.Length is > 0 and <= 90000);
                var bytes = Convert.FromBase64String(certificate.Certificate);
                try { VerifyProtected(protection, wrong, certificatePurpose, bytes, 2 * 1024 * 1024 + 65536); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                var password = Convert.FromBase64String(certificate.Password);
                try { VerifyProtected(protection, wrong, certificatePurpose, password, 65536); }
                finally { CryptographicOperations.ZeroMemory(password); }
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
            var businessRead = await ReadBusiness(client, tenant.Token, input.Business, deadline.Token);
            await Task.Delay(2500, deadline.Token); // Cover more than one unconditional worker polling interval.
            Require(!backend.HasExited && keyHashes.SequenceEqual(KeyHashes()) && Directory.GetDirectories("/keys").Length == 0);
            Console.WriteLine(JsonSerializer.Serialize(new { network = "PASS_IPV4_IPV6", keyring = "PASS_BYTES",
                backend = "PASS_HTTP", platform = platform.Status, tenant = tenant.Status, businessRead,
                protectedValue = input.Certificates.Length > 0 ? "PASS_EXISTING_CERTIFICATE" : (input.Payloads.Length == 0 ? "NOT_PRESENT" : "PASS_EXISTING_SMTP"),
                smtp = input.Payloads.Length == 0 ? "NOT_PRESENT" : "PASS_EXISTING_SMTP",
                certificate = input.Certificates.Length == 0 ? "NOT_PRESENT" : "PASS_EXISTING_CERTIFICATE",
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

    private static async Task<(string Status, string? Token)> Authenticate(HttpClient client, string path, Login? login, CancellationToken ct)
    {
        if (login is null) return ("EMPTY", null);
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
        return ("PASS_HTTP_LOGIN_ME", token);
    }

    private static async Task<string> ReadBusiness(HttpClient client, string? token, JsonElement? sample, CancellationToken ct)
    {
        if (sample is null) return "EMPTY";
        Require(token is not null);
        var expected = sample.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Sales/" + expected.GetProperty("id").GetInt32());
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden) return "PENDING_PERMISSION";
        Require(response.StatusCode == HttpStatusCode.OK);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            Require(buffer.Length + count <= 1048576);
            buffer.Write(chunk, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        foreach (var field in expected.EnumerateObject())
        {
            var actual = json.RootElement.GetProperty(field.Name);
            if (field.Name is "total" or "subtotal" or "taxAmount")
                Require(actual.GetDecimal() == decimal.Parse(field.Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            else Require(actual.GetInt32() == field.Value.GetInt32());
        }
        return "PASS_EXISTING_SALE";
    }

    private static string[] KeyHashes() => Directory.GetFiles("/keys").Order()
        .Select(path => Path.GetFileName(path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static void VerifyProtected(IDataProtectionProvider provider, IDataProtectionProvider wrongApplication,
        string purpose, byte[] payload, int limit)
    {
        Require(payload.Length > 0 && payload.Length <= limit);
        byte[]? plaintext = null;
        try
        {
            plaintext = provider.CreateProtector(purpose).Unprotect(payload);
            Require(plaintext.Length > 0 && plaintext.Length <= limit);
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
        // No PFX parsing, private-key import, signing, export or network service is needed.
        foreach (var protector in new[] { provider.CreateProtector(purpose + ".verification-wrong"), wrongApplication.CreateProtector(purpose) })
        {
            byte[]? unexpected = null;
            try { unexpected = protector.Unprotect(payload); }
            catch (CryptographicException) { continue; }
            finally { if (unexpected is not null) CryptographicOperations.ZeroMemory(unexpected); }
            throw new InvalidOperationException("STOP");
        }
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
