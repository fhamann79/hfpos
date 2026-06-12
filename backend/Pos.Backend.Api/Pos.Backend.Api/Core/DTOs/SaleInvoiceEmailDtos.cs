namespace Pos.Backend.Api.Core.DTOs;

public class SendSaleInvoiceEmailRequestDto
{
    public string? ToEmail { get; set; }

    public string? CcEmail { get; set; }

    public string? Subject { get; set; }

    public string? Message { get; set; }
}

public class SendSaleInvoiceEmailResultDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string? CcEmail { get; set; }

    public string? DocumentNumber { get; set; }

    public string? AuthorizationNumber { get; set; }
}
