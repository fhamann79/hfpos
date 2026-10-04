namespace Pos.Backend.Api.Core.Enums;

public enum ElectronicIssuingJobState
{
    Queued = 0,
    Processing = 1,
    WaitingAuthorization = 2,
    TransientFailure = 3,
    Authorized = 4,
    Rejected = 5,
    ManualAttention = 6
}
