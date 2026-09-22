using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/Coding.swift (B2 1fbf81e), :5-6 and :28-34. Swift Int decoding under the storage
// decoder and inside value(_:default:). Foundation JSONDecoder's integer slow path accepts an exactly integral JSON
// number (1.0, 1e3) but never a numeric string, a fraction or a number Int cannot hold (R-CODE-1; swift-6.0-RELEASE
// JSONDecoder.swift). Int is 64-bit, so the port type is long.
public sealed class CodingIntegerTests
{
    private static JsonElement Root(string number) => JsonDocument.Parse("{\"x\":" + number + "}").RootElement.Clone();

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.0", 1L)]
    [InlineData("1e3", 1000L)]
    [InlineData("-1.0", -1L)]
    [InlineData("1.0000000000000001", 1L)]
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
    [InlineData("-9223372036854775809")]
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
    [InlineData("1.0000000000000001", 1L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void The_storage_decoder_reads_integral_number_forms_as_Int(string number, long expected)
    {
        Assert.Equal(expected, Coding.Decode<long>(Encoding.UTF8.GetBytes(number)));
    }

    [Theory]
    [Trait("Rule", "R-CODE-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    [InlineData("1e19")]
    public void The_storage_decoder_refuses_what_Swift_Int_refuses(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<long>(Encoding.UTF8.GetBytes(json)));
    }
}
