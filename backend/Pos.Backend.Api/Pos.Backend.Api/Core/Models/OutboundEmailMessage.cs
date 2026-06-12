namespace Pos.Backend.Api.Core.Models;

public class OutboundEmailMessage
{
    public string To { get; set; } = string.Empty;

    public string? Cc { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;

    public IReadOnlyList<OutboundEmailAttachment> Attachments { get; set; } = Array.Empty<OutboundEmailAttachment>();
}
