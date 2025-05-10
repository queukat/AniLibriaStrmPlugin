using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLibriaStrmPlugin.Converters;

/// <summary>
///     JSON-  int?,      ( 0.8),
///     ё    (Math.Floor).    — ё null.
/// </summary>
public class IntNullableConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 1)  JSON- = null
        if (reader.TokenType == JsonTokenType.Null) return null;

        // 2)    (int  float/double)
        if (reader.TokenType == JsonTokenType.Number)
        {
            //     int
            if (reader.TryGetInt32(out var iVal)) return iVal;
            //   double
            if (reader.TryGetDouble(out var dVal))
                //  
                return (int)Math.Floor(dVal);
            return null;
        }

        // 3)   ,  
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
                return null;

            //  int
            if (int.TryParse(s, out var iVal))
                return iVal;

            //  double
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal))
                return (int)Math.Floor(dVal);

            //    - ё null (   )
            return null;
        }

        //    (true/false, , ) –   int?
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        //  int? -> JSON
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}