using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// R-CODE writing rules (IMPLEMENTING.md 3.8). Bytes are asserted as bytes (R-TEST-4). Source: draft A
// (proto-A SwiftJsonTests, ConventionGapTests), with key order kept ordinal until O-01.
public sealed class SwiftJsonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement FromString(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) writer.WriteStringValue(value);
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void Non_ASCII_is_written_as_raw_UTF8_not_escaped()
    {
        var bytes = SwiftJson.Write(FromString("\U0001F600 \U00002028\U0000FEFF\U0000E000\U000000E9"), SwiftJsonStyle.Storage);
        Assert.Equal("\"\U0001F600 \U00002028\U0000FEFF\U0000E000\U000000E9\"", Encoding.UTF8.GetString(bytes));
        Assert.Equal([0x22, 0xF0, 0x9F, 0x98, 0x80], bytes[..5]);
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void The_relaxed_encoder_the_existing_Coding_cs_uses_escapes_emoji_and_line_separators()
    {
        // The pitfall, and a proven byte difference in the port's current Coding.Encode (draft A, PortEvidence).
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Assert.Equal("\"\\uD83D\\uDE00\"", JsonSerializer.Serialize("\U0001F600", options));
        Assert.Equal("\"\\u2028\"", JsonSerializer.Serialize("\U00002028", options));
        Assert.Equal("\"\\uFEFF\"", JsonSerializer.Serialize("\U0000FEFF", options));
        Assert.Equal("\"\\u001F\"", JsonSerializer.Serialize("\U0000001F", options));
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Oracle", "O-12")]
    public void Controls_use_short_forms_and_lowercase_hex()
    {
        Assert.Equal("\"\\b\\t\\n\\f\\r\\u001f\\u0000\\\"\\\\\U0000007F\"",
            SwiftJson.WriteString(FromString("\b\t\n\f\r\U0000001F\U00000000\"\\\U0000007F"), SwiftJsonStyle.Storage));
    }

    [Fact]
    [Trait("Rule", "R-CODE-4")]
    public void Slash_escaping_depends_on_the_writer()
    {
        var url = FromString("https://host/path");
        Assert.Equal("\"https:\\/\\/host\\/path\"", SwiftJson.WriteString(url, SwiftJsonStyle.Storage));
        Assert.Equal("\"https://host/path\"", SwiftJson.WriteString(url, SwiftJsonStyle.SerializationCompact));
    }

    [Fact]
    [Trait("Rule", "R-CODE-9")]
    [Trait("Oracle", "O-01")]
    public void Sorted_keys_stay_ordinal_until_the_oracle_and_Foundation_order_differs_only_on_user_keys()
    {
        var value = Parse("{\"b\":1,\"KEY10\":2,\"Authorization\":3,\"KEY2\":4,\"accept\":5,\"a\":6,\"B\":7}");
        Assert.Equal("{\"Authorization\":3,\"B\":7,\"KEY10\":2,\"KEY2\":4,\"a\":6,\"accept\":5,\"b\":1}",
            SwiftJson.WriteString(value, SwiftJsonStyle.Storage));
        string[] keys = ["b", "KEY10", "Authorization", "KEY2", "accept", "a", "B"];
        Assert.Equal(["a", "accept", "Authorization", "B", "b", "KEY2", "KEY10"], keys.OrderBy(k => k, FoundationKeyComparer.Instance));
        string[] fixedKeys = ["updatedAt", "id", "isEnabled", "catalogID", "values", "tools", "connectedAt", "serverVersion"];
        Assert.Equal(fixedKeys.OrderBy(k => k, StringComparer.Ordinal), fixedKeys.OrderBy(k => k, FoundationKeyComparer.Instance));
    }

    [Fact]
    [Trait("Rule", "R-CODE-4")]
    [Trait("Oracle", "O-04")]
    public void Pretty_layout_is_Apples_key_space_colon_space_and_two_space_indent()
    {
        var value = Parse("{\"c\":[],\"a\":[1,2],\"b\":{}}");
        Assert.Equal("{\n  \"a\" : [\n    1,\n    2\n  ],\n  \"b\" : {\n\n  },\n  \"c\" : [\n\n  ]\n}",
            SwiftJson.WriteString(value, SwiftJsonStyle.PrettySorted));
    }

    [Fact]
    [Trait("Rule", "R-CODE-5")]
    public void Doubles_use_Swift_text_and_integer_literals_stay()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            SwiftJson.WriteDouble(writer, "x", 0.00001);
            SwiftJson.WriteDouble(writer, "y", 1.0);
            writer.WriteNumber("n", 42L);
            SwiftJson.WriteDouble(writer, "m", -0.0);
            writer.WriteEndObject();
        }
        var element = JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
        Assert.Equal("{\"m\":-0,\"n\":42,\"x\":1e-05,\"y\":1}", SwiftJson.WriteString(element, SwiftJsonStyle.Storage));
        var dotnet = JsonSerializer.SerializeToElement(new { x = 0.00001 });
        Assert.Equal("{\"x\":1E-05}", dotnet.GetRawText()); // the pitfall
        Assert.Equal("{\"x\":1e-05}", SwiftJson.WriteString(dotnet, SwiftJsonStyle.Storage));
    }

    [Fact]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-09")]
    public void A_duplicated_key_keeps_its_last_value_once()
    {
        Assert.Equal("{\"a\":2}", SwiftJson.WriteString(Parse("{\"a\":1,\"a\":2}"), SwiftJsonStyle.Storage));
    }

    [Fact]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-24")]
    public void Nesting_past_64_levels_needs_the_document_options()
    {
        static string Nested(int depth) => new string('[', depth) + new string(']', depth);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(Nested(65))); // the pitfall: the default stops at 64
        using var deep = JsonDocument.Parse(Nested(512), SwiftJson.DocumentOptions);
        Assert.Equal(JsonValueKind.Array, deep.RootElement.ValueKind);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(Nested(513), SwiftJson.DocumentOptions));
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Rule", "R-CODE-6")]
    [Trait("Rule", "R-UUID-1")]
    public void Storage_bytes_of_a_model_match_Swift_end_to_end()
    {
        var id = Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef");
        var created = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero).AddTicks(9_999_999);
        var thread = new ThreadSample(id, "caf\U000000E9 \U0001F600 a/b", 0.00001, created, new(["x", "y"]), null);
        var json = Encoding.UTF8.GetString(SwiftJson.Encode(thread, SampleCoding.StorageOptions, SwiftJsonStyle.Storage));
        Assert.Equal(
            "{\"createdAt\":\"2026-09-22T10:00:00Z\",\"id\":\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\"," +
            "\"sortOrder\":1e-05,\"tags\":[\"x\",\"y\"],\"title\":\"caf\U000000E9 \U0001F600 a\\/b\"}", json);
        var reloaded = JsonSerializer.Deserialize<ThreadSample>(json, SampleCoding.StorageOptions)!;
        Assert.Equal(created.AddTicks(-9_999_999), reloaded.CreatedAt); // the fraction is lost, as in Swift
        Assert.Equal(thread with { CreatedAt = reloaded.CreatedAt }, reloaded);
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    public void Output_is_deterministic()
    {
        var value = Parse("{\"z\":[{\"z\":2,\"a\":\"a/b\"}],\"a\":\"https://host/path\"}");
        Assert.Equal("{\"a\":\"https:\\/\\/host\\/path\",\"z\":[{\"a\":\"a\\/b\",\"z\":2}]}", SwiftJson.WriteString(value, SwiftJsonStyle.Storage));
        Assert.Equal(SwiftJson.Write(value, SwiftJsonStyle.Storage), SwiftJson.Write(value, SwiftJsonStyle.Storage));
    }
}
