using Pos.Backend.Api.Core.Entities;

namespace Pos.Backend.Api.Core.Models;

public class CompanyEmailSenderSettings
{
    public CompanyEmailSettings Settings { get; set; } = null!;

    public string SmtpPassword { get; set; } = string.Empty;
}
