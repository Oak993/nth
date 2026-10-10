using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// OD-0076 (porting/owner_decisions.md:712-719, "content that swaps crossfades ... every transition runs on the frame clock with no single-frame jump, and the
// system's reduce-motion setting stills them") and AboutUpdateViews.swift:671 `.animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: isReady)`
// over the `if isReady {...} else {...}` branches (:643-660) whose removal and insertion take SwiftUI's default opacity transition under that animation.
// ENGINEERING DECISION ED-261 T1-T3: the Check now button and its sentence, the pill's ready and standing faces and the install slider all swap
// over 0.18 s on that ease (the pill's own), on the frame clock, the old content fading out where it stood while the new fades in, the slot easing to the new width,
// the content that is leaving taking no input; under Reduce Motion they cut. Each test reads the screen at a quarter, the middle and the end of the swap.
[Collection("Headless")]
public sealed class AboutUpdateViewsCrossfadeTests(HeadlessSession headless)
{
    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static double EffectiveOpacity(Visual visual)
    {
        var opacity = 1.0;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent()) opacity *= v.Opacity;
        return opacity;
    }

    private static Avalonia.Controls.Window Show(Control view)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = 600, Height = 200, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Whether a pointer can reach the control: the control and every ancestor take hits (the slot turns hit testing off on the content that is leaving).
    private static bool Hittable(Visual control)
    {
        for (Visual? v = control; v is not null; v = v.GetVisualParent()) if (v is InputElement { IsHitTestVisible: false }) return false;
        return true;
    }

    // The texts that are drawn: the sentence box's invisible measuring text (it carries the new words while the height eases) is not one.
    private static Control[] TextHosts(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == text && !(t.GetVisualParent() is SentenceHeightGlide box && ReferenceEquals(t, box.Probe))).Cast<Control>().ToArray();

    private static double Shown(Control root, string text) => TextHosts(root, text).Select(EffectiveOpacity).DefaultIfEmpty(0).Max();

    // The button of the check row that carries this label (its own opacity chain).
    private static Button ButtonLabelled(Control root, string label) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));

    // ---- T1: the Check now row ----

    [Theory]
    [Trait("Req", "UI-SETTINGS-071")] // AboutUpdateViews.swift:826-833 + OD-0076: Check now to Checking... and back swap by a crossfade (the other clauses: AboutUpdateViewsRecordTests, ProofRowsTests, CheckNowLookTests)
    [Trait("Req", "UI-UPDATE-022")] // same: label and disabled look change together, over 0.18 s
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 the clause "becomes Checking... and is disabled while a check runs" and the detail swap
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Rule", "R-TEST-4")] // partial: the crossfade clause added by OD-0076; the sentences, precedence and look are proven in the tests named above
    [InlineData(false)]
    [InlineData(true)]
    public void The_check_row_crossfades_its_button_and_its_sentence_each_way_and_cuts_under_Reduce_Motion(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        var row = new AboutUpdateCheckRow(); EnvironmentValues.SetAccessibilityReduceMotion(row, reduced); stage.Window = Show(row);
        var slot = Assert.Single(row.GetVisualDescendants().OfType<CrossfadeSlot>());
        const string rest = "Marrow checks GitHub for new versions a few times a day. Not checked yet.";
        const string asking = "Asking GitHub for the latest release…";
        var restWidth = slot.Bounds.Width; Assert.True(restWidth > 0);
        Assert.Equal(1, Shown(row, rest));

        Set(stage.Checker, nameof(UpdateChecker.IsChecking), true); Dispatcher.UIThread.RunJobs();
        if (reduced)
        {
            // :671 `nil`: a cut, the new state at once.
            Assert.Empty(TextHosts(row, "Check now")); Assert.Equal(1, Shown(row, "Checking…")); Assert.Equal(1, Shown(row, asking)); Assert.Empty(TextHosts(row, rest));
            return;
        }
        stage.Step(45); // a quarter of 0.18 s: the old button still there, the new just coming
        var old = ButtonLabelled(row, "Check now"); var checking = ButtonLabelled(row, "Checking…");
        Assert.InRange(EffectiveOpacity(old), 0.80, 0.995); Assert.InRange(EffectiveOpacity(checking), 0.005, 0.20);
        Assert.False(Hittable(old)); // what is leaving takes no input
        Assert.InRange(Shown(row, rest), 0.05, 0.95); Assert.Empty(TextHosts(row, asking)); // the sentence's old words fade out first
        stage.Step(45); // the middle: ease in out is 0.5 each
        Assert.InRange(EffectiveOpacity(old), 0.40, 0.60); Assert.InRange(EffectiveOpacity(checking), 0.40, 0.60);
        var checkingWidth = slot.Live!.DesiredSize.Width;
        Assert.Equal((restWidth + checkingWidth) / 2, slot.Bounds.Width, 0.75); // the slot eases from the old button's width to the new one's: halfway at the middle of the ease
        Assert.Empty(TextHosts(row, rest)); Assert.Single(TextHosts(row, asking)); Assert.InRange(Shown(row, asking), 0.0, 0.2); // the new words come in over the second half
        stage.Step(100); // done
        Assert.Single(row.GetVisualDescendants().OfType<Button>()); Assert.False(checking.IsEnabled); Assert.Equal(1, EffectiveOpacity(checking));
        Assert.Equal(1, Shown(row, asking)); Assert.Empty(TextHosts(row, rest)); Assert.Empty(TextHosts(row, "Check now"));
        var checkingSlot = slot.Bounds.Width;
        Assert.Equal(slot.Live!.DesiredSize.Width, checkingSlot, 0.5); // the slot has eased to the new button's width

        Set(stage.Checker, nameof(UpdateChecker.IsChecking), false); Dispatcher.UIThread.RunJobs(); // and back
        stage.Step(90);
        var leaving = ButtonLabelled(row, "Checking…"); var back = ButtonLabelled(row, "Check now");
        Assert.InRange(EffectiveOpacity(leaving), 0.40, 0.60); Assert.InRange(EffectiveOpacity(back), 0.40, 0.60); Assert.False(Hittable(leaving)); Assert.True(back.IsEnabled);
        stage.Step(100);
        Assert.Single(row.GetVisualDescendants().OfType<Button>()); Assert.Equal(1, EffectiveOpacity(back)); Assert.Empty(TextHosts(row, "Checking…"));
        Assert.Empty(TextHosts(row, asking));
    });

    // ---- T2: the pill's ready and standing faces ----

    [Theory]
    [Trait("Req", "UI-UPDATE-005")] // :671 .animation(.easeInOut(0.18), value: isReady) over the branches :643-660: the symbol and title crossfade (the colours: AboutUpdateViewsProofInputTests)
    [Trait("Req", "PR-UPDATE-073")] // :638-675
    [Trait("Req", "PR-SETTINGS-242")] // :638-682 the four states and the transition between them
    [Trait("Req", "UI-SETTINGS-056")] // :625-682 the pill's states
    [Trait("Rule", "R-TEST-4")] // partial: the content crossfade of the face change; the colour ease is AboutUpdateViewsProofInputTests, hit testing and strings the tests named there
    [InlineData(false)]
    [InlineData(true)]
    public void The_ready_and_standing_faces_crossfade_each_way_over_0_18_seconds_and_cut_under_Reduce_Motion(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        AboutVersionPill pill = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); EnvironmentValues.SetAccessibilityReduceMotion(pill, reduced); stage.Window = Show(pill);
            Assert.Equal(1, Shown(pill, "Not checked yet"));
            Set(stage.Checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            if (reduced)
            {
                Assert.Empty(TextHosts(pill, "Not checked yet")); Assert.Equal(1, Shown(pill, "Update & restart")); return;
            }
            var slot = Assert.Single(pill.GetVisualDescendants().OfType<CrossfadeSlot>());
            var standingWidth = slot.Bounds.Width;
            stage.Step(45);
            Assert.InRange(Shown(pill, "Not checked yet"), 0.80, 0.995); Assert.InRange(Shown(pill, "Update & restart"), 0.005, 0.20);
            Assert.Equal(2, pill.GetVisualDescendants().OfType<Button>().Count());
            stage.Step(45); // the middle
            Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60); Assert.InRange(Shown(pill, "Update & restart"), 0.40, 0.60);
            var buttons = pill.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.False(Hittable(buttons[0])); Assert.True(Hittable(buttons[1])); // the standing that leaves is inert, the ready button that comes in is the one that works
            var readyWidth = slot.Live!.DesiredSize.Width;
            Assert.Equal((standingWidth + readyWidth) / 2, slot.Bounds.Width, 0.75); // the pill eases between them
            stage.Step(100);
            Assert.Single(pill.GetVisualDescendants().OfType<Button>()); Assert.Equal(1, Shown(pill, "Update & restart")); Assert.Empty(TextHosts(pill, "Not checked yet"));
            Assert.Equal(slot.Live!.DesiredSize.Width, slot.Bounds.Width, 0.5);

            Set(stage.Checker, nameof(UpdateChecker.Update), null); Dispatcher.UIThread.RunJobs(); // and back
            stage.Step(90);
            Assert.InRange(Shown(pill, "Update & restart"), 0.40, 0.60); Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60);
            stage.Step(100);
            Assert.Single(pill.GetVisualDescendants().OfType<Button>()); Assert.Equal(1, Shown(pill, "Not checked yet")); Assert.Empty(TextHosts(pill, "Update & restart"));
            Assert.Equal(standingWidth, slot.Bounds.Width, 0.5);
        });
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-005")] // :638-640,673-675 a click on the ready button installs; the face that has left (or is leaving) does not
    [Trait("Rule", "R-TEST-4")] // partial: only that a leaving ready face is inert
    public void A_ready_face_that_is_fading_out_cannot_be_pressed() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        AboutVersionPill pill = null!;
        runtime.OnMain(() =>
        {
            Set(stage.Checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
            pill = new AboutVersionPill(); stage.Window = Show(pill);
            Assert.True(Hittable(Assert.Single(pill.GetVisualDescendants().OfType<Button>())));
            Set(stage.Checker, nameof(UpdateChecker.Update), null);
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); stage.Step(45);
            var buttons = pill.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(2, buttons.Length);
            Assert.False(Hittable(buttons[0])); Assert.False(Hittable(buttons[1])); // the leaving ready face and the standing, which is never a button
        });
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-005")] // :671 the face change eases; a rebuild of the face that looks the same (a check ends meanwhile) must not jump the running swap to its end
    [Trait("Rule", "R-TEST-4")] // partial: only that a swap in flight is not cut by a same-looking rebuild
    public void A_rebuild_that_looks_the_same_does_not_cut_a_swap_that_is_running() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        AboutVersionPill pill = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); stage.Window = Show(pill);
            Set(stage.Checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs(); stage.Step(45);
            Set(stage.Checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now); Dispatcher.UIThread.RunJobs(); // still the ready face, rebuilt while the swap runs
            stage.Step(16);
            Assert.InRange(Shown(pill, "Update & restart"), 0.0, 0.5); // not at once at full opacity
            Assert.True(Shown(pill, "Not checked yet") > 0.3); // the standing is still on its way out
            stage.Step(300);
            Assert.Single(pill.GetVisualDescendants().OfType<Button>()); Assert.Equal(1, Shown(pill, "Update & restart"));
        });
    });

    // Review (2026-10-04, second round): the most common change of the pill is one standing into another when a check ends (:617-621
    // standing: Not checked yet -> Check failed -> Up to date); its symbol and title swap like the ready face (OD-0076 content that swaps
    // crossfades). Killed the mutation that gives every standing one look (the swap would cut).
    [Theory]
    [Trait("Req", "UI-SETTINGS-056")] // :625-682 the pill's three standings replace each other by a crossfade
    [Trait("Req", "PR-SETTINGS-242")] // :586-621 the standings and the transition between them
    [Trait("Rule", "R-TEST-4")] // partial: only the standing-to-standing crossfade
    [InlineData(false)]
    [InlineData(true)]
    public void One_standing_crossfades_into_another_when_a_check_ends_and_cuts_under_Reduce_Motion(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        AboutVersionPill pill = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); EnvironmentValues.SetAccessibilityReduceMotion(pill, reduced); stage.Window = Show(pill);
            Assert.Equal(1, Shown(pill, "Not checked yet"));
            Set(stage.Checker, nameof(UpdateChecker.LastError), "No update feed is set."); Set(stage.Checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            if (reduced)
            {
                Assert.Empty(TextHosts(pill, "Not checked yet")); Assert.Equal(1, Shown(pill, "Check failed")); return;
            }
            stage.Step(45);
            Assert.InRange(Shown(pill, "Not checked yet"), 0.80, 0.995); Assert.InRange(Shown(pill, "Check failed"), 0.005, 0.20);
            stage.Step(45);
            Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60); Assert.InRange(Shown(pill, "Check failed"), 0.40, 0.60);
            stage.Step(100);
            Assert.Empty(TextHosts(pill, "Not checked yet")); Assert.Equal(1, Shown(pill, "Check failed"));
            Set(stage.Checker, nameof(UpdateChecker.LastError), null); Dispatcher.UIThread.RunJobs(); // the next check succeeds
            stage.Step(90);
            Assert.InRange(Shown(pill, "Check failed"), 0.40, 0.60); Assert.InRange(Shown(pill, "Up to date"), 0.40, 0.60);
            stage.Step(100);
            Assert.Empty(TextHosts(pill, "Check failed")); Assert.Equal(1, Shown(pill, "Up to date"));
        });
    });

    // ---- T3: the pill and the install slider ----

    [Theory]
    [Trait("Req", "UI-SETTINGS-056")] // :625-636 the slider in the capsule and :638 the face: the pill's states swap by a crossfade
    [Trait("Req", "PR-SETTINGS-242")] // :625-682
    [Trait("Rule", "R-TEST-4")] // partial: the slider/face crossfade added by OD-0076; the slider geometry and phases are proven in AboutUpdateViewsProofGeometryTests
    [InlineData(false)]
    [InlineData(true)]
    public void The_install_slider_and_the_face_crossfade_when_an_install_starts_fails_and_completes(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var stage = new AboutStage(runtime);
        AboutVersionPill pill = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); EnvironmentValues.SetAccessibilityReduceMotion(pill, reduced); stage.Window = Show(pill);
            stage.Progress.Begin();
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            double Slider() => pill.GetVisualDescendants().OfType<UpdateProgressSlider>().Select(EffectiveOpacity).DefaultIfEmpty(0).Max();
            if (reduced)
            {
                Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Empty(TextHosts(pill, "Not checked yet"));
                stage.Progress.Fail(); Dispatcher.UIThread.RunJobs();
                Assert.Empty(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Equal(1, Shown(pill, "Not checked yet"));
                return;
            }
            // install starts: the face fades out, the slider fades in
            var standingWidth = pill.Bounds.Width; Assert.NotEqual(168, standingWidth, 4); // the standing is not the install capsule's width (nth motion pack step 10: 168), so the width has somewhere to ease to
            stage.Step(45); Assert.InRange(Shown(pill, "Not checked yet"), 0.80, 0.995); Assert.InRange(Slider(), 0.005, 0.20);
            stage.Step(45); Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60); Assert.InRange(Slider(), 0.40, 0.60);
            Assert.Equal((standingWidth + 168) / 2, pill.Bounds.Width, 0.75); // the capsule eases from one width to the other
            stage.Step(100); Assert.Empty(TextHosts(pill, "Not checked yet")); Assert.Equal(1, Slider()); Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>());
            Assert.Equal(168, pill.Bounds.Width, 0.5);
            // it fails: the slider fades out, the face (Failed shows the standing) fades in
            stage.Progress.Fail(); Dispatcher.UIThread.RunJobs();
            stage.Step(90); Assert.InRange(Slider(), 0.40, 0.60); Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60);
            stage.Step(100); Assert.Empty(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Equal(1, Shown(pill, "Not checked yet"));
            // an install celebrates and completes: the slider fades out into the standing
            stage.Progress.ArmCelebration(); Assert.True(stage.Progress.ConsumePendingCelebration()); Dispatcher.UIThread.RunJobs(); stage.Step(200);
            Assert.Equal(1, Slider());
            stage.Progress.CompleteCelebration(); Dispatcher.UIThread.RunJobs();
            stage.Step(45); Assert.InRange(Slider(), 0.80, 0.995); Assert.InRange(Shown(pill, "Not checked yet"), 0.005, 0.20);
            stage.Step(45); Assert.InRange(Slider(), 0.40, 0.60); Assert.InRange(Shown(pill, "Not checked yet"), 0.40, 0.60);
            stage.Step(100); Assert.Empty(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()); Assert.Equal(1, Shown(pill, "Not checked yet"));
        });
    });
}
