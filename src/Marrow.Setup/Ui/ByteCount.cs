using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3b's table, "Installing and Update: the byte count beside Copying"; the canvas's
/// <c>.steps .d</c>; unit U3, ED-NEW-inst-u3-1). "0 of 420 MB" to "420 of 420 MB" in Cascadia Mono 12 (every figure the same width),
/// right-aligned in the width of its longest form, which an invisible copy of that form keeps, so the step label beside it
/// never re-wraps mid-copy. It fades in when its step starts working (0.25 s; Calm 0.15 s) and only its digits change.
/// </summary>
public sealed class ByteCount : Decorator, IMotion
{
    private readonly TextBlock text;
    private readonly TextBlock longest;
    private readonly Spring opacity = new(0.25, 1, 0, 0.002);
    private readonly Func<bool> calm;
    private Animator? animator;

    public ByteCount(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        text = SetupTheme.Text(new TextBlock { TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.NoWrap }, TextRole.Numbers, SetupTheme.Ink3);
        longest = SetupTheme.Text(new TextBlock { TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.NoWrap, Opacity = 0, IsHitTestVisible = false }, TextRole.Numbers, SetupTheme.Ink3);
        AutomationProperties.SetAccessibilityView(longest, AccessibilityView.Raw);
        Child = new Grid { Children = { longest, text } };
        text.Opacity = 0;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>The count shown.</summary>
    public string? Words => text.Text;

    /// <summary>The longest form, whose width is kept.</summary>
    public string? Longest => longest.Text;

    public double CountOpacity => text.Opacity;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>Sets the count and its longest form, and whether it shows.</summary>
    public void Set(string? words, string? longestForm, bool shown)
    {
        longest.Text = longestForm ?? words;
        text.Text = words;
        var target = shown && words is not null ? 1 : 0;
        if (opacity.Target == target) return;
        opacity.Response = calm() ? 0.15 : 0.25;
        opacity.Retarget(target);
        if (animator is null)
        {
            opacity.Snap(target);
            text.Opacity = target;
            return;
        }
        animator.Start(this);
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = opacity.Advance(seconds);
        text.Opacity = Math.Clamp(opacity.Value, 0, 1);
        return moving;
    }
}
