using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:573-574 Text(verbatim: title).contentTransition(.numericText()); :631 the slider writes the effort
// inside withAnimation(.snappy(duration: 0.2)). The morph itself is MorphingTextBlockTests'.
[Collection("Headless")]
public sealed class EffortTitleMorphTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    [Trait("Req", "UI-COMPOSER-031")]
    public void The_title_morphs_when_the_effort_changes_inside_an_animation_and_changes_at_once_outside_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal("High", title.Text);
            Assert.Equal(0.5, title.GrowthAnchor); // :566-567 a centred title: the "M" of Max appears where it settles (video 35.12 s)

            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Max", title.Text);
            Assert.True(title.IsMorphing); // the change came inside the slider's animation

            effort.Value = "low";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Low", title.Text);
            Assert.False(title.IsMorphing); // no transaction: the text simply changes
        }
        finally { window.Close(); }
    });

    // EffortSlider.swift:543-555 titleStyle, :582-583 .animation(.smooth(duration: 0.3), value: isMaxEffort). SOURCE FACT
    // (reference runtime, 2-chat-use.mov 35.10-35.21 s, study effort-title-2026-10-03): when the title changes text and
    // colour together (Extra High -> Max), the arriving letters wear the new colour from their first frame (an orange
    // "M", R/G 1.4 over the glass, beside the pink leaving "High"); only a colour change without a new text fades.
    // Final review round 4 (finding 3): as first written this read only the title's Foreground, the new colour whatever is
    // drawn (mutants C1, every letter in the new colour, and C2, the arriving ones in the old, survived it); it now reads the
    // letters the title draws, by the line each comes from, as the card sets text and colour (EffortSlider.cs).
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574), not the button clauses; re-traited in final round 3
    public void A_title_that_morphs_into_new_letters_draws_them_in_the_new_colour_at_once() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var old = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.True(title.IsMorphing);
            var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground);
            Assert.Equal(EffortBrand.Create(ProviderKind.Claude).TitleColor.Resolve(title.ActualThemeVariant), brush.Color); // :551 brand.title at Max
            Assert.NotEqual(old, brush.Color);
            // The first frame an arriving letter of "Max" is drawn, and 110 ms in (video 35.15 s: an orange "M" beside the
            // pink leaving "High"): every letter of "Max" in the new colour, every leaving letter of "High" in the old.
            var first = -1;
            for (var ms = 1; ms <= 110; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                var drawn = DrawnColours(title);
                if (first < 0 && drawn.ContainsKey("Max")) first = ms;
                if (ms != first && ms != 110) continue;
                var seen = $"{ms} ms: {Show(drawn)}";
                Assert.True(drawn.TryGetValue("Max", out var arriving) && arriving.SetEquals([brush.Color]), $"an arriving letter of \"Max\" is not drawn in the new colour {brush.Color}: {seen}");
                Assert.True(drawn.TryGetValue("High", out var leaving) && leaving.SetEquals([old]), $"a leaving letter of \"High\" is not drawn in the old colour {old}: {seen}");
            }
            Assert.True(first > 0, "no letter of \"Max\" is drawn within 110 ms");
        }
        finally { window.Close(); }
    });

    // The same through a takeover, as the card is dragged back down (video 35.635-35.807 s, Max -> Extra High taken over by
    // Extra High -> High at 35.753 s: the leaving "x" of "Max" still orange beside the pink "Ext"): the leaving letters of the
    // change taken over keep that change's old colour (mutant C3, drawn in the colour of now, and E3c, not drawn at all, were
    // killed by no test of the card's).
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574) through a takeover, with :582-583's colour
    public void The_leaving_letters_of_a_change_taken_over_keep_that_changes_old_colour() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "max" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Advance(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
            }
        }
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var brand = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "xhigh"); // 35.635 s
            Dispatcher.UIThread.RunJobs();
            var plain = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Assert.NotEqual(brand, plain);
            Advance(118);
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "high"); // 35.753 s, the takeover
            Dispatcher.UIThread.RunJobs();
            Advance(34); // 35.787 s
            var drawn = DrawnColours(title);
            var seen = Show(drawn);
            Assert.True(drawn.TryGetValue("Max", out var max) && max.SetEquals([brand]), $"the leaving letters of \"Max\" are not drawn in their old colour {brand}: {seen}");
            Assert.True(drawn.TryGetValue("Extra High", out var extra) && extra.SetEquals([plain]), $"the letters of \"Extra High\" are not drawn in {plain}: {seen}");
            Assert.True(drawn.TryGetValue("High", out var high) && high.SetEquals([plain]), $"the letters of \"High\" are not drawn in {plain}: {seen}");
        }
        finally { window.Close(); }
    });

    // The colours the title draws its letters in now, by the line each glyph run comes from (a change draws every letter as a
    // run of one, carrying its line's text), read from what it pushes into a recording.
    private static Dictionary<string, HashSet<Avalonia.Media.Color>> DrawnColours(MorphingTextBlock title)
    {
        var recording = new Avalonia.Media.DrawingGroup();
        using (var context = recording.Open()) title.Render(context);
        var result = new Dictionary<string, HashSet<Avalonia.Media.Color>>();
        void Walk(Avalonia.Media.Drawing drawing)
        {
            switch (drawing)
            {
                case Avalonia.Media.DrawingGroup group:
                    foreach (var child in group.Children) Walk(child);
                    break;
                case Avalonia.Media.GlyphRunDrawing { GlyphRun: { } run } glyphs:
                    var line = run.Characters.ToString();
                    if (!result.TryGetValue(line, out var set)) result[line] = set = [];
                    set.Add(glyphs.Foreground is Avalonia.Media.ISolidColorBrush solid ? solid.Color : Avalonia.Media.Colors.Transparent);
                    break;
            }
        }
        Walk(recording);
        return result;
    }

    private static string Show(Dictionary<string, HashSet<Avalonia.Media.Color>> drawn) =>
        string.Join("; ", drawn.Select(pair => $"\"{pair.Key}\" {string.Join(",", pair.Value)}"));

    // SOURCE FACT (reference runtime, 2-chat-use.mov 35.12-35.29 s): as "Extra High" narrows to "Max", the leaving "High"
    // is carried right past the chevron and fades there, raised; a plain SwiftUI button does not clip its label.
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574), not the button clauses; re-traited in final round 3
    public void Leaving_letters_carried_past_the_chevron_are_drawn_not_clipped() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "xhigh" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var button = title.GetVisualAncestors().OfType<Avalonia.Controls.Button>().First();
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            for (var ms = 0; ms < 130; ms += 10)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                frames.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
            }
            window.UpdateLayout();
            var box = new Avalonia.Rect(button.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, button.Bounds.Size);
            using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var pixels = frame.Lock();
            var row = new byte[pixels.RowBytes];
            var ink = 0;
            for (var y = (int)box.Top; y < (int)box.Bottom; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                for (var x = (int)Math.Ceiling(box.Right) + 1; x < Math.Min(pixels.Size.Width, (int)box.Right + 40); x++)
                    if (Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2])) > 40) ink++;
            }
            Assert.True(ink > 10, $"letters drawn past the title button's right edge: {ink} pixels");
        }
        finally { window.Close(); }
    });

    // OD-0094: the card's gradient title (here a pair's lead-to-heads colours) drifts at rest; a single-colour title
    // (a plain effort) stays as it is.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0094's drift at the site (EffortSlider.swift:573-583), not PR-COMPOSER-111's button clauses
    [InlineData(true)]
    [InlineData(false)]
    public void A_gradient_title_drifts_at_rest_and_a_single_colour_one_does_not(bool pair) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var look = pair ? new EffortPairLook(new HydraPair(ProviderKind.Codex), new AppModel().Providers, null) : null;
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Lead", pair ? ProviderKind.Codex : ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, look, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal(pair, title.Foreground is Avalonia.Media.ILinearGradientBrush);
            byte[] Title()
            {
                window.UpdateLayout();
                var box = new Avalonia.Rect(title.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, title.Bounds.Size);
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var pixels = frame.Lock();
                var row = new byte[pixels.RowBytes];
                var result = new List<byte>();
                for (var y = (int)box.Top; y < (int)box.Bottom; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                    for (var x = (int)box.Left; x < (int)box.Right; x++) result.AddRange([row[x * 4], row[x * 4 + 1], row[x * 4 + 2]]);
                }
                return result.ToArray();
            }
            var start = Title();
            for (var ms = 0; ms < 1000; ms += 20)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
            }
            var later = Title();
            var moved = start.Zip(later, (a, b) => Math.Abs(a - b)).Max();
            if (pair) Assert.True(moved > 20, $"the pair's colours drift along the title: {moved}");
            else Assert.True(moved <= 1, $"a single colour stays: {moved}");
        }
        finally { window.Close(); }
    });
}
