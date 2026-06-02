using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriFiscalClock : ISriFiscalClock
{
    private static readonly TimeZoneInfo EcuadorTimeZone = ResolveEcuadorTimeZone();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly GetEcuadorFiscalDate(DateTime utcInstant)
    {
        var normalizedUtcInstant = NormalizeUtcInstant(utcInstant);
        var ecuadorLocalTime = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtcInstant, EcuadorTimeZone);

        return DateOnly.FromDateTime(ecuadorLocalTime);
    }

    private static TimeZoneInfo ResolveEcuadorTimeZone()
    {
        foreach (var timeZoneId in new[] { "America/Guayaquil", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Ecuador continental does not observe DST; UTC-5 preserves the SRI fiscal date if OS time zone data is unavailable.
        return TimeZoneInfo.CreateCustomTimeZone(
            "Ecuador UTC-5",
            TimeSpan.FromHours(-5),
            "Ecuador UTC-5",
            "Ecuador UTC-5");
    }

    private static DateTime NormalizeUtcInstant(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
