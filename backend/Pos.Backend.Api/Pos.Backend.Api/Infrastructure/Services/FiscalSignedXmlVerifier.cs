using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class FiscalSignedXmlVerifier
{
    public static X509Certificate2 Verify(string xml, string persistedHash, string persistedThumbprint,
        string accessKey, string ruc, string rootName, int environment)
    {
        try
        {
            if (accessKey.Length != 49 || accessKey.Any(c => c is < '0' or > '9')
                || environment is not (1 or 2) || accessKey[23] - '0' != environment
                || accessKey.Substring(10, 13) != ruc
                || accessKey.Substring(8, 2) != (rootName == "factura" ? "01" : "04")
                || new SriAccessKeyService().CalculateModulo11CheckDigit(accessKey[..48]) != accessKey[48] - '0')
                throw new CryptographicException();
            if (xml.Length > 4_194_304 || string.IsNullOrEmpty(persistedHash)
                || !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))), persistedHash, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException();
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_194_304 });
            document.Load(reader);
            var root = document.DocumentElement;
            if (root?.Name != rootName || root.NamespaceURI != "" || root.GetAttribute("id") != "comprobante"
                || document.SelectSingleNode($"/{rootName}/infoTributaria/claveAcceso")?.InnerText != accessKey
                || document.SelectSingleNode($"/{rootName}/infoTributaria/ruc")?.InnerText != ruc)
                throw new CryptographicException();
            var ids = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            foreach (XmlElement element in document.GetElementsByTagName("*"))
            foreach (var name in new[] { "id", "Id", "ID" })
                if (element.HasAttribute(name) && !ids.TryAdd(element.GetAttribute(name), element)) throw new CryptographicException();
            var signatures = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            var certs = document.GetElementsByTagName("X509Certificate", SignedXml.XmlDsigNamespaceUrl);
            if (signatures.Count != 1 || certs.Count != 1 || signatures[0]?.ParentNode != root)
                throw new CryptographicException();
            var signature = (XmlElement)signatures[0]!;
            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            ns.AddNamespace("xades", "http://uri.etsi.org/01903/v1.3.2#");
            if (signature.SelectNodes("ds:KeyInfo/ds:X509Data/ds:X509Certificate", ns)?.Count != 1)
                throw new CryptographicException();
            var signed = new SignedXml(document) { Resolver = null! };
            signed.LoadXml(signature);
            if (signed.SignedInfo?.SignatureMethod != SignedXml.XmlDsigRSASHA256Url
                || signed.SignedInfo.CanonicalizationMethod != SignedXml.XmlDsigC14NTransformUrl
                || signed.SignedInfo.References.Count != 2) throw new CryptographicException();
            var references = signed.SignedInfo.References.Cast<Reference>().ToArray();
            var data = references.Single(r => r.Uri == "#comprobante");
            var properties = references.Single(r => r.Type == "http://uri.etsi.org/01903#SignedProperties");
            if (properties.Uri is null || !properties.Uri.StartsWith('#')
                || !ids.TryGetValue(properties.Uri[1..], out var target) || target.LocalName != "SignedProperties"
                || target.NamespaceURI != "http://uri.etsi.org/01903/v1.3.2#" || !signature.Contains(target))
                throw new CryptographicException();
            if (references.Any(r => r.DigestMethod != SignedXml.XmlDsigSHA256Url)
                || data.TransformChain.Count != 2 || properties.TransformChain.Count != 1
                || data.TransformChain[0].Algorithm != SignedXml.XmlDsigEnvelopedSignatureTransformUrl
                || data.TransformChain[1].Algorithm != SignedXml.XmlDsigC14NTransformUrl
                || properties.TransformChain[0].Algorithm != SignedXml.XmlDsigExcC14NTransformUrl)
                throw new CryptographicException();
            var cert = new X509Certificate2(Convert.FromBase64String(certs[0]!.InnerText));
            try
            {
                using var key = cert.GetRSAPublicKey();
                if (!string.Equals(cert.Thumbprint, persistedThumbprint, StringComparison.OrdinalIgnoreCase)
                    || key?.KeySize is not (>= 2048 and <= 8192) || !signed.CheckSignature(cert, true)) throw new CryptographicException();
                var digest = signature.SelectSingleNode("ds:Object/xades:QualifyingProperties/xades:SignedProperties/xades:SignedSignatureProperties/xades:SigningCertificate/xades:Cert/xades:CertDigest/ds:DigestValue", ns);
                if (digest?.InnerText != Convert.ToBase64String(SHA256.HashData(cert.RawData))) throw new CryptographicException();
                return cert;
            }
            catch { cert.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is CryptographicException or XmlException or FormatException or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException("FISCAL_SIGNED_XML_INVALID", ex); }
    }

    private static bool Contains(this XmlElement ancestor, XmlElement element)
    {
        for (XmlNode? node = element; node is not null; node = node.ParentNode) if (node == ancestor) return true;
        return false;
    }
}
