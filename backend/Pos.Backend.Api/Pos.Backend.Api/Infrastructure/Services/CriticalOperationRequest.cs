using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class CriticalOperationRequest
{
    private static readonly JsonSerializerOptions HashOptions = new() { Converters = { new CanonicalDecimalConverter() } };
    public static decimal Amount(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string Hash<T>(T payload) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, HashOptions)));
    public static void RequireId(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new InvalidOperationException("REQUEST_ID_REQUIRED");
    }
    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
}
