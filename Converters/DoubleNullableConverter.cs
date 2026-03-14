using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLibertyStrmPlugin.Converters;

/// <summary>
///     JSON converter for <c>double?</c> that accepts numbers and numeric strings.
///     Invalid values are treated as <c>null</c>.
/// </summary>
public class DoubleNullableConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetDouble(out var value) ? value : null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
