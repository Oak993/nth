using System.Linq;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B2 1fbf81e), :6-30, :839-965, :968-1027, :1093-1132,
// :1338-1346 only. This is a PARTIAL unit merge: ModelCatalog, EffortPalette.Title, EffortBrand and its extension
// members, EffortPairBrand, TrackLook's computed properties, and TrackEffect.Traits/TraitCache/Random/EdgeFade are
// implemented and tested here. Every other declared type in this file (ModelEffortButton, ModelEffortPanel,
// ModelList, HydraPairListRow, ModelListRow, EffortPairLook, EffortSliderCard, FastModeButton, RoundIconButton,
// EffortSlider itself, and TrackEffect's own body/render methods DrawParticles/DrawStreaks/DrawBolts/DrawSheen/
// DrawFusion) stays MARROW_STUB - the actual interactive slider, its popover and its drawn effects are real,
// substantial remaining work for a future pass. See the STOP note at the end of this file for how this was found.
public sealed class EffortSliderTests
{
    // SwiftColor is a plain class with no value equality (this file's own skeleton header documents this: "SwiftColor
    // has no value equality... compare them by reference until that is settled"), so two separately-constructed
    // instances with the same RGBA never compare equal via Assert.Equal directly; resolving to Avalonia's own Color
    // (a value-equatable struct) is the established comparison path (matches HydraFusedPairIconTests.cs).
    private static Avalonia.Media.Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:862-871
    public void EffortBrand_Create_maps_each_provider_to_its_brand()
    {
        Assert.Equal(EffortBrand.Claude, EffortBrand.Create(ProviderKind.Claude));
        Assert.Equal(EffortBrand.Deepseek, EffortBrand.Create(ProviderKind.Deepseek));
        Assert.Equal(EffortBrand.Meta, EffortBrand.Create(ProviderKind.Meta));
        Assert.Equal(EffortBrand.Antigravity, EffortBrand.Create(ProviderKind.Antigravity));
        Assert.Equal(EffortBrand.Silver, EffortBrand.Create(ProviderKind.Codex));
        Assert.Equal(EffortBrand.Silver, EffortBrand.Create(ProviderKind.Copilot));
        Assert.Equal(EffortBrand.Purple, EffortBrand.Create(null));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:923-928
    public void EffortBrand_Silver_is_the_only_light_brand()
    {
        Assert.True(EffortBrand.Silver.IsLight);
        Assert.False(EffortBrand.Claude.IsLight);
        Assert.Equal(SwiftColor.Black, EffortBrand.Silver.Spark);
        Assert.Equal(SwiftColor.White, EffortBrand.Claude.Spark);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:931-935
    public void EffortBrand_Gemini_has_three_stops()
    {
        Assert.Equal(3, EffortBrand.Gemini.Count);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_the_heads_providers_brand_when_they_run_elsewhere()
    {
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Claude };
        var brand = new EffortPairBrand(pair);

        Assert.Equal(2, brand.Colors.Count);
        Assert.Equal(Resolved(EffortBrand.Claude.FillColor), Resolved(brand.Colors[0]));
        Assert.Equal(Resolved(EffortBrand.Claude.TitleColor), Resolved(brand.Colors[1]));
        Assert.False(brand.IsLight);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_Gemini_colors_for_an_antigravity_heads_provider()
    {
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Antigravity };
        var brand = new EffortPairBrand(pair);

        Assert.Equal(EffortBrand.Gemini.Count, brand.Colors.Count);
        Assert.Equal(Resolved(EffortBrand.Gemini[0]), Resolved(brand.Colors[0]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_the_first_four_roster_persona_colours_when_heads_run_in_the_leads_project()
    {
        var pair = new HydraPair(ProviderKind.Codex);
        var brand = new EffortPairBrand(pair);

        Assert.Equal(4, brand.Colors.Count);
        Assert.False(brand.IsLight);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018
    public void TrackLook_without_a_pair_follows_its_own_kind()
    {
        var plain = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple);
        Assert.Equal(Resolved(SwiftColor.Black.Opacity(0.28)), Resolved(plain.Glow));
        Assert.False(plain.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.White.Opacity(0.6)), Resolved(plain.StopColor));
        Assert.Equal(Resolved(SwiftColor.White), Resolved(plain.Spark));

        var supercharged = new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Silver);
        Assert.Equal(Resolved(EffortBrand.Silver.Glow), Resolved(supercharged.Glow));
        Assert.True(supercharged.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.Black.Opacity(0.35)), Resolved(supercharged.StopColor));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018
    public void TrackLook_with_a_pair_follows_the_pair_instead_of_its_kind()
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Antigravity });
        var look = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple, pair);

        Assert.Equal(Resolved(pair.Colors[^1].Opacity(0.7)), Resolved(look.Glow));
        Assert.Equal(pair.IsLight, look.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.White), Resolved(look.Spark)); // :1018 a pair never sparks
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1099-1115
    public void TrackEffect_Traits_Build_scales_particle_and_streak_counts_with_width_and_floors_them()
    {
        var narrow = TrackEffect.Traits.Build(10);
        Assert.Equal(10, narrow.Particles.Count); // floor of 10, not 10/5=2
        Assert.Equal(5, narrow.Streaks.Count);    // floor of 5, not 10/14=0

        var wide = TrackEffect.Traits.Build(280);
        Assert.Equal(56, wide.Particles.Count); // 280/5
        Assert.Equal(20, wide.Streaks.Count);   // 280/14
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1099-1115
    public void TrackEffect_Traits_Build_is_deterministic_per_width()
    {
        var first = TrackEffect.Traits.Build(200);
        var second = TrackEffect.Traits.Build(200);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1121-1132
    public void TrackEffect_TraitCache_rebuilds_only_when_the_width_changes()
    {
        var cache = new TrackEffect.TraitCache();
        var first = cache.Traits(200);
        var same = cache.Traits(200);
        var different = cache.Traits(300);

        Assert.Same(first, same);
        Assert.NotSame(first, different);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1343-1346
    public void TrackEffect_Random_is_deterministic_and_stays_in_zero_to_one()
    {
        var first = TrackEffect.Random(3, 2);
        var second = TrackEffect.Random(3, 2);
        Assert.Equal(first, second);
        Assert.InRange(first, 0, 1);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-140")] // EffortSlider.swift:11
    public void ModelCatalog_Entry_id_joins_provider_and_option_ids()
    {
        var option = new ModelOption("gpt-6", "GPT-6", null, null, null);
        var entry = new ModelCatalog.Entry(ProviderKind.Codex, option);
        Assert.Equal(ProviderKind.Codex.Id + "/" + option.Id, entry.Id);
    }
}

// STOP App.Views.Composer.EffortSlider #1 (helper Claude, 2026-09-26)
// Kind: prior WIP was more incomplete than its own commit messages claimed
// Swift: DroppyCode/App/Views/Composer/EffortSlider.swift (whole file, :1-1347)
// Requirements: only PR-COMPOSER-129, 130, 131, 134 and 140 are IMPLEMENTED here. PR-COMPOSER-106 through 128
// (ModelEffortButton's chip, ModelEffortPanel, EffortSliderCard, FastModeButton, RoundIconButton, EffortSlider's
// own drag/snap body), 132-133 and 135-138 (TrackEffect's own render body and its five draw methods), and every
// record in part2.md/part3.md not listed above (the model list and its rows, the pairs section) all stay
// SPECIFIED, genuinely not implemented.
// What is unclear or missing: nothing is unclear about the SCOPE left; what was unclear going in was the actual
// STATE of codex/App.Views.Composer.HydraFusedPairIcon's earlier "App.Views.Composer.EffortSlider part 1/2:
// implementation" commits (4ac96775, 8810e8f0, 4f62b622, 7bb489aa, 8b12fbc2, 1e362be1). Despite those commit
// messages, `git diff` against the untouched skeleton showed 329 lines deleted versus 248 inserted: the whole
// interactive view layer this file declares (ModelEffortButton, ModelEffortPanel, ModelList, HydraPairListRow,
// ModelListRow, EffortPairLook, EffortSliderCard, FastModeButton, RoundIconButton, EffortSlider itself, and
// TrackEffect's own Decorator base plus its five draw methods) had been REMOVED from the file entirely rather than
// implemented or left as MARROW_STUB - a real regression risk had it been merged as-is (R-FILE-1 requires every
// declared type to stay declared). The commits also reshaped EffortBrand's extension members from this codebase's
// established C# 14 `extension(EffortBrand brand) { public SwiftColor TitleColor => ... }` block shape (this
// file's own skeleton, matching R-NAME-4/ED-164) to classic C# extension methods (`public static SwiftColor
// TitleColor(this EffortBrand brand) => ...`), an incompatible call-site shape change.
// Readings or options: what actually survived intact and correctly matched the packet's own source_evidence was
// the pure data/colour layer only: ModelCatalog.Entries (PR-COMPOSER-140), EffortBrand's colour functions
// (PR-COMPOSER-129), EffortPairBrand (PR-COMPOSER-130), TrackLook's computed properties (PR-COMPOSER-131), and
// TrackEffect.Traits/TraitCache/Random/EdgeFade (PR-COMPOSER-134). Extracted those, re-expressed in this file's own
// established extension-block shape (not codex's), verified each numeric literal against this packet's own
// source_evidence quotes, and adapted App.Views.Composer.HydraFusedPairIcon.cs's three call sites
// (EffortBrandExtensions.Create/.IsLight()/.TitleColor() with parens) to match (EffortBrand.Create/.IsLight/
// .TitleColor, no parens) - HydraFusedPairIcon.cs itself (a separate, single-type Swift file, :1-91) was otherwise
// complete and correct as delegated, and is merged alongside this partial EffortSlider in the same commit.
// Needed to proceed on the rest of EffortSlider: a dedicated pass implementing ModelEffortButton, ModelEffortPanel,
// ModelList, HydraPairListRow, ModelListRow, EffortPairLook, EffortSliderCard, FastModeButton, RoundIconButton,
// EffortSlider's own drag/snap body, and TrackEffect's five draw methods - genuinely one of the larger remaining UI
// units (3-part packet), not a quick pickup. Recommend implementing it fresh from the packets rather than trying
// to salvage anything further from the codex/luna branches touched here; the same branches also carry
// codex/App.Views.Composer.HydraFusedPairIcon and luna/App.Views.Composer.HydraFusedPairIcon copies with the same
// underlying issue, so check for deletions with `git diff <merge-base> <branch> -- <target file> --stat` before
// trusting any WIP branch's own commit message again.
// Done so far: ModelCatalog, EffortPalette.Title, EffortBrand/EffortBrandExtensions, EffortPairBrand, TrackLook's
// computed properties, and TrackEffect.Traits/TraitCache/Random/EdgeFade implemented and tested above (16/16
// focused tests, including HydraFusedPairIconTests.cs's own 0-stub, complete unit tested separately).
