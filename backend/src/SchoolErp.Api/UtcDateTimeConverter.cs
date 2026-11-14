using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolErp.Api;

/// <summary>
/// Every DateTime the API stores is UTC. Writing it with a trailing "Z" lets browsers and
/// the mobile app convert it to the user's local time instead of misreading it as local.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value)) return default;
        var parsed = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return parsed.Kind switch
        {
            DateTimeKind.Local => parsed.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
            _ => parsed
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
