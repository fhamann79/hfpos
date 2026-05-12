namespace Pos.Backend.Api.Core.Models;

public class SriAccessKeyResult
{
    public string AccessKey { get; set; } = string.Empty;

    public int Environment { get; set; }

    public int EmissionType { get; set; }

    public string NumericCode { get; set; } = string.Empty;
}
