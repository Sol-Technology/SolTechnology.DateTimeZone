using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolTechnology.Time.Serialization;

/// <summary>System.Text.Json converter: <see cref="DateTimeZone"/> as its RFC 9557 string, also as a dictionary key.</summary>
public sealed class DateTimeZoneJsonConverter : JsonConverter<DateTimeZone>
{
    /// <inheritdoc />
    public override DateTimeZone Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeZone value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());

    /// <inheritdoc />
    public override DateTimeZone ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, DateTimeZone value, JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());

    private static DateTimeZone Parse(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
        {
            throw new JsonException($"Expected a string with an RFC 9557 date-time, got {reader.TokenType}.");
        }

        var text = reader.GetString()!;
        try
        {
            return DateTimeZone.Parse(text);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }
}
