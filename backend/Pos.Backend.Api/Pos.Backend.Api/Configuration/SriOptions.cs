namespace Pos.Backend.Api.Configuration;

public class SriOptions
{
    public int Environment { get; set; } = 1;

    public int EmissionType { get; set; } = 1;

    public string? ReceptionTestUrl { get; set; }

    public string? AuthorizationTestUrl { get; set; }

    public string? ReceptionProductionUrl { get; set; }

    public string? AuthorizationProductionUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public bool AllowProductionSubmission { get; set; } = false;
}
