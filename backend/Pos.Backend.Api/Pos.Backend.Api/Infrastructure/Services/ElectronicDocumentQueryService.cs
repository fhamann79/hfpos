using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ElectronicDocumentQueryService(
    PosDbContext context,
    IOperationalContextAccessor operationalContextAccessor)
    : IElectronicDocumentQueryService
{
    public async Task<ElectronicDocumentListResultDto> GetListAsync(
        ElectronicDocumentQueryDto request)
    {
        request ??= new ElectronicDocumentQueryDto();
        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var query = BuildFilteredQuery(operationalContext, request);

        var summary = await query
            .GroupBy(_ => 1)
            .Select(group => new ElectronicDocumentSummaryDto
            {
                TotalDocuments = group.Count(),
                InvoiceCount = group.Count(row => row.Kind == ElectronicDocumentKind.Invoice),
                CreditNoteCount = group.Count(row => row.Kind == ElectronicDocumentKind.CreditNote),
                DraftCount = group.Count(row => row.DocumentStatus == SaleDocumentStatus.Draft),
                PendingAuthorizationCount = group.Count(row => row.DocumentStatus == SaleDocumentStatus.PendingAuthorization),
                AuthorizedCount = group.Count(row => row.DocumentStatus == SaleDocumentStatus.Authorized),
                RejectedCount = group.Count(row => row.DocumentStatus == SaleDocumentStatus.Rejected),
                CancelledCount = group.Count(row => row.DocumentStatus == SaleDocumentStatus.Cancelled),
                WithSriErrorCount = group.Count(row => row.SriLastSubmissionError != null && row.SriLastSubmissionError != string.Empty)
            })
            .SingleOrDefaultAsync() ?? new ElectronicDocumentSummaryDto();

        var items = await ApplyOrdering(query, request.SortField, request.SortOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new ElectronicDocumentListItemDto
            {
                Kind = row.Kind,
                Id = row.Id,
                Number = row.Number,
                BusinessDate = row.BusinessDate,
                DocumentIssuedAt = row.DocumentIssuedAt,
                CreatedAt = row.CreatedAt,
                BuyerName = row.BuyerName,
                BuyerIdentification = row.BuyerIdentification,
                BuyerEmail = row.BuyerEmail,
                Total = row.Total,
                DocumentStatus = row.DocumentStatus,
                AccessKey = row.AccessKey,
                AuthorizationNumber = row.AuthorizationNumber,
                AuthorizedAt = row.AuthorizedAt,
                SriEnvironment = row.SriEnvironment,
                SriSignedAt = row.SriSignedAt,
                SriSubmittedAt = row.SriSubmittedAt,
                SriReceptionStatus = row.SriReceptionStatus,
                SriAuthorizationStatus = row.SriAuthorizationStatus,
                SriLastSubmissionError = row.SriLastSubmissionError,
                SriLastCheckedAt = row.SriLastCheckedAt,
                HasSriXmlDraft = row.HasSriXmlDraft,
                HasSriSignedXml = row.HasSriSignedXml,
                OriginalSaleId = row.OriginalSaleId,
                OriginalSaleNumber = row.OriginalSaleNumber
            })
            .ToListAsync();

        return new ElectronicDocumentListResultDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = summary.TotalDocuments,
            TotalPages = summary.TotalDocuments == 0
                ? 0
                : (int)Math.Ceiling(summary.TotalDocuments / (double)pageSize),
            Summary = summary
        };
    }

    public async Task<ElectronicDocumentDetailDto?> GetByIdAsync(
        ElectronicDocumentKind kind,
        int id)
    {
        if (id <= 0)
        {
            return null;
        }

        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();

        return kind switch
        {
            ElectronicDocumentKind.Invoice => await GetInvoiceAsync(id, operationalContext),
            ElectronicDocumentKind.CreditNote => await GetCreditNoteAsync(id, operationalContext),
            _ => null
        };
    }

    private IQueryable<ElectronicDocumentQueryRow> BuildFilteredQuery(
        OperationalContext operationalContext,
        ElectronicDocumentQueryDto request)
    {
        var invoices = context.Sales
            .AsNoTracking()
            .Where(sale => sale.CompanyId == operationalContext.CompanyId
                && sale.EstablishmentId == operationalContext.EstablishmentId
                && sale.EmissionPointId == operationalContext.EmissionPointId
                && sale.DocumentType == SaleDocumentType.Invoice)
            .Select(sale => new ElectronicDocumentQueryRow
            {
                Kind = ElectronicDocumentKind.Invoice,
                Id = sale.Id,
                Number = sale.Number,
                BusinessDate = sale.BusinessDate,
                DocumentIssuedAt = sale.DocumentIssuedAt,
                EffectiveDocumentDate = sale.DocumentIssuedAt ?? sale.CreatedAt,
                CreatedAt = sale.CreatedAt,
                BuyerName = sale.BuyerNameSnapshot ?? (sale.Customer != null ? sale.Customer.Name : null),
                BuyerIdentification = sale.BuyerIdentificationSnapshot ?? (sale.Customer != null ? sale.Customer.Identification : null),
                BuyerEmail = sale.BuyerEmailSnapshot ?? (sale.Customer != null ? sale.Customer.Email : null),
                Total = sale.Total,
                DocumentStatus = sale.DocumentStatus,
                AccessKey = sale.AccessKey,
                AuthorizationNumber = sale.AuthorizationNumber,
                AuthorizedAt = sale.AuthorizedAt,
                SriEnvironment = sale.SriEnvironment,
                SriSignedAt = sale.SriSignedAt,
                SriSubmittedAt = sale.SriSubmittedAt,
                SriReceptionStatus = sale.SriReceptionStatus,
                SriAuthorizationStatus = sale.SriAuthorizationStatus,
                SriLastSubmissionError = sale.SriLastSubmissionError,
                SriLastCheckedAt = sale.SriLastCheckedAt,
                HasSriXmlDraft = sale.SriXmlDraft != null,
                HasSriSignedXml = sale.SriSignedXml != null,
                OriginalSaleId = null,
                OriginalSaleNumber = null
            });

        var creditNotes = context.CreditNotes
            .AsNoTracking()
            .Where(note => note.CompanyId == operationalContext.CompanyId
                && note.EstablishmentId == operationalContext.EstablishmentId
                && note.EmissionPointId == operationalContext.EmissionPointId)
            .Select(note => new ElectronicDocumentQueryRow
            {
                Kind = ElectronicDocumentKind.CreditNote,
                Id = note.Id,
                Number = note.Number,
                BusinessDate = note.BusinessDate,
                DocumentIssuedAt = note.DocumentIssuedAt,
                EffectiveDocumentDate = note.DocumentIssuedAt ?? note.CreatedAt,
                CreatedAt = note.CreatedAt,
                BuyerName = note.BuyerNameSnapshot ?? (note.Customer != null ? note.Customer.Name : null),
                BuyerIdentification = note.BuyerIdentificationSnapshot ?? (note.Customer != null ? note.Customer.Identification : null),
                BuyerEmail = note.BuyerEmailSnapshot ?? (note.Customer != null ? note.Customer.Email : null),
                Total = note.Total,
                DocumentStatus = note.DocumentStatus,
                AccessKey = note.AccessKey,
                AuthorizationNumber = note.AuthorizationNumber,
                AuthorizedAt = note.AuthorizedAt,
                SriEnvironment = note.SriEnvironment,
                SriSignedAt = note.SriSignedAt,
                SriSubmittedAt = note.SriSubmittedAt,
                SriReceptionStatus = note.SriReceptionStatus,
                SriAuthorizationStatus = note.SriAuthorizationStatus,
                SriLastSubmissionError = note.SriLastSubmissionError,
                SriLastCheckedAt = note.SriLastCheckedAt,
                HasSriXmlDraft = note.SriXmlDraft != null,
                HasSriSignedXml = note.SriSignedXml != null,
                OriginalSaleId = note.OriginalSaleId,
                OriginalSaleNumber = note.OriginalSaleNumberSnapshot
            });

        IQueryable<ElectronicDocumentQueryRow> query = request.Kind switch
        {
            ElectronicDocumentKind.Invoice => invoices,
            ElectronicDocumentKind.CreditNote => creditNotes,
            _ => invoices.Concat(creditNotes)
        };

        if (request.From.HasValue)
        {
            var from = DateOnly.FromDateTime(request.From.Value);
            query = query.Where(row => row.BusinessDate >= from);
        }

        if (request.To.HasValue)
        {
            var to = DateOnly.FromDateTime(request.To.Value);
            query = query.Where(row => row.BusinessDate <= to);
        }

        if (request.DocumentStatus.HasValue)
        {
            query = query.Where(row => row.DocumentStatus == request.DocumentStatus.Value);
        }

        if (request.OnlyWithSriError)
        {
            query = query.Where(row => row.SriLastSubmissionError != null && row.SriLastSubmissionError != string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(row =>
                (row.Number != null && row.Number.ToLower().Contains(term))
                || (row.AccessKey != null && row.AccessKey.ToLower().Contains(term))
                || (row.AuthorizationNumber != null && row.AuthorizationNumber.ToLower().Contains(term))
                || (row.BuyerName != null && row.BuyerName.ToLower().Contains(term))
                || (row.BuyerIdentification != null && row.BuyerIdentification.ToLower().Contains(term))
                || (row.BuyerEmail != null && row.BuyerEmail.ToLower().Contains(term))
                || (row.OriginalSaleNumber != null && row.OriginalSaleNumber.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<ElectronicDocumentQueryRow> ApplyOrdering(
        IQueryable<ElectronicDocumentQueryRow> query,
        string? sortField,
        string? sortOrder)
    {
        var ascending = string.Equals(sortOrder?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

        return sortField?.Trim().ToLowerInvariant() switch
        {
            "number" => ascending
                ? query.OrderBy(row => row.Number).ThenBy(row => row.Kind).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.Number).ThenBy(row => row.Kind).ThenByDescending(row => row.Id),
            "buyername" => ascending
                ? query.OrderBy(row => row.BuyerName).ThenBy(row => row.Kind).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.BuyerName).ThenBy(row => row.Kind).ThenByDescending(row => row.Id),
            "kind" => ascending
                ? query.OrderBy(row => row.Kind).ThenByDescending(row => row.BusinessDate).ThenByDescending(row => row.EffectiveDocumentDate).ThenByDescending(row => row.Id)
                : query.OrderByDescending(row => row.Kind).ThenByDescending(row => row.BusinessDate).ThenByDescending(row => row.EffectiveDocumentDate).ThenByDescending(row => row.Id),
            "documentstatus" => ascending
                ? query.OrderBy(row => row.DocumentStatus).ThenBy(row => row.Kind).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.DocumentStatus).ThenBy(row => row.Kind).ThenByDescending(row => row.Id),
            "total" => ascending
                ? query.OrderBy(row => row.Total).ThenBy(row => row.Kind).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.Total).ThenBy(row => row.Kind).ThenByDescending(row => row.Id),
            "authorizedat" => ascending
                ? query.OrderBy(row => row.AuthorizedAt).ThenBy(row => row.Kind).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.AuthorizedAt).ThenBy(row => row.Kind).ThenByDescending(row => row.Id),
            "documentdate" when ascending => query
                .OrderBy(row => row.BusinessDate)
                .ThenBy(row => row.EffectiveDocumentDate)
                .ThenBy(row => row.Kind)
                .ThenBy(row => row.Id),
            _ => query
                .OrderByDescending(row => row.BusinessDate)
                .ThenByDescending(row => row.EffectiveDocumentDate)
                .ThenBy(row => row.Kind)
                .ThenByDescending(row => row.Id)
        };
    }

    private async Task<ElectronicDocumentDetailDto?> GetInvoiceAsync(
        int id,
        OperationalContext operationalContext)
        => await context.Sales
            .AsNoTracking()
            .Where(sale => sale.Id == id
                && sale.CompanyId == operationalContext.CompanyId
                && sale.EstablishmentId == operationalContext.EstablishmentId
                && sale.EmissionPointId == operationalContext.EmissionPointId
                && sale.DocumentType == SaleDocumentType.Invoice)
            .Select(sale => new ElectronicDocumentDetailDto
            {
                Kind = ElectronicDocumentKind.Invoice,
                Id = sale.Id,
                Number = sale.Number,
                BusinessDate = sale.BusinessDate,
                DocumentIssuedAt = sale.DocumentIssuedAt,
                CreatedAt = sale.CreatedAt,
                BuyerName = sale.BuyerNameSnapshot ?? (sale.Customer != null ? sale.Customer.Name : null),
                BuyerIdentificationType = sale.BuyerIdentificationTypeSnapshot,
                BuyerIdentification = sale.BuyerIdentificationSnapshot ?? (sale.Customer != null ? sale.Customer.Identification : null),
                BuyerAddress = sale.BuyerAddressSnapshot,
                BuyerEmail = sale.BuyerEmailSnapshot ?? (sale.Customer != null ? sale.Customer.Email : null),
                Total = sale.Total,
                DocumentStatus = sale.DocumentStatus,
                AccessKey = sale.AccessKey,
                AuthorizationNumber = sale.AuthorizationNumber,
                AuthorizedAt = sale.AuthorizedAt,
                SriEnvironment = sale.SriEnvironment,
                SriEmissionType = sale.SriEmissionType,
                SriNumericCode = sale.SriNumericCode,
                SriXmlGeneratedAt = sale.SriXmlGeneratedAt,
                SriSignedAt = sale.SriSignedAt,
                SriSignatureHash = sale.SriSignatureHash,
                SriSigningCertificateThumbprint = sale.SriSigningCertificateThumbprint,
                SriSigningCertificateSubject = sale.SriSigningCertificateSubject,
                SriSigningCertificateSerialNumber = sale.SriSigningCertificateSerialNumber,
                SriSubmittedAt = sale.SriSubmittedAt,
                SriReceptionStatus = sale.SriReceptionStatus,
                SriAuthorizationStatus = sale.SriAuthorizationStatus,
                SriLastSubmissionError = sale.SriLastSubmissionError,
                SriLastCheckedAt = sale.SriLastCheckedAt,
                HasSriXmlDraft = sale.SriXmlDraft != null,
                HasSriSignedXml = sale.SriSignedXml != null,
                GrossSubtotal = sale.GrossSubtotal,
                DiscountAmount = sale.DiscountAmount,
                Subtotal = sale.Subtotal,
                TaxAmount = sale.TaxAmount,
                Notes = sale.Notes
            })
            .SingleOrDefaultAsync();

    private async Task<ElectronicDocumentDetailDto?> GetCreditNoteAsync(
        int id,
        OperationalContext operationalContext)
        => await context.CreditNotes
            .AsNoTracking()
            .Where(note => note.Id == id
                && note.CompanyId == operationalContext.CompanyId
                && note.EstablishmentId == operationalContext.EstablishmentId
                && note.EmissionPointId == operationalContext.EmissionPointId)
            .Select(note => new ElectronicDocumentDetailDto
            {
                Kind = ElectronicDocumentKind.CreditNote,
                Id = note.Id,
                Number = note.Number,
                BusinessDate = note.BusinessDate,
                DocumentIssuedAt = note.DocumentIssuedAt,
                CreatedAt = note.CreatedAt,
                BuyerName = note.BuyerNameSnapshot ?? (note.Customer != null ? note.Customer.Name : null),
                BuyerIdentificationType = note.BuyerIdentificationTypeSnapshot,
                BuyerIdentification = note.BuyerIdentificationSnapshot ?? (note.Customer != null ? note.Customer.Identification : null),
                BuyerAddress = note.BuyerAddressSnapshot,
                BuyerEmail = note.BuyerEmailSnapshot ?? (note.Customer != null ? note.Customer.Email : null),
                Total = note.Total,
                DocumentStatus = note.DocumentStatus,
                AccessKey = note.AccessKey,
                AuthorizationNumber = note.AuthorizationNumber,
                AuthorizedAt = note.AuthorizedAt,
                SriEnvironment = note.SriEnvironment,
                SriEmissionType = note.SriEmissionType,
                SriNumericCode = note.SriNumericCode,
                SriXmlGeneratedAt = note.SriXmlGeneratedAt,
                SriSignedAt = note.SriSignedAt,
                SriSignatureHash = note.SriSignatureHash,
                SriSigningCertificateThumbprint = note.SriSigningCertificateThumbprint,
                SriSigningCertificateSubject = note.SriSigningCertificateSubject,
                SriSigningCertificateSerialNumber = note.SriSigningCertificateSerialNumber,
                SriSubmittedAt = note.SriSubmittedAt,
                SriReceptionStatus = note.SriReceptionStatus,
                SriAuthorizationStatus = note.SriAuthorizationStatus,
                SriLastSubmissionError = note.SriLastSubmissionError,
                SriLastCheckedAt = note.SriLastCheckedAt,
                HasSriXmlDraft = note.SriXmlDraft != null,
                HasSriSignedXml = note.SriSignedXml != null,
                OriginalSaleId = note.OriginalSaleId,
                OriginalSaleNumber = note.OriginalSaleNumberSnapshot,
                GrossSubtotal = note.GrossSubtotal,
                DiscountAmount = note.DiscountAmount,
                Subtotal = note.Subtotal,
                TaxAmount = note.TaxAmount,
                Reason = note.Reason,
                Notes = note.Notes
            })
            .SingleOrDefaultAsync();

    private sealed class ElectronicDocumentQueryRow
    {
        public ElectronicDocumentKind Kind { get; set; }
        public int Id { get; set; }
        public string? Number { get; set; }
        public DateOnly BusinessDate { get; set; }
        public DateTime? DocumentIssuedAt { get; set; }
        public DateTime EffectiveDocumentDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? BuyerName { get; set; }
        public string? BuyerIdentification { get; set; }
        public string? BuyerEmail { get; set; }
        public decimal Total { get; set; }
        public SaleDocumentStatus DocumentStatus { get; set; }
        public string? AccessKey { get; set; }
        public string? AuthorizationNumber { get; set; }
        public DateTime? AuthorizedAt { get; set; }
        public int? SriEnvironment { get; set; }
        public DateTime? SriSignedAt { get; set; }
        public DateTime? SriSubmittedAt { get; set; }
        public string? SriReceptionStatus { get; set; }
        public string? SriAuthorizationStatus { get; set; }
        public string? SriLastSubmissionError { get; set; }
        public DateTime? SriLastCheckedAt { get; set; }
        public bool HasSriXmlDraft { get; set; }
        public bool HasSriSignedXml { get; set; }
        public int? OriginalSaleId { get; set; }
        public string? OriginalSaleNumber { get; set; }
    }
}
