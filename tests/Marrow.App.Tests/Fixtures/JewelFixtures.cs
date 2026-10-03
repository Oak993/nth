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

    /// <summary>A "r,g,b" tint string as a colour.</summary>
    public static SKColor Rgb(string text)
    {
        var parts = text.Split(',').Select(byte.Parse).ToArray();
        return new SKColor(parts[0], parts[1], parts[2]);
    }

    /// <summary>A slider <paramref name="width"/> px wide in a window, its index box, with the frame clock and time held.</summary>
    public static JewelRig Rig(int count = 4, int index = 0, double width = 298, bool fast = false, EffortBrand brand = EffortBrand.Claude, EffortBrand? fadeFrom = null)
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        var source = FrameClock.Source.Override(frames);
        var clock = SwiftClock.Provider.Override(time);
        var box = new ObservableIntBox { Value = index };
        var slider = new EffortSlider(count, box.AsBinding(), "Title", fast, brand, null, fadeFrom) { Width = width };
        var window = new Avalonia.Controls.Window { Width = width + 40, Height = 160, Content = new Border { Padding = new Thickness(20), Child = slider } };
        window.Show();
        window.Activate(); // the front window: the knob runs while its window is shown (FrontMonitor)
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
