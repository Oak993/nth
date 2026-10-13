using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.Setup.Ui;
using Xunit;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>
/// Frame capture for the reveal and spring tests (nth installer PLAN.md 3b and 8a): each element's drawn box (its layout
/// box through every render transform up to the screen) and its drawn opacity (the product of the opacities above it),
/// frame by frame on the rig's fixed 60 fps clock, with the plan's checks: no element moves more than 3 DIP in a frame,
/// none appears or disappears by more than 35 % opacity in a frame, the buttons stay put, nothing overlaps the buttons.
/// </summary>
public sealed class FrameProbe
{
    public const double MaxStep = 3, MaxFade = .35, Frame = 1.0 / 60;

    private readonly WindowRig rig;
    private readonly Visual root;
    private readonly IReadOnlyList<Control> elements;
    private readonly IReadOnlyList<Control> buttons;
    private Dictionary<Control, Drawn> last;

    public FrameProbe(WindowRig rig, Visual root, IReadOnlyList<Control> elements, IReadOnlyList<Control> buttons)
    {
        this.rig = rig;
        this.root = root;
        this.elements = elements;
        this.buttons = buttons;
        last = Capture();
        ButtonsAtStart = buttons.Select(b => Of(b).Box).ToList();
        Frames.Add(last);
    }

    public List<Dictionary<Control, Drawn>> Frames { get; } = [];

    public IReadOnlyList<Rect> ButtonsAtStart { get; }

    /// <summary>The largest movement of any element between two frames (DIP).</summary>
    public double LargestStep { get; private set; }

    /// <summary>The largest opacity change of any element between two frames.</summary>
    public double LargestFade { get; private set; }

    /// <summary>Whether the buttons must stay exactly where they started.</summary>
    public bool ButtonsFixed { get; init; } = true;

    /// <summary>Elements allowed to move (a whole screen change, a mark that settles): excluded from the step check.</summary>
    public HashSet<Control> Unchecked { get; } = [];

    public Drawn Of(Control element) => Measure(element);

    /// <summary>Runs <paramref name="seconds"/> of frames, checking each.</summary>
    public void Run(double seconds, Action<int>? beforeFrame = null)
    {
        var count = (int)Math.Round(seconds / Frame);
        for (var i = 0; i < count; i++)
        {
            beforeFrame?.Invoke(i);
            Step();
        }
    }

    /// <summary>One frame: the clock moves 16.7 ms, the window lays out, and the frame is checked against the one before.</summary>
    public void Step()
    {
        rig.Run(Frame);
        var now = Capture();
        foreach (var element in elements)
        {
            var a = last[element];
            var b = now[element];
            // movement counts while the element is drawn in both frames (a hidden element has no place to move from)
            if (!Unchecked.Contains(element) && (a.Opacity > .01 || b.Opacity > .01) && a.Box.Height > 0 && b.Box.Height > 0)
            {
                var step = Step(a.Box, b.Box);
                LargestStep = Math.Max(LargestStep, step);
                Assert.True(step <= MaxStep + 1e-6, $"{Name(element)} moved {step:F2} DIP in one frame (frame {Frames.Count}): {a.Box} to {b.Box}; {Chain(element)}");
            }
            var fade = Math.Abs(b.Opacity - a.Opacity);
            LargestFade = Math.Max(LargestFade, fade);
            Assert.True(fade <= MaxFade + 1e-6, $"{Name(element)} changed opacity by {fade:F2} in one frame (frame {Frames.Count})");
        }
        if (ButtonsFixed)
            for (var i = 0; i < buttons.Count; i++)
                if (now[buttons[i]].Opacity > .01 && ButtonsAtStart[i].Height > 0)
                    Assert.True(Same(ButtonsAtStart[i], now[buttons[i]].Box), $"{Name(buttons[i])} moved from {ButtonsAtStart[i]} to {now[buttons[i]].Box}");
        foreach (var element in elements)
        {
            if (buttons.Contains(element) || now[element].Opacity <= .01 || now[element].Box.Height <= 0) continue;
            foreach (var button in buttons)
            {
                if (now[button].Opacity <= .01 || now[button].Box.Height <= 0) continue;
                var overlap = now[element].Box.Intersect(now[button].Box);
                Assert.True(overlap.Width <= .5 || overlap.Height <= .5, $"{Name(element)} overlaps {Name(button)} ({now[element].Box} and {now[button].Box}); {Chain(element)} | {Chain(button)}");
            }
        }
        last = now;
        Frames.Add(now);
    }

    private Dictionary<Control, Drawn> Capture()
    {
        var all = new Dictionary<Control, Drawn>();
        foreach (var element in elements.Concat(buttons)) all[element] = Measure(element);
        return all;
    }

    private Drawn Measure(Control element)
    {
        if (!element.IsEffectivelyVisible) return new Drawn(new Rect(element.Bounds.Position, new Size()), 0);
        var tl = new Point(0, 0);
        var br = new Point(element.Bounds.Width, element.Bounds.Height);
        double opacity = 1;
        for (Visual? v = element; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            if (v.RenderTransform is { } transform)
            {
                var origin = v.RenderTransformOrigin.ToPixels(v.Bounds.Size);
                var m = Matrix.CreateTranslation(-origin.X, -origin.Y) * transform.Value * Matrix.CreateTranslation(origin.X, origin.Y);
                tl = tl.Transform(m);
                br = br.Transform(m);
            }
            tl += v.Bounds.Position;
            br += v.Bounds.Position;
            opacity *= v.Opacity;
        }
        return new Drawn(new Rect(tl, br).Normalize(), opacity);
    }

    /// <summary>The element's layout place and transform at each level up to the root (for failure messages).</summary>
    public string Chain(Control element)
    {
        var parts = new List<string>();
        for (Visual? v = element; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            var t = v.RenderTransform?.Value;
            parts.Add(v.GetType().Name + "@" + v.Bounds.ToString() + (t is { } m && !m.IsIdentity ? " t(" + m.M31.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "," + m.M32.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ")" : "") + (v is Layoutable l ? " d" + l.DesiredSize.ToString() : ""));
        }
        return string.Join(" < ", parts);
    }

    /// <summary>
    /// How far a box moved between two frames: along each axis the least move of its start, centre and end, so new words of
    /// another width (a centred title, a cross-fade) do not count as movement while a glide, which moves all three, does.
    /// </summary>
    public static double Step(Rect a, Rect b)
    {
        var x = Math.Min(Math.Abs(b.Left - a.Left), Math.Min(Math.Abs(b.Center.X - a.Center.X), Math.Abs(b.Right - a.Right)));
        var y = Math.Min(Math.Abs(b.Top - a.Top), Math.Min(Math.Abs(b.Center.Y - a.Center.Y), Math.Abs(b.Bottom - a.Bottom)));
        return Math.Max(x, y);
    }

    private static bool Same(Rect a, Rect b) => Math.Abs(a.X - b.X) < .01 && Math.Abs(a.Y - b.Y) < .01 && Math.Abs(a.Width - b.Width) < .01 && Math.Abs(a.Height - b.Height) < .01;

    private static string Name(Control element) => element switch
    {
        TextBlock t => "\"" + (t.Text ?? Avalonia.Automation.AutomationProperties.GetName(t)) + "\"",
        SetupButton b => b.Label + " button",
        _ => element.GetType().Name,
    };
}
