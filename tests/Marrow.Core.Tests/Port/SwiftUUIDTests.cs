using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// R-UUID-1 (IMPLEMENTING.md 3.12). Source: draft A (strict parser) and draft B (converter).
public sealed class SwiftUUIDTests
{
    private static readonly Guid Id = Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef");

    [Fact]
    [Trait("Rule", "R-UUID-1")]
    [Trait("Decision", "ED-073")]
    public void UuidString_is_uppercase_where_Guid_ToString_is_lowercase()
    {
        Assert.Equal("0123ABCD-89AB-CDEF-0123-456789ABCDEF", SwiftUUID.UuidString(Id));
        Assert.Equal("0123abcd-89ab-cdef-0123-456789abcdef", Id.ToString()); // the pitfall
        Assert.Equal("0123ABCD-89AB-CDEF-0123-456789ABCDEF.json", SwiftUUID.UuidString(Id) + ".json"); // Storage.swift:51
    }

    [Theory]
    [Trait("Rule", "R-UUID-1")]
    [InlineData("0123ABCD-89AB-CDEF-0123-456789ABCDEF", true)]
    [InlineData("0123abcd-89ab-cdef-0123-456789abcdef", true)]
    [InlineData("{0123abcd-89ab-cdef-0123-456789abcdef}", false)]
    [InlineData("0123abcd89abcdef0123456789abcdef", false)]
    [InlineData(" 0123abcd-89ab-cdef-0123-456789abcde", false)]
    [InlineData("0123abcd-89ab-cdef-0123-456789abcdeg", false)]
    [InlineData("", false)]
    public void UUID_parsing_takes_only_the_hyphenated_36_character_form(string text, bool accepted)
    {
        Assert.Equal(accepted, SwiftUUID.Parse(text) is not null);
        if (!accepted) return;
        Assert.Equal(Id, SwiftUUID.Parse(text));
    }

    [Fact]
    [Trait("Rule", "R-UUID-1")]
    public void Guid_TryParse_accepts_forms_Foundation_rejects()
    {
        Assert.True(Guid.TryParse("{0123abcd-89ab-cdef-0123-456789abcdef}", out _)); // the pitfall
        Assert.True(Guid.TryParse("0123abcd89abcdef0123456789abcdef", out _));
    }

    [Fact]
    [Trait("Rule", "R-CODE-7")]
    public void The_converter_writes_uuidString_and_reads_strictly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SwiftUUID.JsonConverter());
        Assert.Equal("\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\"", JsonSerializer.Serialize(Id, options));
        Assert.Equal(Id, JsonSerializer.Deserialize<Guid>("\"0123abcd-89ab-cdef-0123-456789abcdef\"", options));
        Assert.Throws<DecodingError>(() => JsonSerializer.Deserialize<Guid>("\"{0123abcd-89ab-cdef-0123-456789abcdef}\"", options));
    }
}
