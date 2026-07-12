using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IFiscalDocumentNumberService
{
    /// <remarks>
    /// The caller owns the transaction and must persist the tracked sequence update.
    /// </remarks>
    Task<FiscalDocumentNumberAssignment> AssignNextAsync(
        OperationalContext operationalContext,
        FiscalDocumentType documentType);
}
