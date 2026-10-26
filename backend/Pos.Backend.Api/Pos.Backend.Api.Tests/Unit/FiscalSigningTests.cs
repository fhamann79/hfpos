using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class FiscalSigningTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("native", "CERTIFICATE_CHAIN_BUILD_FAILED", "warning")]
    [InlineData("success", "CERTIFICATE_CHAIN_BUILD_OK", "success")]
    [InlineData("exception", "CERTIFICATE_CHAIN_DIAGNOSTIC_FAILED", "warning")]
    public async Task Chain_diagnostic_never_changes_signing_eligibility(string outcome, string code, string severity)
    {
        using var p = new SyntheticSigningCertificates();
        var result = await Diagnose(p.Leaf, outcome);
        var check = Assert.Single(result.Checks.Where(c => c.Code == code));
        Assert.Equal(severity, check.Severity); Assert.False(check.IsBlocking);
        Assert.True(result.Usable); Assert.DoesNotContain(result.Checks, c => c.IsBlocking);
        if (outcome == "native") Assert.Contains("PartialChain", check.Details);
        output.WriteLine($"{code}: {severity}, IsBlocking=false, IsUsableForProduction={result.Usable}");
    }

    [Fact]
    public async Task Self_issued_unknown_root_and_unconfirmed_identity_are_only_warnings()
    {
        using var p = new SyntheticSigningCertificates();
        var result = await Diagnose(p.Root);
        Assert.True(result.Usable);
        Assert.Contains(result.Checks, c => c.Code == "CERTIFICATE_SELF_SIGNED" && c.Severity == "warning" && !c.IsBlocking);
        Assert.Contains(result.Checks, c => c.Code == "CERTIFICATE_RUC_NOT_CONFIRMED" && !c.IsBlocking);
        Assert.Contains(result.Checks, c => c.Code == "CERTIFICATE_CHAIN_BUILD_FAILED" && c.Details!.Contains("UntrustedRoot"));
    }

    [Theory]
    [InlineData("public", "CERTIFICATE_WITHOUT_PRIVATE_KEY")]
    [InlineData("expired", "CERTIFICATE_EXPIRED")]
    [InlineData("future", "CERTIFICATE_NOT_YET_VALID")]
    [InlineData("weak-rsa", "CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED")]
    [InlineData("ecdsa", "CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED")]
    [InlineData("identity", "CERTIFICATE_RUC_MISMATCH")]
    public async Task Unusable_material_blocks_and_skips_chain_diagnostic(string failure, string code)
    {
        using var p = new SyntheticSigningCertificates();
        using var ec = ECDsa.Create();
        using var cert = failure switch
        {
            "public" => new X509Certificate2(p.Leaf.RawData),
            "expired" => p.Issue("Synthetic expired", p.Root, until: p.Now.AddDays(-1)),
            "future" => p.Issue("Synthetic future", p.Root, from: p.Now.AddDays(1)),
            "weak-rsa" => p.Issue("Synthetic weak RSA", keySize: 1024),
            "ecdsa" => new CertificateRequest("CN=Synthetic EC signer", ec, HashAlgorithmName.SHA256)
                .CreateSelfSigned(p.Now.AddDays(-1), p.Now.AddDays(10)),
            _ => p.Issue("Synthetic different taxpayer", p.Root, ruc: SyntheticSigningCertificates.RucB)
        };
        var result = await Diagnose(cert, "must-not-run", metadata: p.Leaf);
        Assert.False(result.Usable);
        Assert.Contains(result.Checks, c => c.Code == code && c.Severity == "error" && c.IsBlocking);
        Assert.DoesNotContain(result.Checks, c => c.Code.StartsWith("CERTIFICATE_CHAIN_", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_key_must_sign_and_match_the_certificate_public_key()
    {
        using var actual = RSA.Create(2048); using var wrong = RSA.Create(2048);
        FiscalDocumentGuard.VerifyKeyPair(actual, actual);
        Assert.Equal("CERTIFICATE_PRIVATE_KEY_MISMATCH", Assert.Throws<InvalidOperationException>(
            () => FiscalDocumentGuard.VerifyKeyPair(wrong, actual)).Message);
        using var publicOnly = RSA.Create(); publicOnly.ImportParameters(actual.ExportParameters(false));
        Assert.Throws<CryptographicException>(() => FiscalDocumentGuard.VerifyKeyPair(publicOnly, actual));
    }

    [Fact]
    public void RSA_signer_is_usable_even_when_issuer_signs_certificate_with_ECDSA()
    {
        using var issuerKey = ECDsa.Create();
        var issuerRequest = new CertificateRequest("CN=Synthetic ECDSA issuer", issuerKey, HashAlgorithmName.SHA256);
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(20));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Synthetic RSA taxpayer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var publicCert = request.Create(issuer.SubjectName, X509SignatureGenerator.CreateForECDsa(issuerKey),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), [1]);
        using var cert = publicCert.CopyWithPrivateKey(rsa);
        Assert.Equal("1.2.840.10045.4.3.2", cert.SignatureAlgorithm.Value);
        FiscalDocumentGuard.RequireSigningCertificate(cert, SyntheticSigningCertificates.RucA, DateTime.UtcNow);
    }

    [Fact]
    public void Pfx_selects_only_one_ephemeral_signer_and_discards_auxiliaries()
    {
        using var p = new SyntheticSigningCertificates();
        using var intermediate = new X509Certificate2(p.Intermediate.RawData);
        using var root = new X509Certificate2(p.Root.RawData);
        using var material = SriPfxReader.Load(p.Pfx(p.Leaf, intermediate, root), SyntheticSigningCertificates.Password);
        Assert.Equal(p.Leaf.RawData, material.Certificate.RawData);
        FiscalDocumentGuard.RequireSigningCertificate(material.Certificate, SyntheticSigningCertificates.RucA, p.Now);
        Assert.Equal("CERTIFICATE_SIGNER_AMBIGUOUS", Assert.Throws<InvalidOperationException>(() =>
            SriPfxReader.Load(p.Pfx(p.Leaf, p.Intermediate), SyntheticSigningCertificates.Password)).Message);
        Assert.Equal("CERTIFICATE_WITHOUT_PRIVATE_KEY", Assert.Throws<InvalidOperationException>(() =>
            SriPfxReader.Load(p.Pfx(root), SyntheticSigningCertificates.Password)).Message);
        Assert.Throws<CryptographicException>(() => SriPfxReader.Load(p.Pfx(p.Leaf), "synthetic-wrong-password"));
        Assert.Throws<CryptographicException>(() => SriPfxReader.Load([1, 2, 3], SyntheticSigningCertificates.Password));
        Assert.Throws<CryptographicException>(() => SriPfxReader.Load(new byte[2_097_153], SyntheticSigningCertificates.Password));
        Assert.Equal("CERTIFICATE_COLLECTION_LIMIT", Assert.Throws<InvalidOperationException>(() =>
            SriPfxReader.Load(p.Pfx([p.Leaf, .. Enumerable.Range(0, 33).Select(_ => root)]), SyntheticSigningCertificates.Password)).Message);
    }

    [Theory]
    [InlineData("factura")] [InlineData("notaCredito")]
    public void Actual_signer_roundtrip_and_tamper_guards(string root)
    {
        using var p = new SyntheticSigningCertificates();
        var key = AccessKey(root);
        var xml = Xml(root, key);
        var signer = new SriXadesBesSigner();
        var signed = root == "factura" ? signer.SignInvoiceXml(xml, p.Leaf, key, p.Now) : signer.SignCreditNoteXml(xml, p.Leaf, key, p.Now);
        using var certificate = FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Leaf.Thumbprint, key, SyntheticSigningCertificates.RucA, root, 2);
        Assert.Equal(p.Leaf.RawData, certificate.RawData);
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(signed, "00", p.Leaf.Thumbprint, key, SyntheticSigningCertificates.RucA, root, 2));
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Root.Thumbprint, key, SyntheticSigningCertificates.RucA, root, 2));
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Leaf.Thumbprint, key, SyntheticSigningCertificates.RucA, root, 1));
        foreach (var malformed in new[]
        {
            signed.Replace("<ruc>", "<ruc>9"),
            signed.Replace("URI=\"#comprobante\"", "URI=\"http://127.0.0.1:9/ssrf\""),
            signed.Replace("id=\"comprobante\"", "id=\"comprobante\" Id=\"comprobante\""),
            signed.Replace("rsa-sha256", "rsa-sha1"),
            signed.Replace("enveloped-signature", "xpath"),
            signed.Replace("</ds:X509Data>", $"<ds:X509Certificate>{Convert.ToBase64String(p.Root.RawData)}</ds:X509Certificate></ds:X509Data>")
        })
        {
            Assert.NotEqual(signed, malformed);
            Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(malformed, Hash(malformed), p.Leaf.Thumbprint, key, SyntheticSigningCertificates.RucA, root, 2));
        }
    }

    [Theory]
    [InlineData("RECIBIDA")] [InlineData("DEVUELTA")]
    public async Task PartialChain_allows_actual_signature_and_mock_SOAP_transport_without_network(string status)
    {
        using var p = new SyntheticSigningCertificates();
        var result = await Diagnose(p.Leaf);
        Assert.True(result.Usable);
        Assert.Contains(result.Checks, c => c.Code == "CERTIFICATE_CHAIN_BUILD_FAILED" && !c.IsBlocking && c.Details!.Contains("PartialChain"));
        var key = AccessKey("factura");
        var signed = new SriXadesBesSigner().SignInvoiceXml(Xml("factura", key), p.Leaf, key, p.Now);
        using var verified = FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Leaf.Thumbprint, key, SyntheticSigningCertificates.RucA, "factura", 2);
        using var handler = new MockReception(status, signed);
        using var http = new HttpClient(handler);
        var client = new SriWebServiceClient(http, Options.Create(new SriOptions
        { AllowProductionSubmission = true, ReceptionProductionUrl = "https://synthetic.invalid/reception" }), NullLogger<SriWebServiceClient>.Instance);
        var response = await client.SubmitAsync(signed, 2);
        Assert.Equal(status, response.Estado); Assert.Equal(1, handler.Calls);
        Assert.Equal(status == "RECIBIDA", response.IsReceived);
        output.WriteLine($"PartialChain warning -> RSA-SHA256/XML verified -> mock SOAP {status}; no network, no DB.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Xml(string root, string key) => $"<{root} id=\"comprobante\"><infoTributaria><claveAcceso>{key}</claveAcceso><ruc>{SyntheticSigningCertificates.RucA}</ruc></infoTributaria></{root}>";
    private static string AccessKey(string root)
    {
        var request = new SriAccessKeyRequest { DocumentCode = root == "factura" ? "01" : "04", IssuerRuc = SyntheticSigningCertificates.RucA,
            EmissionDate = DateTime.UtcNow, Environment = 2, EmissionType = 1, EstablishmentCode = "001", EmissionPointCode = "001", Sequential = 1, NumericCodeSeed = "synthetic" };
        var service = new SriAccessKeyService();
        return (root == "factura" ? service.GenerateInvoiceAccessKey(request) : service.GenerateCreditNoteAccessKey(request)).AccessKey;
    }

    private static async Task<(List<SriFiscalReadinessCheckDto> Checks, bool Usable)> Diagnose(X509Certificate2 cert,
        string outcome = "native", X509Certificate2? metadata = null)
    {
        var service = new DiagnosticProbe(new CertificateProvider(cert), outcome);
        metadata ??= cert;
        var stored = new CompanySriCertificate { HasPrivateKey = true, NotBefore = DateTime.UtcNow.AddDays(-1),
            NotAfter = DateTime.UtcNow.AddDays(20), Subject = metadata.Subject, Issuer = metadata.Issuer };
        var checks = new List<SriFiscalReadinessCheckDto>();
        var task = (Task)typeof(SriFiscalReadinessService).GetMethod("AddCertificateChecksAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [checks, new Company { Ruc = SyntheticSigningCertificates.RucA }, stored, 2, DateTime.UtcNow])!;
        await task;
        var diagnostics = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return (checks, (bool)diagnostics.GetType().GetProperty("IsUsableForProduction")!.GetValue(diagnostics)!);
    }

    private sealed class CertificateProvider(X509Certificate2 cert) : ISriSigningCertificateProvider
    {
        public Task<SriSigningCertificateMaterial> GetActiveCertificateMaterialAsync()
            => Task.FromResult(new SriSigningCertificateMaterial { Certificate = new X509Certificate2(cert) });
    }

    private sealed class DiagnosticProbe(ISriSigningCertificateProvider provider, string outcome) : SriFiscalReadinessService(
        null!, null!, provider, Options.Create(new SriOptions()), NullLogger<SriFiscalReadinessService>.Instance)
    {
        internal override bool BuildLocalChain(X509Chain chain, X509Certificate2 cert)
        {
            Assert.Equal(X509RevocationMode.NoCheck, chain.ChainPolicy.RevocationMode);
            Assert.Equal(X509VerificationFlags.NoFlag, chain.ChainPolicy.VerificationFlags);
            Assert.True(chain.ChainPolicy.DisableCertificateDownloads);
            Assert.Empty(chain.ChainPolicy.ExtraStore); Assert.Empty(chain.ChainPolicy.CustomTrustStore);
            Assert.Equal(X509ChainTrustMode.System, chain.ChainPolicy.TrustMode);
            return outcome switch { "success" => true, "exception" => throw new CryptographicException("synthetic-private-detail"),
                "must-not-run" => throw new Xunit.Sdk.XunitException("Unusable material must skip chain diagnostic."), _ => base.BuildLocalChain(chain, cert) };
        }
    }

    private sealed class MockReception(string status, string signed) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("synthetic.invalid", request.RequestUri!.Host);
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(signed)), await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"<respuesta><estado>{status}</estado></respuesta>") };
        }
    }
}
