using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/Coding.swift (B2 1fbf81e), :3-34.
public static class Coding
{
    // :19-33
    public static JsonSerializerOptions StorageOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { MaxDepth = SwiftJson.DocumentOptions.MaxDepth };
        options.Converters.Add(new StorageDateConverter());
        options.Converters.Add(new StorageIntegerConverter());
        options.Converters.Add(new StorageDoubleConverter());
        options.Converters.Add(new SwiftUUID.JsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Use this entry point for the shared storage encoder's wire format.</summary>
    // :19-26
    public static byte[] Encode<T>(T value)
    {
        return SwiftJson.Encode(value, StorageOptions, SwiftJsonStyle.Storage);
    }

    // :28-34
    public static T? Decode<T>(ReadOnlySpan<byte> data)
    {
        using var document = JsonDocument.Parse(data.ToArray(), SwiftJson.DocumentOptions);
        if (typeof(T) == typeof(DateTimeOffset))
            return (T)(object)SwiftDecoding.ReadStorageDate(document.RootElement);
        return document.RootElement.Deserialize<T>(StorageOptions);
    }

    // :3-8
    public static T Value<T>(JsonElement container, string key, SwiftDecoding.Reader<T> read, Func<T> fallback) =>
        SwiftDecoding.Value(container, key, read, fallback);

    // :10-17; container.value(...).compactMap at the model call sites.
    public static ValueList<T> LenientList<T>(JsonElement container, string key, SwiftDecoding.Reader<T> read, Func<ValueList<T>> fallback) =>
        SwiftDecoding.LenientList(container, key, read, fallback);

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSorted(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteSorted(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private sealed class StorageIntegerConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException();
            if (reader.TryGetInt64(out var integer)) return integer;
            // Foundation first tries an exactly integral Double, then Decimal at large magnitudes.
            if (!reader.TryGetDouble(out var number) || !double.IsFinite(number) ||
                Math.Truncate(number) != number || number < -9223372036854775808d || number >= 9223372036854775808d)
                throw new JsonException();
            if (Math.Abs(number) < 9007199254740992d) return (long)number;
            if (reader.TryGetDecimal(out var precise) && decimal.Truncate(precise) == precise &&
                precise >= long.MinValue && precise <= long.MaxValue) return (long)precise;
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class StorageDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value) || !double.IsFinite(value))
                throw new JsonException();
            return value;
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            SwiftJson.WriteDouble(writer, value);
    }

    private sealed class StorageDateConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException();
            var text = reader.GetString();
            string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
            if (DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)) return value;
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
