using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// OD-0076 (porting/owner_decisions.md:712-719): "a card ... that changes size eases to the new size ... with no single-frame jump", "the system's reduce-motion
// setting stills them". ENGINEERING DECISION ED-261 T1: when the check row's sentence swaps its words, the box it sits in (SentenceHeightGlide)
// eases its height from the height it has to the height the new words take over the swap's 0.18 s on the ease of the pill's :671 (AboutUpdateViews.swift:671),
// so the row and the card around it ease with it; under Reduce Motion the height cuts. At the settings window's default size (SettingsView.cs:170 Width = 780,
// :122; the 212 pt sidebar, :178; 24 pt each side of the page stack, :249) the card is 520 pt wide and the resting sentence takes two lines where "Asking GitLab
// for the latest release…" takes one, so the swap changes the card's height by one 16 pt line. The card's step-by-step height is read at every 16 ms frame;
// the curve it is held to is Animation.EaseInOut(0.18) itself, the one the buttons beside it use.
// A swap that meets a change which leaves the words as they are goes on to them (it is not cancelled with the old words at full opacity).
// The line is the subheadline text style's 16-point box (ED-290 (1), SETTLED 2026-10-04): 1-settings-tour.mov S 212.0 to S 213.0, the Software update row
// (AboutUpdateViews.swift:826-844) goes from 648..721 = 74 to 648..705 = 58 when its detail goes from two lines to one, so one line is 16 (it was pinned
// to Inter's own 13.3125 before the line boxes were settled).
[Collection("Headless")]
public sealed class AboutUpdateViewsSentenceGlideTests(HeadlessSession headless)
{
    private const string Rest = "nth checks GitHub for new versions a few times a day. Not checked yet.";
    private const string Asking = "Asking GitHub for the latest release…";
    private const double Line = 16; // one subheadline line (ED-290 (1); S 212.0 to S 213.0)

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    // The detail sentence's drawn texts (the box's invisible measuring text is not drawn).
    private static TextBlock[] Drawn(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == text && !(t.GetVisualParent() is SentenceHeightGlide box && ReferenceEquals(t, box.Probe))).ToArray();

    private static double Eased(double from, double to, double milliseconds) => from + (to - from) * Animation.EaseInOut(0.18).Progress(milliseconds / 1000);

    [Theory]
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 the detail sentence and OD-0076 card size change
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Req", "UI-SETTINGS-071")] // :826-844 the row's change from Check now to Checking
    [Trait("Rule", "R-TEST-4")] // partial: only that the card's height eases over the swap on the pill's curve (or cuts); the sentences are AboutUpdateViewsProofRowsTests, the fades AboutUpdateViewsCrossfadeTests
    [InlineData(false)]
    [InlineData(true)]
    public void The_card_height_eases_over_the_swap_on_the_ease_of_the_pill_each_way_and_cuts_under_Reduce_Motion(bool reduced) => headless.OnUiThread(() =>
    {
        using var rig = new AboutCheckRowRig(reduced);
        Assert.Single(Drawn(rig.Row, Rest));
        var tall = rig.Height; Assert.True(tall > 0);

        rig.Change(nameof(UpdateChecker.IsChecking), true); // the sentence goes from two lines to one
        if (reduced)
        {
            // :671 `nil`: a cut. No frame has run: the height is already the one-line height, with no measuring text left.
            Assert.Equal(tall - Line, rig.Height, 0.5); Assert.Null(rig.Box.Probe); Assert.False(rig.Box.ClipToBounds);
            Assert.Single(Drawn(rig.Row, Asking)); Assert.Equal(1, rig.Sentence.Opacity);
            rig.Change(nameof(UpdateChecker.IsChecking), false);
            Assert.Equal(tall, rig.Height, 0.5);
            return;
        }
        var short1 = tall - Line; // one line less (the measured step of the two sentences at this width)
        Assert.Equal(tall, rig.Height, 0.01); // the swap has just begun: the height is where it was
        var gliding = rig.Box.Probe; Assert.NotNull(gliding);
        Assert.Equal(Asking, gliding!.Text); Assert.Equal(0, gliding.Opacity); Assert.False(gliding.IsHitTestVisible); // the measuring text is never seen nor hit
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(gliding)); Assert.True(rig.Box.ClipToBounds);
        var heights = new List<double> { rig.Height };
        for (var ms = 16; ms <= 176; ms += 16) { rig.Step(16); heights.Add(rig.Height); Assert.Equal(Eased(tall, short1, ms), rig.Height, 0.5); }
        rig.Step(4); Assert.Equal(short1, rig.Height, 0.05); // the whole 0.18 s
        Assert.True(heights.Zip(heights.Skip(1), (a, b) => b <= a + 1e-9).All(x => x), $"the height only falls: {string.Join(", ", heights)}");
        Assert.True(heights.Zip(heights.Skip(1), (a, b) => a - b).Max() < Line * 0.25, $"no frame steps a quarter of the line: {string.Join(", ", heights)}");
        Assert.Null(rig.Box.Probe); Assert.False(rig.Box.ClipToBounds); Assert.Single(rig.Box.Children); Assert.Empty(Drawn(rig.Row, Rest));

        rig.Change(nameof(UpdateChecker.IsChecking), false); // and back: one line to two, the card grows over the swap
        var grown = new List<double> { rig.Height };
        for (var ms = 16; ms <= 176; ms += 16) { rig.Step(16); grown.Add(rig.Height); Assert.Equal(Eased(short1, tall, ms), rig.Height, 0.5); }
        rig.Step(4); Assert.Equal(tall, rig.Height, 0.05);
        Assert.True(grown.Zip(grown.Skip(1), (a, b) => b >= a - 1e-9).All(x => x), $"the height only grows: {string.Join(", ", grown)}");
        Assert.Null(rig.Box.Probe); Assert.Single(Drawn(rig.Row, Rest)); Assert.Equal(1, rig.Sentence.Opacity); // the words that came in are whole
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 + OD-0076 card size change
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Rule", "R-TEST-4")] // partial: only that a swap turned back part way eases the height on from where it is, with no jump
    public void A_swap_turned_back_part_way_eases_the_height_on_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = new AboutCheckRowRig();
        var tall = rig.Height;
        rig.Change(nameof(UpdateChecker.IsChecking), true);
        var heights = new List<double> { rig.Height };
        for (var i = 0; i < 4; i++) { rig.Step(16); heights.Add(rig.Height); } // 64 ms in: the height is part way down
        var turned = rig.Height; Assert.True(turned < tall - 1 && turned > tall - Line + 1, $"part way: {turned} of {tall}");
        rig.Change(nameof(UpdateChecker.IsChecking), false);
        Assert.Equal(turned, rig.Height, 0.01); heights.Add(rig.Height); // turned back from where it was, not from either end
        for (var i = 0; i < 14; i++) { rig.Step(16); heights.Add(rig.Height); }
        var steps = heights.Zip(heights.Skip(1), (a, b) => Math.Abs(b - a)).ToArray();
        Assert.True(steps.Max() < Line * 0.25, $"no step of a quarter of a line: {string.Join(", ", heights)}");
        Assert.Equal(tall, rig.Height, 0.05); Assert.Null(rig.Box.Probe);
    });

    [Theory]
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 the detail sentence
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Rule", "R-TEST-4")] // partial: only that a change which leaves the sentence as it is does not cancel the swap that is running (the old words never come back to full opacity)
    [InlineData(20)]  // in the first half: the old words are fading out
    [InlineData(70)]  // just before the middle
    [InlineData(120)] // in the second half: the new words are fading in
    public void A_change_that_leaves_the_sentence_as_it_is_lets_the_running_swap_go_on(int milliseconds) => headless.OnUiThread(() =>
    {
        using var rig = new AboutCheckRowRig();
        rig.Change(nameof(UpdateChecker.IsChecking), true); // the sentence "Asking GitLab..." is on its way in
        rig.Step(milliseconds);
        var before = rig.Sentence.Opacity; var wordsBefore = rig.Sentence.Text;
        // The error does not show while a check runs (:836 the checking sentence has precedence), so the sentence is the same one: the check row hears of it and keeps its words.
        rig.Change(nameof(UpdateChecker.LastError), "The request timed out.");
        Assert.Equal(wordsBefore, rig.Sentence.Text); Assert.Equal(before, rig.Sentence.Opacity, 0.001); // it did not cut to full opacity
        Assert.True(before < 0.999, $"mid-swap the words are not whole ({before})");
        rig.Step(200);
        Assert.Equal(Asking, rig.Sentence.Text); Assert.Equal(1, rig.Sentence.Opacity); Assert.Single(Drawn(rig.Row, Asking)); Assert.Empty(Drawn(rig.Row, Rest)); // the swap finished
        Assert.Null(rig.Box.Probe);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 the detail sentence
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Rule", "R-TEST-4")] // partial: only that Reduce Motion, switched on while a swap runs, finishes that swap at once when the sentence is changed to the same words
    public void Reduce_Motion_switched_on_mid_swap_finishes_the_running_swap_at_the_next_change() => headless.OnUiThread(() =>
    {
        using var rig = new AboutCheckRowRig();
        var tall = rig.Height;
        rig.Change(nameof(UpdateChecker.IsChecking), true); rig.Step(20);
        Assert.Equal(Rest, rig.Sentence.Text); Assert.True(rig.Sentence.Opacity < 0.999);
        EnvironmentValues.SetAccessibilityReduceMotion(rig.Row, true);
        rig.Change(nameof(UpdateChecker.LastError), "The request timed out."); // the same words
        Assert.Equal(Asking, rig.Sentence.Text); Assert.Equal(1, rig.Sentence.Opacity); Assert.Null(rig.Box.Probe);
        Assert.Equal(tall - Line, rig.Height, 0.5); // cut to the one-line height at once
        rig.Step(300); Assert.Equal(Asking, rig.Sentence.Text); Assert.Equal(1, rig.Sentence.Opacity); Assert.Equal(tall - Line, rig.Height, 0.5);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-240")] // :836-844 the detail sentence
    [Trait("Req", "PR-UPDATE-076")] // :822-845
    [Trait("Rule", "R-TEST-4")] // partial: only that Reduce Motion, switched on while a swap runs, cuts the next different sentence in with the height cut too
    public void Reduce_Motion_switched_on_mid_swap_cuts_the_next_sentence_in_with_its_height() => headless.OnUiThread(() =>
    {
        using var rig = new AboutCheckRowRig();
        var tall = rig.Height;
        rig.Change(nameof(UpdateChecker.IsChecking), true); rig.Step(64);
        Assert.NotNull(rig.Box.Probe); Assert.InRange(rig.Height, tall - Line + 1, tall - 1); // a swap is running, the height part way
        EnvironmentValues.SetAccessibilityReduceMotion(rig.Row, true);
        rig.Change(nameof(UpdateChecker.IsChecking), false); // a different sentence: cut
        Assert.Equal(Rest, rig.Sentence.Text); Assert.Equal(1, rig.Sentence.Opacity);
        Assert.Null(rig.Box.Probe); Assert.False(rig.Box.ClipToBounds); Assert.Equal(tall, rig.Height, 0.05);
        rig.Step(300); Assert.Equal(tall, rig.Height, 0.05); Assert.Equal(1, rig.Sentence.Opacity);
    });
}
