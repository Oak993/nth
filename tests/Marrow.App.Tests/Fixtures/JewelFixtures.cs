using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// OD-0090 helpers: the values the locked Jewel knob page computes, recorded by tests/fixtures/jewel-knob/expected.mjs from
/// E:/Droppy/work/design/jewel-knob-locked/ (knob-variants.js, knob-orbs-1.js, strike-engine.js, jewel-knob.html; the files'
/// sha256 are in the JSON), and a rig that drives one slider frame by frame.
/// </summary>
public static class JewelFixtures
{
    private static JsonElement? expected;

    /// <summary>tests/fixtures/jewel-knob/expected.json, read once.</summary>
    public static JsonElement Expected
    {
        get
        {
            if (expected is { } e) return e;
            using var document = JsonDocument.Parse(File.ReadAllBytes(StrikeFixtures.FixturePath("jewel-knob", "expected.json")));
            return (expected = document.RootElement.Clone()).Value;
        }
    }

    /// <summary>The page's maker name for an app brand.</summary>
    public static EffortBrand BrandOf(string maker) => maker switch
    {
        "Claude" => EffortBrand.Claude,
        "OpenAI" => EffortBrand.Silver,
        "Gemini" => EffortBrand.Antigravity,
        "DeepSeek" => EffortBrand.Deepseek,
        "Grok" => EffortBrand.Grok,
        "Zai" => EffortBrand.Zai,
        "Pi" => EffortBrand.Pi,
        "Cursor" => EffortBrand.Cursor,
        _ => throw new ArgumentOutOfRangeException(nameof(maker)),
    };

    /// <summary>The colour names of a material state, in knob-variants.js's keys, with the port's value of each.</summary>
    public static IEnumerable<(string Key, SKColor Value)> Colours(JewelState s) =>
    [
        ("colorA", s.ColorA), ("colorB", s.ColorB), ("colorC", s.ColorC), ("colorD", s.ColorD), ("highlightColor", s.Highlight),
        ("glowColor", s.Glow), ("shellInner", s.ShellInner), ("shellMid", s.ShellMid), ("shellEdge", s.ShellEdge),
        ("sheenColor", s.Sheen), ("specColor", s.Spec), ("canvasColor", s.Canvas),
    ];

    /// <summary>The numbers of a material state, in knob-variants.js's keys (null: the recipe leaves it to the preset).</summary>
    public static IEnumerable<(string Key, double? Value)> Numbers(JewelState s) =>
    [
        ("exposure", s.Exposure), ("speed", s.Speed), ("zoom", s.Zoom), ("warp", s.Warp), ("ridgeAmt", s.RidgeAmt), ("sharp", s.Sharp),
    ];

    /// <summary>
    /// Every first contact of the A2 strikes with the bubbles in (from, to], counted directly from the rule (the owner's
    /// OD-0078 as the engine states it): each of the leader's seven steps lights the channel from one vertex before the last
    /// tip to round(min(1, j / 7) 0.9 last); the strike lights the whole channel and its five branches; a bubble (opacity at
    /// least 0.12) is touched when its circle comes within 0.6 k of a lit segment at that instant; a touched rise is gone.
    /// </summary>
    public static List<(double T, int Lane, int Rise)> Contacts(StrikeGeometry g, int strikeSeed, int bubbleSeed, double width, double from, double to)
    {
        var channels = new StrikeChannels(strikeSeed);
        var lanes = new BubbleLanes(bubbleSeed, width, g.Height);
        var lit = new List<(double T, List<(double[] Pts, int From, int To)> Segs)>();
        for (var k = (int)Math.Floor(from / Strike.Period) - 2; k <= (int)Math.Floor(to / Strike.Period) + 1; k++)
        {
            var at = Strike.StrikeAt(k, strikeSeed);
            var main = channels.Main(g, k);
            var last = main.Length / 2 - 1;
            var tip = 0;
            for (var j = 1; j <= 7; j++)
            {
                var next = (int)Math.Floor(Math.Min(1, j / 7.0) * 0.9 * last + 0.5);
                lit.Add((at - 0.14 + (j - 1) * 0.02, [(main, Math.Max(0, tip - 1), next)]));
                tip = next;
            }
            var all = new List<(double[], int, int)> { (main, 0, last) };
            for (var b = 0; b < 5; b++) all.Add((channels.Branch(g, k, b), 0, 5));
            lit.Add((at, all));
        }
        var dead = new HashSet<(int, int)>();
        var found = new List<(double, int, int)>();
        foreach (var (te, segs) in lit.Where(e => e.T > from && e.T <= to).OrderBy(e => e.T))
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes.At(i, te) is not { } now) continue;
                var (centre, radius, opacity) = LiquidTrack.BubbleAt(now.Rise, g.Height, now.Progress);
                if (opacity < 0.12 || dead.Contains((i, now.K))) continue;
                var reach = radius + 0.6 * g.K;
                var touched = segs.Any(s => Distance(centre.X, centre.Y, s.Pts, s.From, s.To) <= reach);
                if (!touched) continue;
                dead.Add((i, now.K));
                found.Add((te, i, now.K));
            }
        return found;
    }

    // the distance from a point to the polyline's vertices i0..i1 (one vertex when i1 <= i0)
    private static double Distance(double x, double y, double[] p, int i0, int i1)
    {
        if (i1 <= i0) return Math.Sqrt(Math.Pow(p[i0 * 2] - x, 2) + Math.Pow(p[i0 * 2 + 1] - y, 2));
        var best = double.MaxValue;
        for (var i = i0; i < i1; i++)
        {
            double ax = p[i * 2], ay = p[i * 2 + 1], dx = p[i * 2 + 2] - ax, dy = p[i * 2 + 3] - ay, l = dx * dx + dy * dy;
            var u = l > 0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / l, 0, 1) : 0;
            best = Math.Min(best, Math.Sqrt(Math.Pow(ax + dx * u - x, 2) + Math.Pow(ay + dy * u - y, 2)));
        }
        return best;
    }

    /// <summary>A "r,g,b" tint string as a colour.</summary>
    public static SKColor Rgb(string text)
    {
        var parts = text.Split(',').Select(byte.Parse).ToArray();
        return new SKColor(parts[0], parts[1], parts[2]);
    }

    /// <summary>A slider <paramref name="width"/> px wide in a window, its index box, with the frame clock and time held.</summary>
    public static JewelRig Rig(int count = 4, int index = 0, double width = 298, bool fast = false, EffortBrand brand = EffortBrand.Claude, EffortBrand? fadeFrom = null,
        bool reduceMotion = false, EffortPairBrand? pair = null)
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        var source = FrameClock.Source.Override(frames);
        var clock = SwiftClock.Provider.Override(time);
        var box = new ObservableIntBox { Value = index };
        var slider = new EffortSlider(count, box.AsBinding(), "Title", fast, brand, pair, fadeFrom) { Width = width };
        var window = new Avalonia.Controls.Window { Width = width + 40, Height = 160, Content = new Border { Padding = new Thickness(20), Child = slider } };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        window.Show();
        window.Activate(); // the front window: the knob runs while its window is shown (FrontMonitor)
        Dispatcher.UIThread.RunJobs();
        return new JewelRig(slider, box, window, time, frames, source, clock);
    }
    /// <summary>
    /// A slider the model list's switch has just faded in, as ModelEffortPanel plays it (EffortSlider.swift:180-188
    /// .transition(.opacity) under .animation(.easeOut(duration: 0.12), value: showsModels): SwiftTransition under the same
    /// Transaction, the content it replaces fading out beside it), in a window that showed a stand-in for the model list.
    /// </summary>
    public static JewelRig FadingRig(int count = 4, int index = 1, double width = 298)
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        var source = FrameClock.Source.Override(frames);
        var clock = SwiftClock.Provider.Override(time);
        var box = new ObservableIntBox { Value = index };
        var slider = new EffortSlider(count, box.AsBinding(), "Title") { Width = width };
        var pages = new Panel();
        pages.Children.Add(new Border { Height = EffortSlider.ThumbSize }); // the model list
        var window = new Avalonia.Controls.Window { Width = width + 40, Height = 160, Content = new Border { Padding = new Thickness(20), Child = pages } };
        window.Show();
        window.Activate();
        Dispatcher.UIThread.RunJobs();
        Transaction.WithAnimation(Animation.EaseOut(0.12), () =>
        {
            foreach (var old in pages.Children.ToArray()) SwiftTransition.Remove(pages, old);
            SwiftTransition.Insert(pages, pages.Children.Count, slider);
        });
        Dispatcher.UIThread.RunJobs();
        return new JewelRig(slider, box, window, time, frames, source, clock);
    }
}

/// <summary>One slider under test, frame by frame.</summary>
public sealed class JewelRig(EffortSlider slider, ObservableIntBox box, Avalonia.Controls.Window window, ManualTimeProvider time, ManualFrameSource frames,
    IDisposable source, IDisposable clock) : IDisposable
{
    public EffortSlider Slider => slider;
    public ObservableIntBox Index => box;
    public Avalonia.Controls.Window Window => window;
    public ManualFrameSource Frames => frames;

    /// <summary>The slider's top-left in the window.</summary>
    public Point Origin => slider.TranslatePoint(default, window)!.Value;

    /// <summary>Advances the clock and delivers one frame.</summary>
    public void Frame(double seconds = 1.0 / 60)
    {
        var span = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        time.Advance(span); frames.Advance(span); Dispatcher.UIThread.RunJobs();
    }

    public void Down(double x) => window.MouseDown(new Point(Origin.X + x, Origin.Y + EffortSlider.ThumbSize / 2), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
    public void Move(double x) => window.MouseMove(new Point(Origin.X + x, Origin.Y + EffortSlider.ThumbSize / 2), Avalonia.Input.RawInputModifiers.None);
    public void Up(double x) => window.MouseUp(new Point(Origin.X + x, Origin.Y + EffortSlider.ThumbSize / 2), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);

    public void Dispose() { window.Close(); clock.Dispose(); source.Dispose(); }
}
