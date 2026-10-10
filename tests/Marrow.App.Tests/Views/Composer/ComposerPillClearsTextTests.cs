using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// OD-0130 (owner finding F5) and OD-0132: while the voice pill opens and closes inside the composer no composer text shows under or through it. The text column's right edge is held in
// before the pill in step with its width (V-12.6, 0.42 s ease) and the column's last 24 points fade to clear (ComposerView.FadeBesidePill); the field note (the privacy line) is part
// of that column, so it ends before the pill too, trimmed rather than run on.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-gate-fixes")]
public sealed class ComposerPillClearsTextTests(HeadlessSession headless)
{
    private static void Tick() { for (var k = 0; k < 3; k++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }

    private static void Prepare(AvaloniaWindow window, double scale, bool dark)
    {
        window.Height = 300;
        window.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(48, 56, 70));
        window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        ((Border)window.Content!).Margin = new Thickness(0, 0, 0, 64);
        window.SetRenderScaling(scale);
    }

    /// <summary>The pixels of the pill's rectangle that the text column draws: the frame with the column and the frame without it, compared inside the rectangle.</summary>
    private static (int Pixels, string Where) ColumnInk(ComposerView view, AvaloniaWindow window, Rect pill)
    {
        var column = LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single();
        Tick();
        var with = DictationProbe.Frame(window);
        var opacity = column.Opacity;
        column.Opacity = 0;
        Tick();
        var without = DictationProbe.Frame(window);
        column.Opacity = opacity;
        Tick();
        var scale = window.RenderScaling;
        var ink = 0;
        var where = "";
        // the pill's own rim (its hairline and the antialiasing of its rounded edge, two device pixels) is not text: it shifts by a pixel with any sibling's layer
        const int Rim = 2;
        for (var y = (int)Math.Ceiling(pill.Top * scale) + Rim; y < (int)Math.Floor(pill.Bottom * scale) - Rim; y++)
        for (var x = (int)Math.Ceiling(pill.Left * scale) + Rim; x < (int)Math.Floor(pill.Right * scale) - Rim; x++)
        {
            var at = y * with.Stride + x * 4;
            if (Enumerable.Range(0, 3).Max(c => Math.Abs(with.Bytes[at + c] - without.Bytes[at + c])) >= 3) { ink++; if (where.Length == 0) where = $"first at ({x}, {y}) of the pill ({pill.Left * scale:F1}..{pill.Right * scale:F1}, {pill.Top * scale:F1}..{pill.Bottom * scale:F1})"; }
        }
        return (ink, where);
    }

    private static void Check(VoiceComposerRig rig, ComposerView view, AvaloniaWindow window, string when, List<string> errors)
    {
        rig.Main.OnMain(() =>
        {
            var pill = Pill(view);
            if (pill.Bounds.Width <= 0 || pill.Opacity <= 0) return;
            var rect = pill.TranslatePoint(default, window) is { } origin ? new Rect(origin, pill.Bounds.Size) : default;
            var column = LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single();
            var right = column.TranslatePoint(new Point(column.Bounds.Width, 0), window)!.Value.X;
            if (right > rect.Left + .01) errors.Add($"{when}: the column ends at {right:F2}, {right - rect.Left:F2} past the pill's left edge {rect.Left:F2}");
            var (ink, where) = ColumnInk(view, window, rect);
            if (ink > 0) errors.Add($"{when}: {ink} px of composer text inside the pill's rectangle, {where}");
        });
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(1, false, false)]
    [InlineData(1.25, true, false)]
    [InlineData(1.5, true, false)]
    [InlineData(2, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, true)]
    public void No_composer_text_shows_under_the_pill_in_any_frame_of_its_opening_and_closing(double scale, bool dark, bool draft) => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig(VoiceOrbSettings.Default);
        var (view, window) = rig.Open(900);
        var errors = new List<string>();
        try
        {
            rig.Main.OnMain(() => Prepare(window, scale, dark));
            rig.PumpWindow(window);
            if (draft) rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = string.Join(' ', Enumerable.Repeat("a draft that runs along the line", 12)) });
            Run(rig, window, .4);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .2);
            rig.Main.OnMain(() => view.Voice!.Activate());
            for (var k = 1; k <= 48; k++)
            {
                rig.Elapse(window, FrameTime);
                Check(rig, view, window, $"opening frame {k}", errors);
            }
            Assert.True(rig.Main.OnMain(() => view.Voice!.Cancel()));
            for (var k = 1; k <= 48; k++)
            {
                rig.Elapse(window, FrameTime);
                Check(rig, view, window, $"closing frame {k}", errors);
            }
        }
        finally { rig.Close(window); }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(12)) + $"{Environment.NewLine}({errors.Count} in all)");
    });
}
