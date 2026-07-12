using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICreditNoteService
{
    Task<CreditNoteEligibilityDto> GetEligibilityAsync(int originalSaleId);
}
