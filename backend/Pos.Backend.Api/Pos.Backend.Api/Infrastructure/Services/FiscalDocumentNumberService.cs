using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class FiscalDocumentNumberService : IFiscalDocumentNumberService
{
    private readonly PosDbContext _context;
    private readonly ILogger<FiscalDocumentNumberService> _logger;
    private readonly ISriFiscalClock _sriFiscalClock;

    public FiscalDocumentNumberService(
        PosDbContext context,
        ILogger<FiscalDocumentNumberService> logger,
        ISriFiscalClock sriFiscalClock)
    {
        _context = context;
        _logger = logger;
        _sriFiscalClock = sriFiscalClock;
    }

    public async Task<FiscalDocumentNumberAssignment> AssignNextAsync(
        OperationalContext operationalContext,
        FiscalDocumentType documentType)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_TYPE");
        }

        try
        {
            var documentContext = await _context.Establishments
                .AsNoTracking()
                .Where(e =>
                    e.Id == operationalContext.EstablishmentId
                    && e.CompanyId == operationalContext.CompanyId
                    && e.IsActive)
                .Select(e => new
                {
                    EstablishmentCode = e.Code,
                    EmissionPointCode = e.EmissionPoints
                        .Where(ep => ep.Id == operationalContext.EmissionPointId && ep.IsActive)
                        .Select(ep => ep.Code)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (documentContext is null || documentContext.EmissionPointCode is null)
            {
                throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED");
            }

            var establishmentCode = documentContext.EstablishmentCode.Trim();
            var emissionPointCode = documentContext.EmissionPointCode.Trim();

            if (establishmentCode.Length != 3 || emissionPointCode.Length != 3)
            {
                throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED");
            }

            var now = _sriFiscalClock.UtcNow;
            var documentTypeValue = (int)documentType;

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""DocumentSequences""
                    (""CompanyId"", ""EstablishmentId"", ""EmissionPointId"", ""DocumentType"", ""CurrentNumber"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    ({operationalContext.CompanyId}, {operationalContext.EstablishmentId}, {operationalContext.EmissionPointId}, {documentTypeValue}, 0, {now}, {now})
                ON CONFLICT (""CompanyId"", ""EstablishmentId"", ""EmissionPointId"", ""DocumentType"") DO NOTHING");

            var sequence = await _context.DocumentSequences
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""DocumentSequences""
                    WHERE ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                      AND ""DocumentType"" = {documentTypeValue}
                    FOR UPDATE")
                .SingleOrDefaultAsync();

            if (sequence is null)
            {
                throw new InvalidOperationException("DOCUMENT_SEQUENCE_ERROR");
            }

            sequence.CurrentNumber += 1;
            sequence.UpdatedAt = now;

            return new FiscalDocumentNumberAssignment
            {
                DocumentType = documentType,
                Number = $"{establishmentCode}-{emissionPointCode}-{sequence.CurrentNumber:000000000}",
                EstablishmentCode = establishmentCode,
                EmissionPointCode = emissionPointCode,
                Sequential = sequence.CurrentNumber,
                IssuedAt = now
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Document number generation failed. CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} DocumentType {DocumentType}",
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                documentType);

            throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED", ex);
        }
    }
}
