using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class FiscalTrustTests
{
    [Fact]
    public void Two_independent_authorities_and_tenants_coexist_without_cross_identity()
    {
        using var a = new FiscalTrustSyntheticPki(); using var b = new FiscalTrustSyntheticPki(FiscalTrustSyntheticPki.RucB, "Synthetic root B");
        a.Manifest.Authorities.AddRange(b.Manifest.Authorities); a.Manifest.Crls.AddRange(b.Manifest.Crls);
        var evaluator = new FiscalCertificateEvaluator(a.Registry());
        Assert.True(evaluator.Evaluate(a.Leaf, [], FiscalTrustSyntheticPki.RucA, a.Now).Eligible);
        Assert.True(evaluator.Evaluate(b.Leaf, [], FiscalTrustSyntheticPki.RucB, a.Now).Eligible);
        var cross = evaluator.Evaluate(b.Leaf, [], FiscalTrustSyntheticPki.RucA, a.Now);
        Assert.False(cross.Eligible); Assert.Equal(FiscalIdentityState.Mismatch, cross.Identity);
    }

    [Theory]
    [InlineData(0x03)] [InlineData(0x80)] [InlineData(0xff)]
    public void Unsigned_high_bit_serials_are_good_and_detect_revocation(int serial)
    {
        using var pki = new FiscalTrustSyntheticPki(serial: [(byte)serial]);
        Assert.True(new FiscalCertificateEvaluator(pki.Registry()).Evaluate(pki.Leaf, [], FiscalTrustSyntheticPki.RucA, pki.Now).Eligible);
        pki.Manifest.Crls[1] = pki.Crl(pki.Intermediate, pki.Leaf);
        var decision = new FiscalCertificateEvaluator(pki.Registry()).Evaluate(pki.Leaf, [], FiscalTrustSyntheticPki.RucA, pki.Now);
        Assert.Equal(FiscalEvidenceState.Revoked, decision.Revocation); Assert.False(decision.Eligible);
    }

    [Theory]
    [InlineData("unknown")] [InlineData("stale")] [InlineData("forged")] [InlineData("hold")]
    [InlineData("intermediate-revoked")] [InlineData("freshest")] [InlineData("delta")]
    [InlineData("partition")] [InlineData("unknown-extension")] [InlineData("duplicate")]
    [InlineData("future")] [InlineData("wrong-aki")] [InlineData("remove-from-crl")]
    [InlineData("observed-stale")] [InlineData("number-rollback")] [InlineData("ocsp-required")]
    public void Revocation_never_fabricates_good(string scenario)
    {
        using var p = new FiscalTrustSyntheticPki();
        var expected = FiscalEvidenceState.Error;
        switch (scenario)
        {
            case "unknown": p.Manifest.Crls.RemoveAt(1); expected = FiscalEvidenceState.Unknown; break;
            case "stale": p.Manifest.Crls[1] = p.Crl(p.Intermediate, thisUpdate: p.Now.AddDays(-2), nextUpdate: p.Now.AddDays(-1)); expected = FiscalEvidenceState.Stale; break;
            case "forged": p.Manifest.Crls[1] = p.Crl(p.Intermediate, tamper: true); break;
            case "hold": p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf, X509RevocationReason.CertificateHold); expected = FiscalEvidenceState.Suspended; break;
            case "intermediate-revoked": p.Manifest.Crls[0] = p.Crl(p.Root, p.Intermediate); expected = FiscalEvidenceState.Revoked; break;
            case "freshest": p.Manifest.Crls[1] = p.Crl(p.Intermediate, extraOid: "2.5.29.46"); break;
            case "delta": p.Manifest.Crls[1] = p.Crl(p.Intermediate, extraOid: "2.5.29.27"); break;
            case "partition": p.Manifest.Crls[1] = p.Crl(p.Intermediate, extraOid: "2.5.29.28"); break;
            case "unknown-extension": p.Manifest.Crls[1] = p.Crl(p.Intermediate, extraOid: "1.2.3.4.5"); break;
            case "duplicate": p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf, duplicate: true); break;
            case "future": p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf, futureRevoked: true); break;
            case "wrong-aki": p.Manifest.Crls[1] = p.Crl(p.Intermediate, aki: new string('A', 40)); break;
            case "remove-from-crl": p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf, X509RevocationReason.RemoveFromCrl); break;
            case "observed-stale": p.Manifest.Crls[1].ProvisionedAt = p.Now.AddDays(-2); p.Manifest.Crls[1].ApprovedCurrentUntil = p.Now.AddDays(-1); expected = FiscalEvidenceState.Stale; break;
            case "number-rollback": p.Manifest.Crls[1].CrlNumberHex = "02"; break;
            case "ocsp-required": p.Manifest.Authorities[0].RequiresOcsp = true; expected = FiscalEvidenceState.Unknown; break;
        }
        var decision = new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now);
        Assert.Equal(expected, decision.Revocation); Assert.False(decision.Eligible);
    }

    [Fact]
    public void Semiannual_arl_is_distinct_from_current_publication_observation()
    {
        using var p = new FiscalTrustSyntheticPki();
        p.Manifest.Crls[0] = p.Crl(p.Root, thisUpdate: p.Now.AddDays(-90), nextUpdate: p.Now.AddDays(90));
        p.Manifest.Crls[0].MaximumAgeHours = 24 * 184;
        Assert.True(new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now).Eligible);
        p.Manifest.Crls[0].ProvisionedAt = p.Now.AddDays(-2); p.Manifest.Crls[0].ApprovedCurrentUntil = p.Now.AddDays(-1);
        Assert.Equal(FiscalEvidenceState.Stale, new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now).Revocation);
    }

    [Fact]
    public void Pfx_selects_one_signer_retains_auxiliaries_and_rejects_ambiguity()
    {
        using var p = new FiscalTrustSyntheticPki();
        using var intermediatePublic = new X509Certificate2(p.Intermediate.RawData);
        using var rootPublic = new X509Certificate2(p.Root.RawData);
        using var material = SriPfxReader.Load(p.Pfx(p.Leaf, intermediatePublic, rootPublic), "synthetic-rc6-only");
        Assert.Equal(p.Leaf.RawData, material.Certificate.RawData); Assert.Equal(2, material.AuxiliaryCertificates.Count);
        var ambiguous = p.Pfx(p.Leaf, p.Intermediate);
        Assert.Equal("CERTIFICATE_SIGNER_AMBIGUOUS", Assert.Throws<InvalidOperationException>(() => SriPfxReader.Load(ambiguous, "synthetic-rc6-only")).Message);
        Assert.Throws<InvalidOperationException>(() => SriPfxReader.Load(p.Pfx(intermediatePublic), "synthetic-rc6-only"));
    }

    [Fact]
    public void Pfx_intermediate_is_extra_store_not_a_trust_anchor()
    {
        using var p = new FiscalTrustSyntheticPki();
        // Approve the exact CRL issuer without supplying it as a chain-building certificate.
        p.Manifest.Authorities[1].Role = "CrlIssuer";
        var noAux = new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now);
        Assert.False(noAux.ChainTrusted);
        var withAux = new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [p.Intermediate], FiscalTrustSyntheticPki.RucA, p.Now);
        Assert.True(withAux.ChainTrusted); Assert.True(withAux.Eligible);
        using var fake = new FiscalTrustSyntheticPki(rootName: p.Root.GetNameInfo(X509NameType.SimpleName, false));
        Assert.False(new FiscalCertificateEvaluator(p.Registry()).Evaluate(fake.Leaf, [fake.Root, fake.Intermediate], FiscalTrustSyntheticPki.RucA, p.Now).Eligible);
    }

    [Theory]
    [InlineData("expired")] [InlineData("future")] [InlineData("usage")] [InlineData("weak")]
    [InlineData("eku")] [InlineData("ca-signer")] [InlineData("identity-unknown")]
    public void Certificate_profile_and_identity_are_independent(string scenario)
    {
        using var p = new FiscalTrustSyntheticPki();
        var cert = p.Issue("profile", p.Intermediate, scenario == "ca-signer", 0,
            scenario == "identity-unknown" ? "" : FiscalTrustSyntheticPki.RucA, [10],
            usage: scenario == "usage" ? X509KeyUsageFlags.KeyEncipherment : null,
            keySize: scenario == "weak" ? 1024 : 2048,
            from: scenario == "future" ? p.Now.AddDays(1) : p.Now.AddDays(-2),
            until: scenario == "expired" ? p.Now.AddDays(-1) : p.Now.AddDays(1), eku: scenario == "eku");
        var result = new FiscalCertificateEvaluator(p.Registry()).Evaluate(cert, [], FiscalTrustSyntheticPki.RucA, p.Now);
        if (scenario == "identity-unknown") { Assert.Equal(FiscalIdentityState.NotConfirmed, result.Identity); Assert.True(result.Eligible); }
        else Assert.False(result.Eligible);
    }

    [Theory]
    [InlineData("hash")] [InlineData("malformed")] [InlineData("schema")] [InlineData("ca-hash")] [InlineData("duplicate")]
    public void Registry_is_independently_pinned_and_strict(string failure)
    {
        using var p = new FiscalTrustSyntheticPki();
        if (failure == "schema") p.Manifest.SchemaVersion = 2;
        if (failure == "ca-hash") p.Manifest.Authorities[0].Sha256 = new string('0', 64);
        if (failure == "duplicate") p.Manifest.Authorities.Add(p.Manifest.Authorities[0]);
        var bytes = failure == "malformed" ? "{bad"u8.ToArray() : JsonSerializer.SerializeToUtf8Bytes(p.Manifest);
        var pin = failure == "hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
        Assert.False(FiscalTrustRegistry.Load(bytes, pin).Available);
    }

    [Theory]
    [InlineData("1.3.6.1.5.5.7.3.1", false)]
    [InlineData("1.3.6.1.5.5.7.3.2", true)]
    [InlineData("2.5.29.37.0", true)]
    public void Approved_eku_must_survive_intermediate_and_leaf_constraints(string intermediatePurpose, bool eligible)
    {
        using var p = new FiscalTrustSyntheticPki();
        const string approvedPurpose = "1.3.6.1.5.5.7.3.2";
        var intermediate = p.Issue("EKU constrained CA", p.Root, true, 0, "", [30],
            extendedKeyUsages: [intermediatePurpose]);
        var leaf = p.Issue("EKU constrained taxpayer", intermediate, false, 0, FiscalTrustSyntheticPki.RucA, [31],
            extendedKeyUsages: [approvedPurpose]);
        p.Manifest.Authorities[0].AllowedExtendedKeyUsages = [approvedPurpose];
        p.Manifest.Authorities[1] = FiscalTrustSyntheticPki.Authority(intermediate, "Intermediate");
        p.Manifest.Crls[1] = p.Crl(intermediate);

        var result = new FiscalCertificateEvaluator(p.Registry()).Evaluate(leaf, [], FiscalTrustSyntheticPki.RucA, p.Now);
        Assert.Equal(eligible, result.Eligible);
        Assert.Equal(eligible, result.SigningSuitable);
        if (!eligible) Assert.Equal("CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED", result.Reason);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Freshest_crl_only_on_certificate_path_cannot_be_accepted_as_full_crl(bool onIntermediate)
    {
        using var p = new FiscalTrustSyntheticPki();
        var intermediate = p.Issue("delta CA", p.Root, true, 0, "", [32], freshestCrl: onIntermediate);
        var leaf = p.Issue("delta taxpayer", intermediate, false, 0, FiscalTrustSyntheticPki.RucA, [33],
            freshestCrl: !onIntermediate);
        p.Manifest.Authorities[1] = FiscalTrustSyntheticPki.Authority(intermediate, "Intermediate");
        p.Manifest.Crls[1] = p.Crl(intermediate);

        var result = new FiscalCertificateEvaluator(p.Registry()).Evaluate(leaf, [], FiscalTrustSyntheticPki.RucA, p.Now);
        Assert.False(result.Eligible);
        Assert.Equal(FiscalEvidenceState.Error, result.Revocation);
        Assert.Equal("CERTIFICATE_REVOCATION_ERROR", result.Reason);
    }

    [Fact]
    public void Authority_constraints_path_length_expiry_and_fake_self_issued_root_are_rejected()
    {
        using var p = new FiscalTrustSyntheticPki();
        var sub = p.Issue("forbidden sub CA", p.Intermediate, true, 0, "", [20]);
        var leaf = p.Issue("too deep", sub, false, 0, FiscalTrustSyntheticPki.RucA, [21]);
        Assert.False(new FiscalCertificateEvaluator(p.Registry()).Evaluate(leaf, [sub], FiscalTrustSyntheticPki.RucA, p.Now).ChainTrusted);
        var fakeSelfIssued = p.Issue(p.Root.GetNameInfo(X509NameType.SimpleName, false), p.Root, true, 1, "", [22]);
        p.Manifest.Authorities[0] = FiscalTrustSyntheticPki.Authority(fakeSelfIssued, "Root");
        Assert.False(p.Registry().Available); // Same DN, different key, signature not by its own subject key.
        using var rsa = p.Root.GetRSAPrivateKey()!;
        var request = new CertificateRequest(p.Root.SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var expired = request.CreateSelfSigned(p.Now.AddDays(-400), p.Now.AddDays(-1));
        p.Manifest.Authorities[0] = FiscalTrustSyntheticPki.Authority(expired, "Root");
        p.Manifest.Crls[0] = p.Crl(expired);
        Assert.True(p.Registry().Available);
        Assert.False(new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now).ChainTrusted);
    }

    [Fact]
    public void Same_dn_wrong_key_crl_cannot_forge_good_and_resource_limits_fail_closed()
    {
        using var p = new FiscalTrustSyntheticPki(); using var fake = new FiscalTrustSyntheticPki();
        var forged = fake.Crl(fake.Intermediate);
        forged.IssuerSha256 = p.Intermediate.GetCertHashString(HashAlgorithmName.SHA256);
        p.Manifest.Crls[1] = forged;
        Assert.Equal(FiscalEvidenceState.Error, new FiscalCertificateEvaluator(p.Registry()).Evaluate(p.Leaf, [], FiscalTrustSyntheticPki.RucA, p.Now).Revocation);
        Assert.Equal("FISCAL_CERTIFICATE_LIMIT", new FiscalCertificateEvaluator(p.Registry())
            .Evaluate(p.Leaf, Enumerable.Repeat(p.Intermediate, 33).ToArray(), FiscalTrustSyntheticPki.RucA, p.Now).Reason);
        Assert.False(FiscalTrustRegistry.Load(new byte[FiscalTrustRegistry.MaximumBytes + 1], new string('0', 64)).Available);
        Assert.Throws<CryptographicException>(() => SriPfxReader.Load(new byte[2_097_153], "synthetic-only"));
    }

    [Theory]
    [InlineData("factura")] [InlineData("notaCredito")]
    public void Actual_signer_roundtrip_and_tamper_guards(string root)
    {
        using var p = new FiscalTrustSyntheticPki();
        const string key = "synthetic-access-key";
        var xml = $"<{root} id=\"comprobante\"><infoTributaria><claveAcceso>{key}</claveAcceso><ruc>{FiscalTrustSyntheticPki.RucA}</ruc></infoTributaria></{root}>";
        var signer = new SriXadesBesSigner();
        var signed = root == "factura" ? signer.SignInvoiceXml(xml, p.Leaf, key, p.Now) : signer.SignCreditNoteXml(xml, p.Leaf, key, p.Now);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        using var certificate = FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Leaf.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root);
        Assert.Equal(p.Leaf.RawData, certificate.RawData);
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(signed, "00", p.Leaf.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root));
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(signed, Hash(signed), p.Root.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root));
        var tampered = signed.Replace("<ruc>", "<ruc>9");
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(tampered, Hash(tampered), p.Leaf.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root));
        var external = signed.Replace("URI=\"#comprobante\"", "URI=\"http://127.0.0.1:9/ssrf\"");
        Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(external, Hash(external), p.Leaf.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root));
        foreach (var malformed in new[]
        {
            signed.Replace("id=\"comprobante\"", "id=\"comprobante\" Id=\"comprobante\""),
            signed.Replace("rsa-sha256", "rsa-sha1"),
            signed.Replace("enveloped-signature", "xpath"),
            signed.Replace("</ds:X509Data>", $"<ds:X509Certificate>{Convert.ToBase64String(p.Root.RawData)}</ds:X509Certificate></ds:X509Data>")
        })
        {
            Assert.NotEqual(signed, malformed);
            Assert.Throws<InvalidOperationException>(() => FiscalSignedXmlVerifier.Verify(malformed, Hash(malformed), p.Leaf.Thumbprint, key, FiscalTrustSyntheticPki.RucA, root));
        }
    }
}
