using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class PaymentSettlementCreateDto
{
    public Guid RequestId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public SalePaymentMethod PaymentMethod { get; set; }
    public decimal SettledAmount { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class PaymentSettlementQueryDto
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public SalePaymentMethod? PaymentMethod { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PaymentSettlementDto
{
    public int Id { get; set; }
    public DateOnly BusinessDate { get; set; }
    public SalePaymentMethod PaymentMethod { get; set; }
    public decimal GrossSalesAmount { get; set; }
    public decimal VoidAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal ExpectedNetAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DifferenceAmount { get; set; }
    public Guid RequestId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public int ReconciledByUserId { get; set; }
    public string ReconciledByUsername { get; set; } = string.Empty;
    public DateTime ReconciledAt { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
    public bool WasAlreadyProcessed { get; set; }
}

public class PaymentMethodActivityDto
{
    public SalePaymentMethod PaymentMethod { get; set; }
    public decimal GrossSalesAmount { get; set; }
    public decimal VoidAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal NetPaymentAmount { get; set; }
    public bool CanSettle { get; set; }
    public PaymentSettlementDto? Settlement { get; set; }
}

public class PaymentReconciliationDto
{
    public DateOnly BusinessDate { get; set; }
    public DateOnly CurrentBusinessDate { get; set; }
    public int LegacyUnattributedVoidCount { get; set; }
    public IReadOnlyList<PaymentMethodActivityDto> Methods { get; set; } = [];
}
