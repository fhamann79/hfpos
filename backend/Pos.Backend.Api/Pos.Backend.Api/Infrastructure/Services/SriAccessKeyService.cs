using System.Globalization;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriAccessKeyService : ISriAccessKeyService
{
    private const string InvoiceDocumentCode = "01";
    private const string CreditNoteDocumentCode = "04";

    public SriAccessKeyResult GenerateInvoiceAccessKey(SriAccessKeyRequest request)
        => GenerateAccessKey(request, InvoiceDocumentCode);

    public SriAccessKeyResult GenerateCreditNoteAccessKey(SriAccessKeyRequest request)
        => GenerateAccessKey(request, CreditNoteDocumentCode);

    private SriAccessKeyResult GenerateAccessKey(
        SriAccessKeyRequest request,
        string expectedDocumentCode)
    {
        if (request.DocumentCode != expectedDocumentCode)
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        if (!IsNumeric(request.IssuerRuc, 13)
            || !IsNumeric(request.EstablishmentCode, 3)
            || !IsNumeric(request.EmissionPointCode, 3)
            || request.Sequential <= 0
            || request.Environment is not (1 or 2)
            || request.EmissionType != 1)
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        var emissionDate = request.FiscalEmissionDate ?? DateOnly.FromDateTime(request.EmissionDate);
        var sequential = request.Sequential.ToString("000000000", CultureInfo.InvariantCulture);
        var numericCode = GenerateNumericCode(request.NumericCodeSeed);
        var accessKeyBase = string.Concat(
            emissionDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
            request.DocumentCode,
            request.IssuerRuc,
            request.Environment.ToString(CultureInfo.InvariantCulture),
            request.EstablishmentCode,
            request.EmissionPointCode,
            sequential,
            numericCode,
            request.EmissionType.ToString(CultureInfo.InvariantCulture));

        var checkDigit = CalculateModulo11CheckDigit(accessKeyBase);
        var accessKey = string.Concat(accessKeyBase, checkDigit.ToString(CultureInfo.InvariantCulture));

        if (!IsNumeric(accessKey, 49))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_GENERATION_FAILED");
        }

        return new SriAccessKeyResult
        {
            AccessKey = accessKey,
            Environment = request.Environment,
            EmissionType = request.EmissionType,
            NumericCode = numericCode
        };
    }

    public int CalculateModulo11CheckDigit(string accessKeyBase48)
    {
        if (!IsNumeric(accessKeyBase48, 48))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_GENERATION_FAILED");
        }

        var factor = 2;
        var sum = 0;

        for (var i = accessKeyBase48.Length - 1; i >= 0; i--)
        {
            sum += (accessKeyBase48[i] - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var check = 11 - (sum % 11);

        return check switch
        {
            11 => 0,
            10 => 1,
            _ => check
        };
    }

    private static string GenerateNumericCode(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_GENERATION_FAILED");
        }

        var hash = 17L;

        foreach (var character in seed)
        {
            hash = ((hash * 31) + character) % 100000000;
        }

        return hash.ToString("00000000", CultureInfo.InvariantCulture);
    }

    private static bool IsNumeric(string? value, int expectedLength)
    {
        return value is not null
            && value.Length == expectedLength
            && value.All(char.IsDigit);
    }
}
