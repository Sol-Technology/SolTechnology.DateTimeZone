using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolTechnology.Time.Serialization;

/// <summary>System.Text.Json converter: <see cref="UnzonedDateTime"/> as an ISO 8601 string without offset, also as a dictionary key.</summary>
public sealed class UnzonedDateTimeJsonConverter : JsonConverter<UnzonedDateTime>
{
    /// <inheritdoc />
    public override UnzonedDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UnzonedDateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());

    /// <inheritdoc />
    public override UnzonedDateTime ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, UnzonedDateTime value, JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());

    private static UnzonedDateTime Parse(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
        {
            throw new JsonException($"Expected a string with an ISO 8601 date-time, got {reader.TokenType}.");
        }

        var text = reader.GetString()!;
        try
        {
            return UnzonedDateTime.Parse(text);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }
}
