using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Support;

public static class Coding
{
    public static JsonSerializerOptions StorageOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new StorageDateConverter());
        options.Converters.Add(new StorageIntegerConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Use this entry point for storage: sorting and slash escaping are part of its wire format.</summary>
    public static byte[] Encode<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, StorageOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = StorageOptions.Encoder }))
            WriteSorted(writer, element);
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).Replace("/", "\\/", StringComparison.Ordinal));
    }

    public static T? Decode<T>(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<T>(data, StorageOptions);

    public static T Value<T>(JsonElement container, string key, Func<T> fallback)
    {
        if (container.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null)
        {
            try
            {
                var value = field.Deserialize<T>(StorageOptions);
                if (value is not null) return value;
            }
            catch (JsonException) { }
        }
        return fallback();
    }

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
