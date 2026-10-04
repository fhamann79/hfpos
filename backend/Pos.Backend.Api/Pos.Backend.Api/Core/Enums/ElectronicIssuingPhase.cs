namespace Pos.Backend.Api.Core.Enums;

public enum ElectronicIssuingPhase
{
    ReadyToSign,
    ReadyToSubmit,
    ReceptionInFlight,
    UnknownReception,
    AwaitingAuthorization,
    Completed
}
