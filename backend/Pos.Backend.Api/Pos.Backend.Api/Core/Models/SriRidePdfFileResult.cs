namespace Pos.Backend.Api.Core.Models;

public class SriRidePdfFileResult
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    public string ContentType { get; set; } = "application/pdf";

    public string FileName { get; set; } = string.Empty;
}
