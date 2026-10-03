using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (final review of unit/effort-title-fidelity, round 4, 2026-10-03): ED-238 final round 2 (4) claims PR-COMPOSER-111's
// plate "clear at rest, Chrome.overlay(0.08) over Chrome.hover and back", and EffortTitleButtonTests reads the way back only
// as clear 120 ms after the pointer has left: with the plate cleared at once when the pointer leaves (mutant F4, the fill set
// with no animation on the way out), every App Views.Composer test passes (work/claude-verify/resume-2026-10-03/
// effort-rev-r4.mutants*.txt). EffortSlider.swift:592-594 `.onHover { hovering in withAnimation(Chrome.hover) {
// isTitleHovering = hovering } }` animates both ways (Chrome.hover is Chrome.swift's easeOut(duration: 0.1)); OD-0076: no
// change by a cut.
[Collection("Headless")]
public sealed class EffortTitleHoverOutReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-238")] // a clause of PR-COMPOSER-111 (EffortSlider.swift:592-594, the plate over Chrome.hover, both ways)
    public void REVIEW_The_title_plate_fades_out_along_Chrome_hover_when_the_pointer_leaves() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        void Advance(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms += 5)
            {
                clock.Advance(TimeSpan.FromMilliseconds(5));
                frames.Advance(TimeSpan.FromMilliseconds(5));
                Dispatcher.UIThread.RunJobs();
            }
        }
        string? chosen = null;
        var card = new EffortSliderCard("Test model", null, new ValueList<string>(["low", "high"]), "high", false,
            new Binding<string?>(() => chosen, value => chosen = value), new Binding<bool>(() => false, _ => { }), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = Assert.Single(card.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Choose a model"));
            var plate = Assert.IsType<Border>(button.Content);
            var full = Chrome.Overlay(0.08).Resolve(plate.ActualThemeVariant);
            var centre = new Rect(plate.TranslatePoint(default, window)!.Value, plate.Bounds.Size).Center;
            window.MouseMove(centre, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Advance(200);
            Assert.Equal(full, Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color);
            window.MouseMove(new Point(2, 218), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var seen = new List<string>();
            var elapsed = 0;
            foreach (var at in new[] { 20, 40, 70 })
            {
                Advance(at - elapsed);
                elapsed = at;
                var alpha = Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color.A;
                var expected = full.A * (1 - Chrome.Hover.Progress(at / 1000.0));
                seen.Add($"{at} ms: {alpha} (Chrome.hover {expected:F1} of {full.A})");
                Assert.True(Math.Abs(alpha - expected) <= 1.5, $"the plate does not fade out along Chrome.hover: {string.Join("; ", seen)}");
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
