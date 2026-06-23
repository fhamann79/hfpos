using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class BusinessClockService : IBusinessClockService
{
    public const string DefaultTimeZoneId = "America/Guayaquil";

    private static readonly IReadOnlyDictionary<string, string> TimeZoneFallbacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["America/Guayaquil"] = "SA Pacific Standard Time",
            ["America/Bogota"] = "SA Pacific Standard Time",
            ["America/Lima"] = "SA Pacific Standard Time",
            ["America/Mexico_City"] = "Central Standard Time (Mexico)",
            ["America/New_York"] = "Eastern Standard Time",
            ["America/Los_Angeles"] = "Pacific Standard Time",
            ["Europe/Madrid"] = "Romance Standard Time",
            ["Etc/UTC"] = "UTC",
            ["SA Pacific Standard Time"] = "America/Bogota",
            ["Central Standard Time (Mexico)"] = "America/Mexico_City",
            ["Eastern Standard Time"] = "America/New_York",
            ["Pacific Standard Time"] = "America/Los_Angeles",
            ["Romance Standard Time"] = "Europe/Madrid",
            ["UTC"] = "Etc/UTC"
        };

    public DateTime UtcNow => DateTime.UtcNow;

    public TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new InvalidOperationException("COMPANY_TIMEZONE_INVALID");
        }

        var normalized = timeZoneId.Trim();
        if (TryFindTimeZone(normalized, out var timeZone))
        {
            return timeZone;
        }

        if (TimeZoneFallbacks.TryGetValue(normalized, out var fallback)
            && TryFindTimeZone(fallback, out timeZone))
        {
            return timeZone;
        }

        throw new InvalidOperationException("COMPANY_TIMEZONE_INVALID");
    }

    public DateOnly GetBusinessDate(DateTime utcInstant, string timeZoneId)
    {
        var utc = utcInstant.Kind switch
        {
            DateTimeKind.Utc => utcInstant,
            DateTimeKind.Local => utcInstant.ToUniversalTime(),
            // DB instants are expected as UTC; Unspecified is normalized as UTC for compatibility.
            _ => DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc)
        };

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ResolveTimeZone(timeZoneId));
        return DateOnly.FromDateTime(local);
    }

    public DateTime GetBusinessDateStartUtc(DateOnly businessDate, string timeZoneId)
    {
        var localStart = DateTime.SpecifyKind(businessDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, ResolveTimeZone(timeZoneId));
    }

    public DateTime GetBusinessDateEndExclusiveUtc(DateOnly businessDate, string timeZoneId)
    {
        return GetBusinessDateStartUtc(businessDate.AddDays(1), timeZoneId);
    }

    public BusinessDateRange GetBusinessDateRangeUtc(DateOnly from, DateOnly toInclusive, string timeZoneId)
    {
        return new BusinessDateRange(
            GetBusinessDateStartUtc(from, timeZoneId),
            GetBusinessDateEndExclusiveUtc(toInclusive, timeZoneId));
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = null!;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = null!;
            return false;
        }
    }
}
