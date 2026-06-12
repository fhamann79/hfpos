using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IEmailSenderService
{
    Task SendAsync(
        CompanyEmailSettings settings,
        string smtpPassword,
        OutboundEmailMessage message,
        CancellationToken cancellationToken = default);
}
