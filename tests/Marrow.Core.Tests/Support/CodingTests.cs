using System.Text;
using System.Text.Json;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class CodingTests
{
    [Fact]
    [Trait("Req", "PR-STORE-017")]
    public void Storage_sorts_keys_recursively_is_compact_and_escapes_slashes()
    {
        var payload = new Dictionary<string, object?>
        {
            ["z"] = new[] { new Dictionary<string, object?> { ["z"] = 2, ["a"] = "a/b" } },
            ["a"] = "https://host/path"
        };
        Assert.Equal("{\"a\":\"https:\\/\\/host\\/path\",\"z\":[{\"a\":\"a\\/b\",\"z\":2}]}", Encoding.UTF8.GetString(Coding.Encode(payload)));
        Assert.Equal(Coding.Encode(payload), Coding.Encode(payload));
    }

    [Fact]
    [Trait("Req", "PR-STORE-017")]
    [Trait("Req", "PR-STORE-018")]
    public void Dates_write_UTC_ISO8601_without_fractional_seconds_and_read_offsets()
    {
        var date = new DateTimeOffset(2026, 9, 22, 5, 0, 1, TimeSpan.FromHours(3)).AddMilliseconds(500);
        Assert.Equal("\"2026-09-22T02:00:01Z\"", Encoding.UTF8.GetString(Coding.Encode(date)));
        var parsed = Coding.Decode<DateTimeOffset>(Encoding.UTF8.GetBytes("\"2026-09-22T05:00:01+03:00\""));
        Assert.Equal(date.AddMilliseconds(-500), parsed);
        Assert.Equal(DateTimeOffset.UnixEpoch, Coding.Decode<DateTimeOffset>(Encoding.UTF8.GetBytes("\"1970-01-01T00:00:00Z\"")));
    }

    [Fact]
    [Trait("Req", "PR-STORE-017")]
    public void Escaped_slash_format_round_trips_backslashes_quotes_controls_and_unicode()
    {
        const string value = "\\/\"\n\t\u00e9";
        var encoded = Coding.Encode(value);
        Assert.Equal(value, Coding.Decode<string>(encoded));
        Assert.Contains("\\/", Encoding.UTF8.GetString(encoded), StringComparison.Ordinal);
        Assert.DoesNotContain((byte)'\n', encoded);
    }

    [Theory]
    [Trait("Req", "PR-STORE-020")]
    [InlineData("{}")]
    [InlineData("{\"x\":null}")]
    [InlineData("{\"x\":\"1\"}")]
    [InlineData("{\"x\":false}")]
    [InlineData("{\"x\":[]}")]
    [InlineData("{\"x\":{}}")]
    [InlineData("{\"x\":1.5}")]
    public void Missing_null_and_type_errors_evaluate_fallback_once(string json)
    {
        using var document = JsonDocument.Parse(json);
        var calls = 0;
        var result = Coding.Value(document.RootElement, "x", () => { calls++; return 42L; });
        Assert.Equal(42, result); Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")]
    public void Present_value_does_not_evaluate_fallback_and_keys_are_case_sensitive()
    {
        using var document = JsonDocument.Parse("{\"x\":0,\"X\":99}");
        Assert.Equal(0, Coding.Value<long>(document.RootElement, "x", () => throw new Exception("must not evaluate")));
        Assert.Equal(99, Coding.Value(document.RootElement, "X", () => -1L));
        Assert.Equal(-1, Coding.Value(document.RootElement, "missing", () => -1L));
    }

    [Theory]
    [Trait("Req", "PR-STORE-018")]
    [Trait("Req", "PR-STORE-020")]
    [InlineData("{\"date\":\"yesterday\"}")]
    [InlineData("{\"date\":123}")]
    [InlineData("{\"date\":\"2026-99-99T99:99:99Z\"}")]
    public void Invalid_date_reads_as_the_callers_default(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(DateTimeOffset.UnixEpoch, Coding.Value(document.RootElement, "date", () => DateTimeOffset.UnixEpoch));
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")]
    public void One_invalid_array_member_falls_back_the_whole_field()
    {
        using var document = JsonDocument.Parse("{\"values\":[1,\"bad\",3]}");
        Assert.Equal(new long[] { 9 }, Coding.Value(document.RootElement, "values", () => new long[] { 9 }));
    }

    [Fact]
    [Trait("Req", "PR-STORE-018")]
    public void Storage_options_are_shared_and_frozen()
    {
        Assert.Same(Coding.StorageOptions, Coding.StorageOptions);
        Assert.True(Coding.StorageOptions.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => Coding.StorageOptions.WriteIndented = true);
    }
}
