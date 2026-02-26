using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLibertyStrmPlugin.Converters;

/// <summary>
///     JSON converter for <c>int?</c> that accepts integer and floating-point values (for example, 0.8).
///     Floating-point values are rounded down via <c>Math.Floor</c>; invalid values are treated as <c>null</c>.
/// </summary>
public class IntNullableConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 1) JSON token is null
        if (reader.TokenType == JsonTokenType.Null) return null;

        // 2) Numeric token (int or float/double)
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Try as int first
            if (reader.TryGetInt32(out var iVal)) return iVal;
            // Then try as double
            if (reader.TryGetDouble(out var dVal))
                return (int)Math.Floor(dVal);
            return null;
        }

        // 3) String token, then parse
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
                return null;

            // Parse integer
            if (int.TryParse(s, out var iVal))
                return iVal;

            // Parse floating-point
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal))
                return (int)Math.Floor(dVal);

            // Invalid string -> null (do not throw)
            return null;
        }

        // Any other token type (true/false, object, array) is invalid for int?
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        // int? -> JSON
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
