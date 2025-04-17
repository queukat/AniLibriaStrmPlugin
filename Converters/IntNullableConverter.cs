using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLibriaStrmPlugin.Converters;

/// <summary>
///     Кастомный JSON-конвертер для int?, позволяющий безопасно обрабатывать нецелые значения (например 0.8),
///     превращая их в целое число путём отбрасывания дробной части (Math.Floor). Если парсинг невозможен — вернёт null.
/// </summary>
public class IntNullableConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 1) Если JSON-значение = null
        if (reader.TokenType == JsonTokenType.Null) return null;

        // 2) Если это число (int или float/double)
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Сначала пытаемся прочитать как int
            if (reader.TryGetInt32(out var iVal)) return iVal;
            // Иначе читаем double
            if (reader.TryGetDouble(out var dVal))
                // Отбрасываем дробь
                return (int)Math.Floor(dVal);
            return null;
        }

        // 3) Если это строка, попробуем распарсить
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
                return null;

            // Сначала int
            if (int.TryParse(s, out var iVal))
                return iVal;

            // Или double
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal))
                return (int)Math.Floor(dVal);

            // Если невозможно спарсить - вернём null (или можно бросить исключение)
            return null;
        }

        // Любые другие типы (true/false, объект, массив) – невалидны для int?
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        // Сериализация int? -> JSON
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}