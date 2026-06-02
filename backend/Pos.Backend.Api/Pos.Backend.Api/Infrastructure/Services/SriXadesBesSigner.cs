using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriXadesBesSigner : ISriXadesBesSigner
{
    private const string InvoiceRootName = "factura";
    private const string InvoiceRootId = "comprobante";
    private const string DsNamespace = SignedXml.XmlDsigNamespaceUrl;
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string XadesSignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";
    private const string SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
    private const string DigestMethod = SignedXml.XmlDsigSHA256Url;
    private const string CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;

    public string SignInvoiceXml(
        string unsignedXml,
        X509Certificate2 certificate,
        string accessKey,
        DateTime signingTimeUtc)
    {
        try
        {
            var xmlDocument = LoadXmlDocument(unsignedXml);
            var root = xmlDocument.DocumentElement;

            if (root is null
                || !string.Equals(root.Name, InvoiceRootName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(root.GetAttribute("id"), InvoiceRootId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
            }

            using var privateKey = certificate.GetRSAPrivateKey();

            if (privateKey is null)
            {
                throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
            }

            var idToken = BuildDeterministicIdToken(accessKey, unsignedXml);
            var signatureId = $"Signature-{idToken}";
            var certificateId = $"Certificate-{idToken}";
            var referenceId = $"Reference-{idToken}";
            var signedPropertiesId = $"SignedProperties-{idToken}";
            var xadesObjectId = $"XadesObject-{idToken}";

            var signedXml = new SriSignedXml(xmlDocument)
            {
                SigningKey = privateKey
            };

            signedXml.Signature.Id = signatureId;

            var signedInfo = signedXml.SignedInfo
                ?? throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");

            signedInfo.CanonicalizationMethod = CanonicalizationMethod;
            signedInfo.SignatureMethod = SignatureMethod;

            var invoiceReference = new Reference
            {
                Id = referenceId,
                Uri = $"#{InvoiceRootId}",
                DigestMethod = DigestMethod
            };

            invoiceReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            invoiceReference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(invoiceReference);

            var signedProperties = BuildSignedProperties(
                xmlDocument,
                certificate,
                NormalizeUtc(signingTimeUtc),
                signedPropertiesId,
                referenceId);

            var qualifyingProperties = xmlDocument.CreateElement("xades", "QualifyingProperties", XadesNamespace);
            qualifyingProperties.SetAttribute("Target", $"#{signatureId}");
            qualifyingProperties.AppendChild(signedProperties);

            var xadesObject = new DataObject
            {
                Id = xadesObjectId,
                Data = qualifyingProperties.SelectNodes(".")!
            };

            signedXml.RegisterIdElement(signedPropertiesId, signedProperties);
            signedXml.AddObject(xadesObject);

            var signedPropertiesReference = new Reference
            {
                Uri = $"#{signedPropertiesId}",
                Type = XadesSignedPropertiesType,
                DigestMethod = DigestMethod
            };

            // SignedProperties is embedded under ds:Object after digesting; exclusive C14N keeps ancestor namespaces stable.
            signedPropertiesReference.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(signedPropertiesReference);

            signedXml.KeyInfo = BuildKeyInfo(xmlDocument, certificate, certificateId);

            signedXml.ComputeSignature();

            var signatureElement = signedXml.GetXml();
            PrefixUnsignedKeyInfo(signatureElement);
            root.AppendChild(xmlDocument.ImportNode(signatureElement, true));

            AssertXadesMetadata(xmlDocument);
            AssertKeyInfoCertificate(xmlDocument, certificate);
            VerifySignature(xmlDocument, certificate);

            return xmlDocument.OuterXml;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED", ex);
        }
    }

    private static XmlElement BuildSignedProperties(
        XmlDocument document,
        X509Certificate2 certificate,
        DateTime signingTimeUtc,
        string signedPropertiesId,
        string referenceId)
    {
        var signedProperties = document.CreateElement("xades", "SignedProperties", XadesNamespace);
        signedProperties.SetAttribute("Id", signedPropertiesId);

        var signedSignatureProperties = document.CreateElement("xades", "SignedSignatureProperties", XadesNamespace);
        signedSignatureProperties.AppendChild(CreateTextElement(
            document,
            "xades",
            "SigningTime",
            XadesNamespace,
            signingTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        signedSignatureProperties.AppendChild(BuildSigningCertificate(document, certificate));

        var signedDataObjectProperties = document.CreateElement("xades", "SignedDataObjectProperties", XadesNamespace);
        var dataObjectFormat = document.CreateElement("xades", "DataObjectFormat", XadesNamespace);
        dataObjectFormat.SetAttribute("ObjectReference", $"#{referenceId}");
        dataObjectFormat.AppendChild(CreateTextElement(
            document,
            "xades",
            "Description",
            XadesNamespace,
            "Comprobante electronico"));
        dataObjectFormat.AppendChild(CreateTextElement(
            document,
            "xades",
            "MimeType",
            XadesNamespace,
            "text/xml"));
        signedDataObjectProperties.AppendChild(dataObjectFormat);

        signedProperties.AppendChild(signedSignatureProperties);
        signedProperties.AppendChild(signedDataObjectProperties);

        return signedProperties;
    }

    private static XmlElement BuildSigningCertificate(
        XmlDocument document,
        X509Certificate2 certificate)
    {
        var signingCertificate = document.CreateElement("xades", "SigningCertificate", XadesNamespace);
        var cert = document.CreateElement("xades", "Cert", XadesNamespace);

        var certDigest = document.CreateElement("xades", "CertDigest", XadesNamespace);
        var digestMethod = document.CreateElement("ds", "DigestMethod", DsNamespace);
        digestMethod.SetAttribute("Algorithm", DigestMethod);
        certDigest.AppendChild(digestMethod);
        certDigest.AppendChild(CreateTextElement(
            document,
            "ds",
            "DigestValue",
            DsNamespace,
            Convert.ToBase64String(SHA256.HashData(certificate.RawData))));

        var issuerSerial = document.CreateElement("xades", "IssuerSerial", XadesNamespace);
        issuerSerial.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509IssuerName",
            DsNamespace,
            certificate.Issuer));
        issuerSerial.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509SerialNumber",
            DsNamespace,
            GetCertificateSerialNumber(certificate)));

        cert.AppendChild(certDigest);
        cert.AppendChild(issuerSerial);
        signingCertificate.AppendChild(cert);

        return signingCertificate;
    }

    private static KeyInfo BuildKeyInfo(
        XmlDocument document,
        X509Certificate2 certificate,
        string certificateId)
    {
        var keyInfo = new KeyInfo
        {
            Id = certificateId
        };

        var x509Data = document.CreateElement("ds", "X509Data", DsNamespace);
        x509Data.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509Certificate",
            DsNamespace,
            Convert.ToBase64String(certificate.RawData)));
        x509Data.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509SubjectName",
            DsNamespace,
            certificate.Subject));

        var issuerSerial = document.CreateElement("ds", "X509IssuerSerial", DsNamespace);
        issuerSerial.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509IssuerName",
            DsNamespace,
            certificate.Issuer));
        issuerSerial.AppendChild(CreateTextElement(
            document,
            "ds",
            "X509SerialNumber",
            DsNamespace,
            GetCertificateSerialNumber(certificate)));
        x509Data.AppendChild(issuerSerial);

        keyInfo.AddClause(new KeyInfoNode(x509Data));

        return keyInfo;
    }

    private static XmlElement CreateTextElement(
        XmlDocument document,
        string prefix,
        string localName,
        string namespaceUri,
        string value)
    {
        var element = document.CreateElement(prefix, localName, namespaceUri);
        element.InnerText = value;

        return element;
    }

    private static XmlDocument LoadXmlDocument(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        document.Load(xmlReader);

        return document;
    }

    private static void VerifySignature(
        XmlDocument xmlDocument,
        X509Certificate2 certificate)
    {
        var signatureNode = xmlDocument
            .GetElementsByTagName("Signature", DsNamespace)
            .OfType<XmlElement>()
            .FirstOrDefault();

        if (signatureNode is null)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
        }

        var signedXml = new SriSignedXml(xmlDocument);
        signedXml.LoadXml(signatureNode);

        if (!signedXml.CheckSignature(certificate, true))
        {
            throw new InvalidOperationException("SRI_SIGNATURE_VALIDATION_FAILED");
        }
    }

    private static void PrefixUnsignedKeyInfo(XmlElement signatureElement)
    {
        var keyInfo = signatureElement
            .GetElementsByTagName("KeyInfo", DsNamespace)
            .OfType<XmlElement>()
            .FirstOrDefault();

        if (keyInfo is null)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
        }

        SetDsPrefix(keyInfo);
    }

    private static void SetDsPrefix(XmlElement element)
    {
        if (string.Equals(element.NamespaceURI, DsNamespace, StringComparison.Ordinal))
        {
            element.Prefix = "ds";
        }

        foreach (var child in element.ChildNodes.OfType<XmlElement>())
        {
            SetDsPrefix(child);
        }
    }

    private static void AssertXadesMetadata(XmlDocument xmlDocument)
    {
        var hasQualifyingProperties = xmlDocument
            .GetElementsByTagName("QualifyingProperties", XadesNamespace)
            .Count > 0;
        var hasSignedProperties = xmlDocument
            .GetElementsByTagName("SignedProperties", XadesNamespace)
            .Count > 0;
        var hasSignedPropertiesReference = xmlDocument
            .GetElementsByTagName("Reference", DsNamespace)
            .OfType<XmlElement>()
            .Any(reference =>
                string.Equals(reference.GetAttribute("Type"), XadesSignedPropertiesType, StringComparison.Ordinal)
                && reference.GetAttribute("URI").StartsWith("#SignedProperties-", StringComparison.Ordinal));

        if (!hasQualifyingProperties || !hasSignedProperties || !hasSignedPropertiesReference)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
        }
    }

    private static void AssertKeyInfoCertificate(
        XmlDocument xmlDocument,
        X509Certificate2 expectedCertificate)
    {
        var encodedCertificate = xmlDocument
            .GetElementsByTagName("X509Certificate", DsNamespace)
            .OfType<XmlElement>()
            .Where(IsInsideKeyInfo)
            .Select(element => element.InnerText)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(encodedCertificate))
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
        }

        var normalizedCertificate = new string(encodedCertificate
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray());

        byte[] certificateBytes;

        try
        {
            certificateBytes = Convert.FromBase64String(normalizedCertificate);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED", ex);
        }

        try
        {
            using var decodedCertificate = new X509Certificate2(certificateBytes);

            if (!string.Equals(
                decodedCertificate.Thumbprint,
                expectedCertificate.Thumbprint,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SRI_SIGNATURE_VALIDATION_FAILED");
            }
        }
        finally
        {
            Array.Clear(certificateBytes);
        }
    }

    private static bool IsInsideKeyInfo(XmlElement element)
    {
        for (var parent = element.ParentNode; parent is not null; parent = parent.ParentNode)
        {
            if (parent is XmlElement parentElement
                && string.Equals(parentElement.LocalName, "KeyInfo", StringComparison.Ordinal)
                && string.Equals(parentElement.NamespaceURI, DsNamespace, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildDeterministicIdToken(string accessKey, string unsignedXml)
    {
        if (!string.IsNullOrWhiteSpace(accessKey))
        {
            var normalized = new string(accessKey.Where(char.IsLetterOrDigit).ToArray());

            if (normalized.Length > 0)
            {
                return normalized;
            }
        }

        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(unsignedXml));
        return Convert.ToHexString(hash)[..24];
    }

    private static string GetCertificateSerialNumber(X509Certificate2 certificate)
    {
        var serialNumber = new BigInteger(certificate.GetSerialNumber(), isUnsigned: true, isBigEndian: false);
        return serialNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private sealed class SriSignedXml : SignedXml
    {
        private readonly Dictionary<string, XmlElement> _idElements = new(StringComparer.Ordinal);

        public SriSignedXml(XmlDocument document)
            : base(document)
        {
        }

        public void RegisterIdElement(string id, XmlElement element)
        {
            _idElements[id] = element;
        }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            var element = document is null
                ? null
                : base.GetIdElement(document, idValue);

            if (element is not null)
            {
                return element;
            }

            if (_idElements.TryGetValue(idValue, out element))
            {
                return element;
            }

            return document is null
                ? null
                : FindElementByIdAttribute(document, idValue);
        }

        private static XmlElement? FindElementByIdAttribute(XmlDocument document, string idValue)
        {
            foreach (XmlElement element in document.GetElementsByTagName("*"))
            {
                if (string.Equals(element.GetAttribute("Id"), idValue, StringComparison.Ordinal)
                    || string.Equals(element.GetAttribute("ID"), idValue, StringComparison.Ordinal)
                    || string.Equals(element.GetAttribute("id"), idValue, StringComparison.Ordinal))
                {
                    return element;
                }
            }

            return null;
        }
    }
}
