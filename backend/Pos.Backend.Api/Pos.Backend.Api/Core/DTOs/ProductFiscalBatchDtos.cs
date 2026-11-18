using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.DTOs;

public sealed record ProductFiscalBatchCandidate(int Id, string Name, string? InternalCode,
    string? Barcode, int CategoryId, bool IsActive, ProductVatCategory VatCategory, string FiscalStatus);

public sealed record ProductFiscalBatchCandidates(int TotalItems, int MaxProducts, ProductFiscalBatchCandidate[] Items);

public sealed record ProductFiscalBatchReceiptSummary(Guid OperationId, DateTime AppliedAt, int RowCount);

public sealed record ProductFiscalBatchRequest(int[] ProductIds, DeclareProductFiscalClassificationDto Declaration,
    string Reason);

public sealed record ProductFiscalBatchConfirmation(Guid OperationId, string PreviewToken, bool Confirmed);

public sealed record ProductFiscalBatchRow(int ProductId, string? Name, string? InternalCode,
    ProductVatCategory? CurrentVat, ProductFiscalClassificationDto? CurrentClassification,
    ProductVatCategory? ProposedVat, ProductFiscalClassificationDto? ProposedClassification,
    FiscalDecision? CurrentDecision, string? CurrentIssue, FiscalDecision? ProposedDecision, string Outcome, string? Rejection,
    string[] AvailabilityWarnings);

public sealed record ProductFiscalBatchPreview(Guid OperationId, int CompanyId, string CompanyName,
    DateOnly FiscalDate, string Reason, DeclareProductFiscalClassificationDto Declaration,
    ProductFiscalBatchRow[] Rows, int Changed, int Unchanged, int Rejected,
    string? PreviewToken, DateTime? ExpiresAt);

public sealed record ProductFiscalBatchResult(Guid OperationId, int CompanyId, string CompanyName,
    int UserId, DateTime AppliedAt, DateOnly FiscalDate, long ProfileRevision, string Reason,
    ProductFiscalBatchRow[] Rows, int Changed, int Unchanged, int Rejected);
