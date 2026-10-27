using System.Formats.Asn1;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Infrastructure.Services;
using Xunit.Abstractions;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SriFiscalReadinessCertificateIdentityTests(ITestOutputHelper output)
{
    private const string CompanyRuc = "1790012345001";
    private const string OtherRuc = "1799999999001";
    private const string Ci = "1700000001";
    private const string OtherCi = "1700000019";
    private const string NaturalRuc = Ci + "001";
    private const string IcertRucOid = "1.3.6.1.4.1.43745.1.3.11";
    private const string IcertDocumentOid = "1.3.6.1.4.1.43745.1.3.1";
    private const string SerialOid = "2.5.4.5";
    private const string OrganizationOid = "2.5.4.97";

    [Theory]
    [InlineData(true, CompanyRuc)]
    [InlineData(true, OtherRuc)]
    [InlineData(false, CompanyRuc)]
    [InlineData(false, OtherRuc)]
    public void Technical_serial_and_SAN_email_are_not_fiscal_identity(bool technicalSerial, string candidate)
    {
        var extensions = new List<X509Extension>();
        if (!technicalSerial)
        {
            var san = new SubjectAlternativeNameBuilder();
            san.AddEmailAddress($"{candidate}@synthetic.invalid");
            extensions.Add(san.Build());
        }

        using var certificate = CreateCertificate(extensions, technicalSerial ? $"0A{candidate}B" : "01");
        var (check, sandboxUsable, _) = Evaluate(certificate, CompanyRuc);
        output.WriteLine($"{(technicalSerial ? "technical serial" : "SAN email")}: {check.Code}; blocking={check.IsBlocking}; sandboxUsable={sandboxUsable}");
        Assert.Equal("CERTIFICATE_RUC_NOT_CONFIRMED", check.Code);
        Assert.Equal("warning", check.Severity);
        Assert.False(check.IsBlocking);
        Assert.True(sandboxUsable);
    }

    [Theory]
    [InlineData("TINEC-", CompanyRuc, CompanyRuc, "MATCH")]
    [InlineData("VATEC-", CompanyRuc, CompanyRuc, "MATCH")]
    [InlineData("TINEC-", OtherRuc, CompanyRuc, "MISMATCH")]
    [InlineData("VATEC-", OtherRuc, CompanyRuc, "MISMATCH")]
    [InlineData("TINEC-", "9999999999999", "9999999999999", "MATCH")]
    public void Explicit_subject_RUC_is_compared_without_a_universal_RUC_checksum(string prefix, string value, string companyRuc, string state)
    {
        AssertDecision(Subject((OrganizationOid, prefix + value)), [], companyRuc, state);
    }

    [Theory]
    [InlineData(CompanyRuc, "MATCH")]
    [InlineData(OtherRuc, "MISMATCH")]
    public void Explicit_ICERT_SAN_directory_RUC_is_recognized(string value, string state)
    {
        AssertDecision(Subject(), [DirectorySan((IcertRucOid, value))], CompanyRuc, state);
    }

    [Theory]
    [InlineData(Ci)]
    [InlineData("PASEC-1700000001")]
    [InlineData("1700000001001")]
    public void Ambiguous_subject_serial_does_not_invalidate_an_independent_explicit_RUC(string document)
    {
        AssertDecision(Subject((SerialOid, document)), [DirectorySan((IcertRucOid, CompanyRuc))], CompanyRuc, "MATCH");
        AssertDecision(Subject((SerialOid, document), (OrganizationOid, "VATEC-" + CompanyRuc)), [], CompanyRuc, "MATCH");
    }

    [Theory]
    [InlineData("IDCEC-1700000001", NaturalRuc, "EC", "MATCH")]
    [InlineData("IDCEC-1700000019", NaturalRuc, "EC", "MISMATCH")]
    [InlineData("IDCEC-1700000001", CompanyRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1700000001", "1700000001002", "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1700000001", "1700000000001", "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1700000001", NaturalRuc, "US", "NOT_CONFIRMED")]
    [InlineData("IDCUS-1700000001", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("PASEC-1700000001", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("1700000001", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("1700000019", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("1700000001001", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1700000000", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-0000000000", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1760000005", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-2500000003", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-1700000001extra", NaturalRuc, "EC", "NOT_CONFIRMED")]
    [InlineData("IDCEC-\u0661\u0667\u0660\u0660\u0660\u0660\u0660\u0660\u0660\u0661", NaturalRuc, "EC", "NOT_CONFIRMED")]
    public void Only_typed_Ecuadorian_CI_can_infer_the_restricted_natural_RUC(string document, string companyRuc, string country, string state)
    {
        AssertDecision(Subject(("2.5.4.6", country), (SerialOid, document)), [], companyRuc, state);
    }

    [Fact]
    public void ICERT_document_or_passport_and_natural_profile_never_type_a_bare_CI()
    {
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, Ci)),
            [DirectorySan((IcertDocumentOid, Ci), ("1.3.6.1.4.1.43745.1.3.50", "PERSONA NATURAL"))], NaturalRuc, "NOT_CONFIRMED");
    }

    [Fact]
    public void Matching_natural_RUC_cannot_hide_a_different_typed_CI()
    {
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, "IDCEC-" + OtherCi), (OrganizationOid, "TINEC-" + NaturalRuc)),
            [], NaturalRuc, "MISMATCH");
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, "IDCEC-" + OtherCi)),
            [DirectorySan((IcertRucOid, NaturalRuc))], NaturalRuc, "MISMATCH");
    }

    [Fact]
    public void Legal_person_RUC_is_not_the_representatives_CI()
    {
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, "IDCEC-" + OtherCi), (OrganizationOid, "VATEC-" + NaturalRuc)),
            [], NaturalRuc, "MATCH");
    }

    [Theory]
    [InlineData(CompanyRuc, OtherRuc)]
    [InlineData(OtherRuc, CompanyRuc)]
    public void Contradictory_RUCs_block_regardless_of_order(string first, string second)
    {
        AssertDecision(Subject(), [DirectorySan((IcertRucOid, first), (IcertRucOid, second))], CompanyRuc, "MISMATCH");
        AssertDecision(Subject((OrganizationOid, "TINEC-" + first)), [DirectorySan((IcertRucOid, second))], CompanyRuc, "MISMATCH");
    }

    [Theory]
    [InlineData(CompanyRuc)]
    [InlineData(OtherRuc)]
    public void Duplicate_recognized_attributes_never_confirm_but_valid_mismatch_still_blocks(string value)
    {
        AssertDecision(Subject(), [DirectorySan((IcertRucOid, value), (IcertRucOid, value))], CompanyRuc,
            value == CompanyRuc ? "NOT_CONFIRMED" : "MISMATCH");
        AssertDecision(Subject((OrganizationOid, "TINEC-" + value), (OrganizationOid, "TINEC-" + value)), [], CompanyRuc,
            value == CompanyRuc ? "NOT_CONFIRMED" : "MISMATCH");
    }

    [Theory]
    [InlineData("2.5.4.3")]
    [InlineData("2.5.4.11")]
    [InlineData("2.5.4.20")]
    [InlineData("1.3.6.1.4.1.43745.1.3.11.1")]
    [InlineData("1.3.6.1.4.1.55555.1")]
    public void Unknown_OIDs_and_names_never_supply_RUC_evidence(string oid)
    {
        AssertDecision(Subject((oid, CompanyRuc)), [DirectorySan((oid, OtherRuc))], CompanyRuc, "NOT_CONFIRMED");
    }

    [Fact]
    public void Provider_OIDs_in_undocumented_locations_and_arbitrary_extension_bytes_are_ignored()
    {
        AssertDecision(Subject((IcertRucOid, CompanyRuc)),
            [ScalarExtension(IcertRucOid, CompanyRuc), ScalarExtension("1.3.6.1.4.1.37947.3.11", CompanyRuc),
             ScalarExtension("1.3.6.1.4.1.37746.3.11", OtherRuc), ScalarExtension("1.3.6.1.4.1.55555.1", CompanyRuc)],
            CompanyRuc, "NOT_CONFIRMED");
        AssertDecision(Subject(), [DirectorySan((OrganizationOid, "TINEC-" + CompanyRuc))], CompanyRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData("TINEC-1790012345001 suffix")]
    [InlineData(" TINEC-1790012345001")]
    [InlineData("TINUS-1790012345001")]
    [InlineData("RUC:1790012345001")]
    [InlineData("1790012345001")]
    [InlineData("tinec-1790012345001")]
    [InlineData("TINEC\u20111790012345001")]
    [InlineData("TINEC-\u0661\u0667\u0669\u0660\u0660\u0661\u0662\u0663\u0664\u0665\u0660\u0660\u0661")]
    public void Organization_identifier_requires_exact_supported_scheme_and_ASCII_value(string value)
    {
        AssertDecision(Subject((OrganizationOid, value)), [], CompanyRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData("RUC:1790012345001")]
    [InlineData("17900123450010")]
    [InlineData("1790012345001 ")]
    [InlineData("1700000001")]
    [InlineData("\u0661\u0667\u0669\u0660\u0660\u0661\u0662\u0663\u0664\u0665\u0660\u0660\u0661")]
    public void ICERT_RUC_requires_an_exact_13_ASCII_digit_value(string value)
    {
        AssertDecision(Subject(), [DirectorySan((IcertRucOid, value))], CompanyRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData(CompanyRuc)]
    [InlineData(OtherRuc)]
    public void Trailing_data_invalidates_the_whole_SAN_container_not_just_match(string value)
    {
        var san = DirectorySan((IcertRucOid, value));
        var malformed = new X509Extension("2.5.29.17", [.. san.RawData, 0x05, 0x00], false);
        AssertDecision(Subject(), [malformed], CompanyRuc, "NOT_CONFIRMED");
    }

    [Fact]
    public void Valid_subject_mismatch_survives_an_independent_malformed_SAN()
    {
        AssertDecision(Subject((OrganizationOid, "TINEC-" + OtherRuc)),
            [new X509Extension("2.5.29.17", [0x30, 0x03, 0xA4, 0x01], false)], CompanyRuc, "MISMATCH");
        AssertDecision(Subject((OrganizationOid, "TINEC-" + CompanyRuc)),
            [new X509Extension("2.5.29.17", [0x30, 0x03, 0xA4, 0x01], false)], CompanyRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData("0500", CompanyRuc)]
    [InlineData("0500", OtherRuc)]
    [InlineData("A100", CompanyRuc)]
    [InlineData("A100", OtherRuc)]
    [InlineData("8900", CompanyRuc)]
    [InlineData("8900", OtherRuc)]
    public void Invalid_GeneralName_discards_all_SAN_evidence_but_not_valid_subject_mismatch(string encodedName, string ruc)
    {
        foreach (var invalidFirst in new[] { false, true })
        {
            var san = SanWithGeneralName(ruc, Convert.FromHexString(encodedName), invalidFirst);
            AssertDecision(Subject(), [san], CompanyRuc, "NOT_CONFIRMED");
            AssertDecision(Subject((OrganizationOid, "TINEC-" + OtherRuc)), [san], CompanyRuc, "MISMATCH");
        }
    }

    [Theory]
    [InlineData("8100")]
    [InlineData("8200")]
    [InlineData("8600")]
    [InlineData("8700")]
    [InlineData("8703010203")]
    [InlineData("8800")]
    [InlineData("880180")]
    [InlineData("88028000")]
    [InlineData("810180")]
    [InlineData("810100")]
    [InlineData("A200")]
    [InlineData("A600")]
    [InlineData("A700")]
    [InlineData("A800")]
    [InlineData("8000")]
    [InlineData("A000")]
    [InlineData("A300")]
    [InlineData("A500")]
    [InlineData("8400")]
    public void Unsupported_or_malformed_GeneralName_cannot_leave_partial_RUC_evidence(string encodedName)
    {
        foreach (var ruc in new[] { CompanyRuc, OtherRuc })
        {
            AssertDecision(Subject(), [SanWithGeneralName(ruc, Convert.FromHexString(encodedName))], CompanyRuc, "NOT_CONFIRMED");
        }
    }

    [Theory]
    [InlineData(1, "synthetic@invalid.test")]
    [InlineData(2, "synthetic.invalid")]
    [InlineData(6, "https://synthetic.invalid/")]
    public void Valid_primitive_IA5_GeneralNames_are_ignored_without_discarding_RUC(int tag, string value)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.IA5String, value, new Asn1Tag(TagClass.ContextSpecific, tag));
        AssertDecision(Subject(), [SanWithGeneralName(CompanyRuc, writer.Encode())], CompanyRuc, "MATCH");
        AssertDecision(Subject(), [SanWithGeneralName(OtherRuc, writer.Encode())], CompanyRuc, "MISMATCH");
    }

    [Theory]
    [InlineData("8704C0000201")]
    [InlineData("871020010DB8000000000000000000000001")]
    [InlineData("88022A03")]
    public void Valid_IP_and_registered_OID_GeneralNames_are_ignored_without_discarding_RUC(string encodedName)
    {
        AssertDecision(Subject(), [SanWithGeneralName(CompanyRuc, Convert.FromHexString(encodedName))], CompanyRuc, "MATCH");
        AssertDecision(Subject(), [SanWithGeneralName(OtherRuc, Convert.FromHexString(encodedName))], CompanyRuc, "MISMATCH");
    }

    [Theory]
    [InlineData("040D31373930303132333435303031")]
    [InlineData("0C02C328")]
    [InlineData("0C0131")]
    public void Malformed_or_unsupported_attribute_values_are_not_fiscal_evidence(string encodedValue)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
        writer.PushSequence();
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier(IcertRucOid);
        writer.WriteEncodedValue(Convert.FromHexString(encodedValue));
        writer.PopSequence();
        writer.PopSetOf();
        writer.PopSequence();
        writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
        writer.PopSequence();
        AssertDecision(Subject(), [new X509Extension("2.5.29.17", writer.Encode(), false)], CompanyRuc, "NOT_CONFIRMED");
    }

    [Fact]
    public void Limits_do_not_confirm_a_matching_prefix()
    {
        var attributes = new[] { (IcertRucOid, CompanyRuc) }.Concat(Enumerable.Range(0, 64)
            .Select(index => ($"1.3.6.1.4.1.55555.{index}", "noise"))).ToArray();
        AssertDecision(Subject(), [DirectorySan(attributes)], CompanyRuc, "NOT_CONFIRMED");
        AssertDecision(Subject(), [DirectorySan((IcertRucOid, new string('1', 16_400)))], CompanyRuc, "NOT_CONFIRMED");
    }

    [Fact]
    public void Multiple_directory_names_do_not_hide_duplicates_or_conflicts()
    {
        AssertDecision(Subject(), [SanNames(Name([(IcertRucOid, CompanyRuc)]), Name([(IcertRucOid, CompanyRuc)]))],
            CompanyRuc, "NOT_CONFIRMED");
        AssertDecision(Subject(), [SanNames(Name([(IcertRucOid, CompanyRuc)]), Name([(IcertRucOid, OtherRuc)]))],
            CompanyRuc, "MISMATCH");
    }

    [Fact]
    public void Duplicate_CI_cannot_confirm_or_hide_another_typed_identity()
    {
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, "IDCEC-" + Ci), (SerialOid, "IDCEC-" + Ci)),
            [], NaturalRuc, "NOT_CONFIRMED");
        AssertDecision(Subject(("2.5.4.6", "EC"), (SerialOid, "IDCEC-" + Ci), (SerialOid, "IDCEC-" + OtherCi)),
            [], NaturalRuc, "MISMATCH");
    }

    [Fact]
    public void Missing_country_does_not_support_CI_inference()
    {
        AssertDecision(Subject((SerialOid, "IDCEC-" + Ci)), [], NaturalRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("3004A4023001")]
    [InlineData("300BA40930073105300306012A")]
    [InlineData("301EA41C301A31183016060A2B0601040182D55D01030B0C02C328")]
    public void Malformed_and_unsupported_SAN_encodings_return_warning_without_throwing(string hex)
    {
        AssertDecision(Subject(), [new X509Extension("2.5.29.17", Convert.FromHexString(hex), false)], CompanyRuc, "NOT_CONFIRMED");
    }

    [Theory]
    [InlineData("MetadataUsable", false)]
    [InlineData("MaterialLoaded", false)]
    [InlineData("SigningUsable", false)]
    public void Matching_identity_does_not_bypass_other_existing_certificate_guards(string guard, bool sandboxUsable)
    {
        using var certificate = CreateCertificate([], subject: Subject((OrganizationOid, "TINEC-" + CompanyRuc)));
        var result = Evaluate(certificate, CompanyRuc, disabledGuard: guard);
        Assert.Equal("CERTIFICATE_RUC_MATCH", result.Check.Code);
        Assert.False(result.Check.IsBlocking);
        Assert.Equal(sandboxUsable, result.SandboxUsable);
        Assert.False(result.ProductionUsable);
    }

    [Theory]
    [InlineData(UniversalTagNumber.UTF8String)]
    [InlineData(UniversalTagNumber.PrintableString)]
    [InlineData(UniversalTagNumber.BMPString)]
    public void Subject_DirectoryStrings_are_decoded_from_DER_not_formatted_text(UniversalTagNumber type)
    {
        using var certificate = CreateCertificate([], subject: new X500DistinguishedName(Name([(OrganizationOid, "TINEC-" + CompanyRuc)], type)));
        AssertState(Evaluate(certificate, CompanyRuc), "MATCH");
    }

    private static void AssertDecision(X500DistinguishedName subject, X509Extension[] extensions, string companyRuc, string state)
    {
        using var certificate = CreateCertificate(extensions, subject: subject);
        AssertState(Evaluate(certificate, companyRuc), state);
    }

    private static void AssertState((SriFiscalReadinessCheckDto Check, bool SandboxUsable, bool ProductionUsable) result, string state)
    {
        Assert.Equal("CERTIFICATE_RUC_" + state, result.Check.Code);
        Assert.Equal(state == "MISMATCH" ? "error" : state == "MATCH" ? "success" : "warning", result.Check.Severity);
        Assert.Equal(state == "MISMATCH", result.Check.IsBlocking);
        Assert.Equal(state != "MISMATCH", result.SandboxUsable);
        Assert.Equal(state != "MISMATCH", result.ProductionUsable);
    }

    private static X500DistinguishedName Subject(params (string Oid, string Value)[] attributes)
        => new(Name([("2.5.4.3", "HFPOS synthetic identity test"), .. attributes]));

    private static byte[] Name((string Oid, string Value)[] attributes, UniversalTagNumber type = UniversalTagNumber.UTF8String)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        foreach (var (oid, value) in attributes)
        {
            writer.PushSetOf();
            writer.PushSequence();
            writer.WriteObjectIdentifier(oid);
            writer.WriteCharacterString(type, value);
            writer.PopSequence();
            writer.PopSetOf();
        }

        writer.PopSequence();
        return writer.Encode();
    }

    private static X509Extension DirectorySan(params (string Oid, string Value)[] attributes)
        => SanNames(Name(attributes));

    private static X509Extension SanWithGeneralName(string ruc, byte[] encodedName, bool invalidFirst = false)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        if (invalidFirst)
        {
            writer.WriteEncodedValue(encodedName);
        }

        writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
        writer.WriteEncodedValue(Name([(IcertRucOid, ruc)]));
        writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
        if (!invalidFirst)
        {
            writer.WriteEncodedValue(encodedName);
        }

        writer.PopSequence();
        return new X509Extension("2.5.29.17", writer.Encode(), false);
    }

    private static X509Extension SanNames(params byte[][] names)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        foreach (var name in names)
        {
            writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
            writer.WriteEncodedValue(name);
            writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
        }

        writer.PopSequence();
        return new X509Extension("2.5.29.17", writer.Encode(), false);
    }

    private static X509Extension ScalarExtension(string oid, string value)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.UTF8String, value);
        return new X509Extension(oid, writer.Encode(), false);
    }

    private static X509Certificate2 CreateCertificate(IEnumerable<X509Extension> extensions, string serial = "01", X500DistinguishedName? subject = null)
    {
        using var rsa = RSA.Create(2048);
        var name = subject ?? new X500DistinguishedName("CN=HFPOS synthetic identity test");
        var request = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        foreach (var extension in extensions)
        {
            request.CertificateExtensions.Add(extension);
        }

        return request.Create(name, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60), Convert.FromHexString(serial));
    }

    // Exercise the service decision and its readiness flags without DB, provider, chain/network or API startup.
    private static (SriFiscalReadinessCheckDto Check, bool SandboxUsable, bool ProductionUsable) Evaluate(X509Certificate2 certificate, string companyRuc, string? disabledGuard = null)
    {
        var serviceType = typeof(SriFiscalReadinessService);
        var diagnosticsType = serviceType.GetNestedType("CertificateReadinessDiagnostics", BindingFlags.NonPublic)!;
        var diagnostics = Activator.CreateInstance(diagnosticsType, nonPublic: true)!;
        foreach (var property in new[] { "MetadataUsable", "MaterialLoaded", "SigningUsable" })
        {
            diagnosticsType.GetProperty(property)!.SetValue(diagnostics, true);
        }

        if (disabledGuard is not null)
        {
            diagnosticsType.GetProperty(disabledGuard)!.SetValue(diagnostics, false);
        }

        var checks = new List<SriFiscalReadinessCheckDto>();
        serviceType.GetMethod("AddCertificateRucCheck", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [checks, companyRuc, certificate, diagnostics]);
        return (Assert.Single(checks),
            (bool)diagnosticsType.GetProperty("IsUsableForSandbox")!.GetValue(diagnostics)!,
            (bool)diagnosticsType.GetProperty("IsUsableForProduction")!.GetValue(diagnostics)!);
    }
}
