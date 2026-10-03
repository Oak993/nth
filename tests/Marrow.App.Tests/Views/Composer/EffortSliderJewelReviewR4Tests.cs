using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 4 on cb4310571): each test states what the unit's code or its tests
// must do and fails on the head reviewed.
public sealed class EffortSliderJewelReviewR4Tests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test that verifies a requirement carries that requirement's Req trait, not another's
    public void REVIEW_no_jewel_knob_test_credits_the_pair_caption_requirement_it_does_not_test()
    {
        // PR-COMPOSER-116 is the pair caption (EffortSlider.swift:642-651: .subheadline, secondary, one line, the -6 top
        // padding, softAppear). EffortSliderJewelKnobTests.An_accessibility_step_springs_the_knob_to_its_stop tests the
        // adjustable action (EffortSlider.swift:822-830, PR-COMPOSER-126) and never shows a caption, yet carries
        // Req PR-COMPOSER-116: req_verify credits the caption's requirement with a test that cannot fail on it.
        var credited = Traceability.TestMethods(typeof(EffortSliderJewelReviewR4Tests).Assembly)
            .Where(m => m.DeclaringType!.Name.StartsWith("EffortSliderJewel", StringComparison.Ordinal))
            .Where(m => Traceability.Traits(m).Contains(("Req", "PR-COMPOSER-116")))
            .Select(m => m.DeclaringType!.Name + "." + m.Name).ToList();
        Assert.True(credited.Count == 0, "credited to PR-COMPOSER-116 (the pair caption) without testing it: " + string.Join(", ", credited));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the knob's tests run in the unit's suites, every one of them filtered FullyQualifiedName!~Native
    public void REVIEW_every_headless_jewel_test_runs_under_the_suites_filter()
    {
        // dotnet test's ~ is a case-insensitive contains: on cb4310571 the filter
        // 'FullyQualifiedName~The_native_bindings_match&FullyQualifiedName!~Native' matches no test. So every suite the
        // unit's reports count (EffortSlider|Jewel 327, the warm-up classes, Views.Composer), each filtered !~Native,
        // leaves out EffortSliderJewelOrbTests.The_native_bindings_match_skiasharps_own, the one guard of JewelNative's
        // DllImport signatures against SkiaSharp's own (a wrong one is a crash or a corrupt call on the render thread),
        // as the build test was before 40f38553f renamed it; whether it passes is not known from any of them.
        var unrun = Traceability.TestMethods(typeof(EffortSliderJewelReviewR4Tests).Assembly)
            .Where(m => m.DeclaringType!.Name.StartsWith("EffortSliderJewel", StringComparison.Ordinal))
            .Select(m => m.DeclaringType!.FullName + "." + m.Name)
            .Where(name => name.Contains("Native", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(unrun.Count == 0, "headless tests the suites' !~Native filter never runs: " + string.Join(", ", unrun));
    }
}
