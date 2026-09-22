namespace Pos.Backend.Api.Core.DTOs;

public class SaleCsvExportDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/csv; charset=utf-8";
}
