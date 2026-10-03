using System.Runtime.CompilerServices;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 8 on 20f64c18b): each test states what the unit's tests must do and
// fails on the head reviewed.
public sealed class EffortSliderJewelReviewR8Tests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test that carries a Req trait proves every clause of that record
    public void REVIEW_no_test_credits_PR_COMPOSER_131_whose_knob_glow_OD_0090_removed()
    {
        // PR-COMPOSER-131, "The knob's glow, the stop colour and the light-fill test all follow the pair when there is one"
        // (EffortSlider.swift:1000-1018 TrackLook.glow, stopColor, isLightFill, spark). The source draws look.glow only as the
        // knob's blurred glow disc, :781-786, part of :777-792, which OD-0090 supersedes ("supersedes the source's plain
        // Liquid Glass lens on the effort slider (EffortSlider.swift 777-792)"; ED-252: "the glow disc and the Liquid Glass
        // lens: replaced by the Jewel orb"). The unit's commit efb32a9df removed the port's glow ellipse and its
        // glow.Fill = look.Glow; nothing in the port draws TrackLook.Glow now. EffortSliderTests
        // TrackLook_without_a_pair_follows_its_own_kind and TrackLook_with_a_pair_follows_the_pair_instead_of_its_kind still
        // carry Req PR-COMPOSER-131 and assert TrackLook.Glow: a value no longer on screen, so they cannot prove that the
        // knob's glow follows the pair. The same class as rounds 6 and 7 (PR-COMPOSER-123, -125, UI-COMPOSER-035; -117,
        // UI-COMPOSER-036, -126): the stop colour and light-fill clauses are still live, so the tests keep their assertions
        // under Decision ED-252 with the source lines, and the lead amends PR-COMPOSER-131 for OD-0090 at merge.
        var slider = File.ReadAllText(Path.Combine(Root(), "src", "Marrow.App", "Views", "Composer", "EffortSlider.cs"));
        Assert.DoesNotContain("look.Glow", slider, StringComparison.Ordinal); // the premise: the glow disc and its look.Glow fill are gone
        Assert.DoesNotContain("Look.Glow", slider, StringComparison.Ordinal);
        var credited = Traceability.TestMethods(typeof(EffortSliderJewelReviewR8Tests).Assembly)
            .SelectMany(m => Traceability.Traits(m).Where(t => t.Name == "Req" && t.Value == "PR-COMPOSER-131")
                .Select(t => $"{m.DeclaringType!.Name}.{m.Name}"))
            .ToList();
        Assert.True(credited.Count == 0, "credited with PR-COMPOSER-131 although OD-0090 removed the knob's glow it states: " + string.Join(", ", credited));
    }

    private static string Root([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".."));
}
