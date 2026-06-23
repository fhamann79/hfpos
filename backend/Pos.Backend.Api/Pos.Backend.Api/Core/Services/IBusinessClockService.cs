using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IBusinessClockService
{
    DateTime UtcNow { get; }

    TimeZoneInfo ResolveTimeZone(string timeZoneId);

    DateOnly GetBusinessDate(DateTime utcInstant, string timeZoneId);

    DateTime GetBusinessDateStartUtc(DateOnly businessDate, string timeZoneId);

    DateTime GetBusinessDateEndExclusiveUtc(DateOnly businessDate, string timeZoneId);

    BusinessDateRange GetBusinessDateRangeUtc(DateOnly from, DateOnly toInclusive, string timeZoneId);
}
