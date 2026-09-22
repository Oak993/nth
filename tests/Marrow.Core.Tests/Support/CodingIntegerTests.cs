using System.Text.Json;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class CodingIntegerTests
{
    // Foundation JSONDecoder's integer slow path accepts integral JSON numbers,
    // but never numeric strings. Source: swift-6.0-RELEASE JSONDecoder.swift.
    [Theory]
    [InlineData("1.0", 1L)]
    [InlineData("1e3", 1000L)]
    [InlineData("-1.0", -1L)]
    [InlineData("1.0000000000000001", 1L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Integral_number_forms_use_Foundation_integer_decoding(string number, long expected)
    {
        using var document = JsonDocument.Parse("{\"x\":" + number + "}");
        Assert.Equal(expected, Coding.Value(document.RootElement, "x", () => -999L));
    }
}
