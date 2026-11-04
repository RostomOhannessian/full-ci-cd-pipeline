using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catalog.Api.Http;

/// <summary>
/// Writes a point in time as UTC with a <c>Z</c>, such as <c>2026-10-11T12:00:00Z</c>, which is how the contract shows every <c>date-time</c> (plan section 8.6). The serializer would write <c>+00:00</c> for a
/// value with a zero offset, and a client should never have to read two spellings of the same instant. It reads any ISO 8601 text and gives back the instant.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
    }
}
