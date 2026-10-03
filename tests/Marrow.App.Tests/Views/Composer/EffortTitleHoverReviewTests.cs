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

// REVIEW (final review of unit/effort-title-fidelity, round 3, 2026-10-03): EffortTitleButtonTests claims PR-COMPOSER-111's
// hover clause, the plate fading in "over Chrome.hover (ease out, 0.1 s)", but reads the plate only as somewhere between
// clear and full at 40 ms and full at 120 ms: with the plate faded over Animation.EaseOut(0.06) instead (mutant R-hovercurve),
// every App Views.Composer test passes. EffortSlider.swift:592-594 `.onHover { hovering in withAnimation(Chrome.hover) {
// isTitleHovering = hovering } }`; Chrome.hover is Chrome.swift's easeOut(duration: 0.1).
[Collection("Headless")]
public sealed class EffortTitleHoverReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-238")] // a clause of PR-COMPOSER-111 (EffortSlider.swift:592-594, the plate over Chrome.hover)
    public void REVIEW_The_title_plate_fades_in_along_Chrome_hover() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
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
            var seen = new List<string>();
            var elapsed = 0;
            foreach (var at in new[] { 20, 40, 70 })
            {
                for (; elapsed < at; elapsed += 5)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(5));
                    frames.Advance(TimeSpan.FromMilliseconds(5));
                    Dispatcher.UIThread.RunJobs();
                }
                var alpha = Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color.A;
                var expected = full.A * Chrome.Hover.Progress(at / 1000.0);
                seen.Add($"{at} ms: {alpha} (Chrome.hover {expected:F1} of {full.A})");
                Assert.True(Math.Abs(alpha - expected) <= 1.5, $"the plate does not fade in along Chrome.hover: {string.Join("; ", seen)}");
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
