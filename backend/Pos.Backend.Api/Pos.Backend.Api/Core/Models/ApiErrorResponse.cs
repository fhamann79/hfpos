namespace Pos.Backend.Api.Core.Models;

public class ApiErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
}
