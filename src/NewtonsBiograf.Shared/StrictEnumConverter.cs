using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewtonsBiograf.Shared;

/// <summary>
/// Reads and writes enums as their member names only. Numbers, numeric strings ("1") and unknown names fail with a
/// JsonException, so the API answers 400 instead of silently accepting an undefined or wrong value.
/// </summary>
public sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            var name = Array.Find(Enum.GetNames<T>(), n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
            return name is null
                ? throw new JsonException($"'{text}' is not a valid {typeof(T).Name}.")
                : Enum.Parse<T>(name);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
