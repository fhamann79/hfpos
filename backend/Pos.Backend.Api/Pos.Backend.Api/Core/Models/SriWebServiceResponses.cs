namespace Pos.Backend.Api.Core.Models;

public sealed class SriResponseMessage
{
    public string? Identifier { get; init; }

    public string? Type { get; init; }

    public string? Message { get; init; }

    public string? AdditionalInfo { get; init; }
}

public sealed class SriReceptionResponse
{
    public string RawResponseXml { get; init; } = string.Empty;

    public string? Estado { get; init; }

    public IReadOnlyList<SriResponseMessage> Messages { get; init; } = Array.Empty<SriResponseMessage>();

    public bool IsReceived => string.Equals(Estado, "RECIBIDA", StringComparison.OrdinalIgnoreCase);

    public bool IsReturned => string.Equals(Estado, "DEVUELTA", StringComparison.OrdinalIgnoreCase);

    public string? ErrorSummary => Messages.FirstOrDefault()?.Message
        ?? Messages.FirstOrDefault()?.AdditionalInfo
        ?? (IsReturned ? "Comprobante devuelto por SRI." : null);
}

public sealed class SriAuthorizationResponse
{
    public string RawResponseXml { get; init; } = string.Empty;

    public string? Estado { get; init; }

    public string? AuthorizationNumber { get; init; }

    public DateTime? AuthorizationDate { get; init; }

    public string? AuthorizedXml { get; init; }

    public IReadOnlyList<SriResponseMessage> Messages { get; init; } = Array.Empty<SriResponseMessage>();

    public bool IsAuthorized => string.Equals(Estado, "AUTORIZADO", StringComparison.OrdinalIgnoreCase);

    public bool IsRejected => string.Equals(Estado, "NO AUTORIZADO", StringComparison.OrdinalIgnoreCase);

    public bool IsPending => !IsAuthorized && !IsRejected;

    public string? ErrorSummary => Messages.FirstOrDefault()?.Message
        ?? Messages.FirstOrDefault()?.AdditionalInfo
        ?? (IsRejected ? "Comprobante no autorizado por SRI." : null);
}
