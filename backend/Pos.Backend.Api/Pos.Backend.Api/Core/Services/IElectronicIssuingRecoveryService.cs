using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IElectronicIssuingRecoveryService
{
    Task ResumeAsync(int saleId, OperationalContext actor, CancellationToken cancellationToken);
}
