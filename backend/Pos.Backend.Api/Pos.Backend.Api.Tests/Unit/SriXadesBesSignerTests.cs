using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SriXadesBesSignerTests
{
    private const string AccessKey = "2109202601179001234500110010010000000011234567811";
    private const string DsNamespace = SignedXml.XmlDsigNamespaceUrl;
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private static readonly DateTime SigningTimeUtc = new(2026, 9, 21, 12, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("factura")]
    [InlineData("notaCredito")]
    public void SignXml_creates_a_verifiable_xades_signature_with_synthetic_material(string rootName)
    {
        using var certificate = CreateSyntheticCertificate();
        var signer = new SriXadesBesSigner();
        var unsignedXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <{rootName} id="comprobante" version="1.0.0">
              <infoTributaria>
                <claveAcceso>{AccessKey}</claveAcceso>
              </infoTributaria>
            </{rootName}>
            """;

        var signedXml = rootName == "factura"
            ? signer.SignInvoiceXml(unsignedXml, certificate, AccessKey, SigningTimeUtc)
            : signer.SignCreditNoteXml(unsignedXml, certificate, AccessKey, SigningTimeUtc);

        var document = LoadDocument(signedXml);
        var signatureElement = Assert.Single(document
            .GetElementsByTagName("Signature", DsNamespace)
            .OfType<XmlElement>());
        var verifier = new SignedXml(document);
        verifier.LoadXml(signatureElement);

        Assert.True(verifier.CheckSignature(certificate, verifySignatureOnly: true));
        Assert.Equal(SignedXml.XmlDsigC14NTransformUrl, verifier.SignedInfo?.CanonicalizationMethod);
        Assert.Equal(SignedXml.XmlDsigRSASHA256Url, verifier.SignedInfo?.SignatureMethod);

        var references = verifier.SignedInfo?.References
            .OfType<Reference>()
            .ToArray() ?? [];

        Assert.Equal(2, references.Length);
        Assert.Contains(references, reference => reference.Uri == "#comprobante");
        Assert.Contains(references, reference =>
            reference.Type == "http://uri.etsi.org/01903#SignedProperties");
        Assert.All(references, reference =>
            Assert.Equal(SignedXml.XmlDsigSHA256Url, reference.DigestMethod));

        var signingTime = Assert.Single(document
            .GetElementsByTagName("SigningTime", XadesNamespace)
            .OfType<XmlElement>());
        Assert.Equal("2026-09-21T12:30:00Z", signingTime.InnerText);

        var encodedCertificate = Assert.Single(document
            .GetElementsByTagName("X509Certificate", DsNamespace)
            .OfType<XmlElement>());
        Assert.Equal(certificate.RawData, Convert.FromBase64String(encodedCertificate.InnerText));

        document.DocumentElement!.SetAttribute("version", "tampered");
        var tamperedVerifier = new SignedXml(document);
        tamperedVerifier.LoadXml(signatureElement);
        Assert.False(tamperedVerifier.CheckSignature(certificate, verifySignatureOnly: true));
    }

    private static X509Certificate2 CreateSyntheticCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=HFPOS synthetic signing test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            critical: true));

        return request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static XmlDocument LoadDocument(string xml)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null
        };
        document.LoadXml(xml);
        return document;
    }
}
