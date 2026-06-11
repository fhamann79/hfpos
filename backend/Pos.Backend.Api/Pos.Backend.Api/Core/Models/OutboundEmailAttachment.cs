namespace Pos.Backend.Api.Core.Models;

public class OutboundEmailAttachment
{
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}
