using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CashSessionListQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public CashSessionStatus? Status { get; set; }
    public int? UserId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
