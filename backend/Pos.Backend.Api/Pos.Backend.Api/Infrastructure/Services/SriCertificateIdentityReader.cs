using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Pos.Backend.Api.Infrastructure.Services;

// Deliberately small supported profile set; see docs/testing/DEV-531-certificate-identity.md.
internal sealed class SriCertificateIdentityReader
{
    private const string CountryOid = "2.5.4.6";
    private const string SubjectSerialOid = "2.5.4.5";
    private const string OrganizationIdentifierOid = "2.5.4.97";
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string IcertRucOid = "1.3.6.1.4.1.43745.1.3.11";
    private const int MaxEncodedBytes = 16_384;
    private const int MaxEntries = 64;
    private readonly HashSet<string> _seenAttributes = new(StringComparer.Ordinal);
    private readonly List<string> _countries = [];
    private readonly List<string> _identityCards = [];
    private int _attributeCount;
    private bool _incomplete;
    private bool _hasLegalPersonRuc;

    private List<RucEvidence> Rucs { get; } = [];

    public static SriCertificateIdentityReader Read(X509Certificate2 certificate)
    {
        var result = new SriCertificateIdentityReader();
        result.ReadBounded(certificate.SubjectName.RawData, subject: true);
        var sanCount = 0;
        if (certificate.Extensions.Count > MaxEntries)
        {
            result._incomplete = true;
        }

        foreach (var extension in certificate.Extensions.Cast<X509Extension>().Take(MaxEntries))
        {
            if (extension.Oid?.Value != SubjectAlternativeNameOid)
            {
                continue;
            }

            if (++sanCount > 1)
            {
                result._incomplete = true;
            }

            result.ReadBounded(extension.RawData, subject: false);
        }

        return result;
    }

    public (bool Mismatch, bool Match) Compare(string companyRuc)
    {
        var mismatch = Rucs.Any(ruc => ruc.Value != companyRuc);
        var match = Rucs.Any(ruc => ruc.Value == companyRuc);

        // A legal representative's identity card is not the company's tax identity.
        // Bare serials/ICERT identity-or-passport values never enter _identityCards.
        if (!_hasLegalPersonRuc && _countries.Count == 1 && _countries[0] == "EC"
            && IsNaturalPersonRuc(companyRuc))
        {
            mismatch |= _identityCards.Any(ci => ci + "001" != companyRuc);
            match |= _identityCards.Any(ci => ci + "001" == companyRuc);
        }

        // Preserve recognized contradictions even if another field is malformed/duplicated.
        return (mismatch, match && !mismatch && !_incomplete);
    }

    public string Describe()
    {
        var sources = Rucs.Select(ruc => ruc.Source).ToList();
        if (_identityCards.Count > 0)
        {
            sources.Add("Subject 2.5.4.5 IDCEC (relacion CI+001 solo para RUC natural conocido)");
        }

        if (_incomplete)
        {
            sources.Add("Evidencia incompleta, duplicada, fuera de limites o malformada; no confirma identidad");
        }

        return sources.Count == 0
            ? "Sin identificadores fiscales tipados soportados."
            : string.Join("; ", sources.Distinct(StringComparer.Ordinal));
    }

    private void ReadBounded(byte[] encoded, bool subject)
    {
        if (encoded.Length > MaxEncodedBytes)
        {
            _incomplete = true;
            return;
        }

        try
        {
            // Commit only a completely decoded container; malformed trailing bytes are not evidence.
            var parsed = new SriCertificateIdentityReader();
            var reader = new AsnReader(encoded, AsnEncodingRules.DER);
            if (subject)
            {
                parsed.ReadName(reader, subject: true);
            }
            else
            {
                var names = reader.ReadSequence();
                var count = 0;
                while (names.HasData)
                {
                    if (++count > MaxEntries)
                    {
                        throw new AsnContentException();
                    }

                    if (names.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 4)))
                    {
                        var directoryName = names.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true));
                        parsed.ReadName(directoryName, subject: false);
                        directoryName.ThrowIfNotEmpty();
                    }
                    else
                    {
                        // Email, DNS, otherName and all other GeneralNames carry no supported RUC evidence.
                        names.ReadEncodedValue();
                    }
                }
            }

            reader.ThrowIfNotEmpty();
            if (_attributeCount + parsed._attributeCount > MaxEntries)
            {
                throw new AsnContentException();
            }

            _attributeCount += parsed._attributeCount;
            _incomplete |= parsed._incomplete;
            foreach (var attribute in parsed._seenAttributes)
            {
                if (!_seenAttributes.Add(attribute))
                {
                    _incomplete = true;
                }
            }

            Rucs.AddRange(parsed.Rucs);
            _countries.AddRange(parsed._countries);
            _identityCards.AddRange(parsed._identityCards);
            _hasLegalPersonRuc |= parsed._hasLegalPersonRuc;
        }
        catch (AsnContentException)
        {
            _incomplete = true;
        }
    }

    private void ReadName(AsnReader reader, bool subject)
    {
        var name = reader.ReadSequence();
        while (name.HasData)
        {
            var rdn = name.ReadSetOf();
            if (!rdn.HasData)
            {
                throw new AsnContentException();
            }

            while (rdn.HasData)
            {
                if (++_attributeCount > MaxEntries)
                {
                    throw new AsnContentException();
                }

                var attribute = rdn.ReadSequence();
                var oid = attribute.ReadObjectIdentifier();
                var recognized = subject
                    ? oid is CountryOid or SubjectSerialOid or OrganizationIdentifierOid
                    : oid == IcertRucOid;
                if (!recognized)
                {
                    attribute.ReadEncodedValue();
                    attribute.ThrowIfNotEmpty();
                    continue;
                }

                var value = ReadString(attribute, utf8Only: !subject);
                attribute.ThrowIfNotEmpty();
                if (!_seenAttributes.Add($"{subject}:{oid}"))
                {
                    _incomplete = true;
                }

                AddAttribute(oid, value, subject);
            }
        }
    }

    private static string ReadString(AsnReader reader, bool utf8Only)
    {
        var tag = reader.PeekTag();
        if (tag.TagClass != TagClass.Universal || tag.IsConstructed
            || (utf8Only && tag.TagValue != (int)UniversalTagNumber.UTF8String)
            || tag.TagValue is not ((int)UniversalTagNumber.UTF8String)
                and not ((int)UniversalTagNumber.PrintableString)
                and not ((int)UniversalTagNumber.IA5String)
                and not ((int)UniversalTagNumber.BMPString)
                and not ((int)UniversalTagNumber.NumericString))
        {
            throw new AsnContentException();
        }

        var value = reader.ReadCharacterString((UniversalTagNumber)tag.TagValue);
        if (value.Length > 128)
        {
            throw new AsnContentException();
        }

        return value;
    }

    private void AddAttribute(string oid, string value, bool subject)
    {
        if (!subject)
        {
            AddRuc(value, $"SAN directoryName {IcertRucOid}");
        }
        else if (oid == CountryOid)
        {
            _countries.Add(value);
        }
        else if (oid == OrganizationIdentifierOid)
        {
            if (value.StartsWith("TINEC-", StringComparison.Ordinal) || value.StartsWith("VATEC-", StringComparison.Ordinal))
            {
                _hasLegalPersonRuc |= value.StartsWith("VATEC-", StringComparison.Ordinal);
                AddRuc(value[6..], $"Subject {OrganizationIdentifierOid} {value[..5]}");
            }
            else
            {
                _incomplete = true;
            }
        }
        else if (value.StartsWith("IDCEC-", StringComparison.Ordinal))
        {
            if (IsKnownIdentityCard(value[6..]))
            {
                _identityCards.Add(value[6..]);
            }
            else
            {
                _incomplete = true;
            }
        }
        // Other subject serials may be passports or sequential identifiers. Ignore them;
        // they cannot confirm CI+001, but do not invalidate an independent explicit RUC.
    }

    private void AddRuc(string value, string source)
    {
        if (IsAsciiDigits(value, 13))
        {
            Rucs.Add(new RucEvidence(value, source));
        }
        else
        {
            _incomplete = true;
        }
    }

    private static bool IsNaturalPersonRuc(string value)
        => IsAsciiDigits(value, 13) && value.EndsWith("001", StringComparison.Ordinal) && IsKnownIdentityCard(value[..10]);

    private static bool IsKnownIdentityCard(string value)
    {
        if (!IsAsciiDigits(value, 10))
        {
            return false;
        }

        var province = (value[0] - '0') * 10 + value[1] - '0';
        if (province is < 1 or > 24 || value[2] > '5')
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 9; index++)
        {
            var digit = (value[index] - '0') * (index % 2 == 0 ? 2 : 1);
            sum += digit > 9 ? digit - 9 : digit;
        }

        return (10 - sum % 10) % 10 == value[9] - '0';
    }

    private static bool IsAsciiDigits(string value, int length)
        => value.Length == length && value.All(character => character is >= '0' and <= '9');

    private sealed record RucEvidence(string Value, string Source);
}
