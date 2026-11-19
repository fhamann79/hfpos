namespace Pos.Backend.Api.Core.DTOs;

public sealed record ProductPriceBatchRequest(int[] ProductIds, string Action, decimal Value, string Reason);
public sealed record ProductPriceBatchConfirmation(Guid OperationId, string PreviewToken, bool Confirmed);
public sealed record ProductPriceBatchRow(int ProductId, string? Name, string? InternalCode,
    decimal? CurrentPrice, decimal? Cost, decimal? ProposedPrice, string? CurrentMargin,
    string? ProposedMargin, string Outcome, string? Rejection, string[] Warnings);
public sealed record ProductPriceBatchPreview(Guid OperationId, int CompanyId, string CompanyName,
    string Reason, string Action, decimal Value, ProductPriceBatchRow[] Rows, int Changed, int Unchanged,
    int Rejected, string? PreviewToken, DateTime? ExpiresAt);
public sealed record ProductPriceBatchResult(Guid OperationId, int CompanyId, string CompanyName,
    int UserId, DateTime AppliedAt, string Reason, string Action, decimal Value,
    ProductPriceBatchRow[] Rows, int Changed, int Unchanged, int Rejected);
public sealed record ProductPriceBatchReceiptSummary(Guid OperationId, DateTime AppliedAt, int RowCount);
