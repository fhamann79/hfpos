namespace Pos.Backend.Api.Core.Enums;

public enum SaleDocumentStatus
{
    NotRequired = 0,
    Draft = 1,
    PendingAuthorization = 2,
    Authorized = 3,
    Rejected = 4,
    Cancelled = 5
}
