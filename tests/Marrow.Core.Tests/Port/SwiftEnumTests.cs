using System.Diagnostics;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-ENUM rules (IMPLEMENTING.md 3.9). Source: draft C (raw values) and draft B (union coverage).
public sealed class SwiftEnumTests
{
    [Fact]
    [Trait("Rule", "R-ENUM-1")]
    [Trait("Decision", "ED-075")]
    public void Raw_values_are_exact_and_allCases_keeps_declaration_order()
    {
        Assert.Equal("opencodeGo", SwiftEnum<ProviderKindSample>.RawValue(ProviderKindSample.OpencodeGo));
        Assert.Equal(ProviderKindSample.CatppuccinMocha, SwiftEnum<ProviderKindSample>.FromRawValue("catppuccinMocha"));
        Assert.Null(SwiftEnum<ProviderKindSample>.FromRawValue("Codex"));
        Assert.Null(SwiftEnum<ProviderKindSample>.FromRawValue("1"));
        Assert.Equal([ProviderKindSample.Codex, ProviderKindSample.Claude, ProviderKindSample.OpencodeGo, ProviderKindSample.CatppuccinMocha],
            SwiftEnum<ProviderKindSample>.AllCases);
        // The pitfall: Enum.TryParse takes numbers and ignores case where Swift's init?(rawValue:) never does.
        Assert.True(Enum.TryParse<ProviderKindSample>("1", out var numeric) && numeric == ProviderKindSample.Claude);
        Assert.True(Enum.TryParse<ProviderKindSample>("codex", ignoreCase: true, out _));
    }

    [Fact]
    [Trait("Rule", "R-ENUM-1")]
    public void An_enum_member_without_a_raw_value_is_refused()
    {
        var error = Assert.Throws<TypeInitializationException>(() => SwiftEnum<BrokenRawEnum>.AllCases);
        Assert.IsType<SwiftTrapException>(error.InnerException);
    }

    [Fact]
    [Trait("Rule", "R-ENUM-1")]
    [Trait("Rule", "R-STR-2")]
    public void A_raw_value_matches_canonically_as_a_Swift_string_switch_does()
    {
        Assert.Equal(CanonicalRawSample.Kelvin, SwiftEnum<CanonicalRawSample>.FromRawValue("\U0000212A")); // KELVIN SIGN is K
        Assert.Equal(CanonicalRawSample.Cafe, SwiftEnum<CanonicalRawSample>.FromRawValue("cafe\U00000301"));
        Assert.Null(SwiftEnum<CanonicalRawSample>.FromRawValue("k"));
        Assert.Equal("K", SwiftEnum<CanonicalRawSample>.RawValue(CanonicalRawSample.Kelvin));
    }

    [Fact]
    [Trait("Rule", "R-ENUM-1")]
    [Trait("Rule", "R-CODE-1")]
    public void A_raw_value_decodes_exactly_and_an_unknown_one_is_dataCorrupted()
    {
        using var known = JsonDocument.Parse("\"claude\"");
        Assert.Equal(ProviderKindSample.Claude, SwiftDecoding.ReadRawValue<ProviderKindSample>(known.RootElement));
        using var unknown = JsonDocument.Parse("\"Claude\"");
        Assert.Equal("dataCorrupted", Assert.Throws<DecodingError>(() => SwiftDecoding.ReadRawValue<ProviderKindSample>(unknown.RootElement)).Kind);
    }

    [Fact]
    [Trait("Rule", "R-ENUM-3")]
    [Trait("Decision", "ED-075")]
    public void Every_union_switch_is_run_over_one_sample_per_case()
    {
        TransportSample[] samples = [new TransportSample.Stdio("npx"), new TransportSample.Http("https://h"), new TransportSample.OAuth("https://o")];
        foreach (var sample in UnionCoverage.AssertCovers(samples)) Assert.NotNull(sample.Name);
        ContentSample[] content = [new ContentSample.User(new("hi", ValueList<string>.Empty, null, null)), new ContentSample.Todos(ValueList<string>.Empty)];
        foreach (var sample in UnionCoverage.AssertCovers(content)) Assert.NotNull(sample.CaseName);
    }

    [Fact]
    [Trait("Rule", "R-ENUM-3")]
    public void A_forgotten_union_case_is_caught_by_the_harness_not_the_compiler()
    {
        TransportSample[] samples = [new TransportSample.Stdio("npx"), new TransportSample.Http("https://h"), new TransportSample.OAuth("https://o")];
        var covered = UnionCoverage.AssertCovers(samples);
        Assert.Throws<UnreachableException>(() => { foreach (var sample in covered) _ = sample.NameForgettingOAuth; });
    }

    [Fact]
    [Trait("Rule", "R-ENUM-3")]
    public void An_incomplete_sample_set_fails_instead_of_passing_vacuously()
    {
        var error = Record.Exception(() => UnionCoverage.AssertCovers<TransportSample>(new TransportSample.Stdio("x")));
        Assert.NotNull(error);
        Assert.Contains("Http, OAuth", error.Message, StringComparison.Ordinal);
    }
}
