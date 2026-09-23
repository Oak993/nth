using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/Coding.swift (B2 1fbf81e), :5-6 and :28-34. Swift Int decoding under the storage
// decoder and inside value(_:default:). Foundation JSONDecoder's integer slow path accepts an exactly integral JSON
// number (1.0, 1e3) but never a numeric string, a fraction or a number Int cannot hold (R-CODE-1; swift-6.0-RELEASE
// JSONDecoder.swift). Int is 64-bit, so the port type is long. Whether a fraction whose parsed Double is exactly
// integral (1.0000000000000001), or an out-of-range integer whose parsed Double is -2^63
// (-9223372036854775809), is read is open (O-09: big integers and number text in JSONDecoder).
public sealed class CodingIntegerTests
{
    private static JsonElement Root(string number) => JsonDocument.Parse("{\"x\":" + number + "}").RootElement.Clone();

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.0", 1L)]
    [InlineData("1e3", 1000L)]
    [InlineData("-1.0", -1L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Integral_number_forms_are_a_present_Int_and_not_a_miss(string number, long expected)
    {
        Assert.Equal(expected, Coding.Value(Root(number), "x", SwiftDecoding.ReadInt, () => -999L));
    }

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("9223372036854775808")]
    [InlineData("1e19")]
    public void A_number_Int_cannot_hold_gives_the_default(string number)
    {
        Assert.Equal(-999L, Coding.Value(Root(number), "x", SwiftDecoding.ReadInt, () => -999L));
    }

    [Theory]
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.0", 1L)]
    [InlineData("1e3", 1000L)]
    [InlineData("-1.0", -1L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void The_storage_decoder_reads_integral_number_forms_as_Int(string number, long expected)
    {
        Assert.Equal(expected, Coding.Decode<long>(Encoding.UTF8.GetBytes(number)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    [Trait("Oracle", "O-09")]
    public void A_fraction_that_rounds_to_an_integral_Double_is_read_as_Int_until_the_oracle_answers()
    {
        // Rests on O-09 (number text in JSONDecoder): no pinned line says whether Int decoding judges the JSON text,
        // which is a fraction, or the Double it parses to, which is exactly 1. The parked choice judges the parsed
        // Double (SwiftDecoding.ReadInt, the R-CODE-1 reader), so the text reads as 1. Only the macOS oracle can settle it.
        Assert.Equal(1L, Coding.Decode<long>("1.0000000000000001"u8));
    }

    [Theory]
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("9223372036854775808")]
    [InlineData("1e19")]
    public void The_storage_decoder_refuses_what_Swift_Int_refuses(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<long>(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    [Trait("Oracle", "O-09")]
    public void An_out_of_range_negative_integer_gives_the_default_until_the_oracle_answers()
    {
        // The parsed Double is exactly -2^63, so Int(exactly:) would give Int.min. The default comes only
        // from the parked Decimal re-check at 2^53 and above in SwiftDecoding.ReadInt; O-09 settles it.
        Assert.Equal(-999L, Coding.Value(Root("-9223372036854775809"), "x", SwiftDecoding.ReadInt, () => -999L));
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    [Trait("Oracle", "O-09")]
    public void The_storage_decoder_refuses_an_out_of_range_negative_integer_until_the_oracle_answers()
    {
        // The parsed Double is exactly -2^63, so Int(exactly:) would give Int.min. Rejection comes only
        // from the parked Decimal re-check at 2^53 and above in SwiftDecoding.ReadInt; O-09 settles it.
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<long>("-9223372036854775809"u8));
    }
}
