namespace Pos.Backend.Api.Core.Services;

public interface ISriFiscalClock
{
    DateTime UtcNow { get; }

    DateOnly GetEcuadorFiscalDate(DateTime utcInstant);
}
