using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3b, "Reserved, not animated", and 4's stall line; the canvas's <c>reserveFit</c>; unit U3,
/// ED-NEW-inst-u3-1). A line whose words change in place and must move nothing around it: Installing's subtitle, "This usually
/// takes less than a minute." and "Slower than usual. Windows may be checking the files." Every wording it can show sits in
/// the same grid cell, invisible, so the line always takes the height (and width) of its tallest wording at the current
/// width and text size, measured by layout itself, again whenever DPI or text size changes. The words cross-fade as
/// Reveal's text change does: out (critically damped, 0.16 s), the words swap at 0, back in 60 ms later (0.24 s), without
/// the settle; asked back before it has gone, it fades back. Calm: 0.15 s fades. Opacity only. The line is a polite live
/// region whose name is its words, so Narrator reads the new words.
/// </summary>
public sealed class ReservedText : Decorator, IMotion
{
    private readonly Grid cell = new();
    private readonly TextBlock text;
    private readonly Spring opacity = new(0.24, 1, 1, 0.002);
    private readonly Func<bool> calm;
    private readonly TextRole role;
    private readonly string ink;
    private readonly TextAlignment alignment;
    private readonly List<string> wordings = [];
    private Animator? animator;
    private string? next;
    private double delay;

    public ReservedText(TextRole role, string ink, Func<bool> calm, IEnumerable<string> wordings, TextAlignment alignment = TextAlignment.Center)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        ArgumentNullException.ThrowIfNull(wordings);
        this.role = role;
        this.ink = ink ?? throw new ArgumentNullException(nameof(ink));
        this.alignment = alignment;
        text = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = alignment }, role, ink);
        foreach (var wording in wordings) Reserve(wording);
        if (this.wordings.Count == 0) throw new ArgumentException("A reserved line needs at least one wording.", nameof(wordings));
        cell.Children.Add(text);
        Child = cell;
        Words = this.wordings[0];
        text.Text = Words;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(this, Words);
    }

    /// <summary>The words shown, or arriving.</summary>
    public string Words { get; private set; }

    /// <summary>The words drawn now (during a cross-fade, the old ones until they have faded out).</summary>
    public string? DrawnWords => text.Text;

    /// <summary>The words' drawn opacity.</summary>
    public double LineOpacity => text.Opacity;

    /// <summary>Every wording whose place is kept.</summary>
    public IReadOnlyList<string> Wordings => wordings;

    /// <summary>The frame loop it moves on (the window's).</summary>
    public void Attach(Animator frames) => animator = frames;

    /// <summary>Adds a wording whose place is kept from now on (a wording first met in <see cref="Set"/> is added too).</summary>
    public void Reserve(string wording)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wording);
        if (wordings.Contains(wording, StringComparer.Ordinal)) return;
        wordings.Add(wording);
        var reserve = SetupTheme.Text(new TextBlock { Text = wording, TextWrapping = TextWrapping.Wrap, TextAlignment = alignment, Opacity = 0, IsHitTestVisible = false }, role, ink);
        AutomationProperties.SetAccessibilityView(reserve, AccessibilityView.Raw);
        cell.Children.Insert(cell.Children.Count == 0 ? 0 : cell.Children.Count - 1, reserve);
    }

    /// <summary>Changes the words: the old fade out, the new fade in, and nothing around the line moves.</summary>
    public void Set(string words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(words);
        Reserve(words);
        if (string.Equals(words, Words, StringComparison.Ordinal)) return;
        Words = words;
        AutomationProperties.SetName(this, words);
        if (string.Equals(text.Text, words, StringComparison.Ordinal))
        {
            next = null; // asked back while fading out: fade back
            delay = 0;
            opacity.Response = calm() ? 0.15 : 0.24;
            opacity.Retarget(1);
        }
        else
        {
            next = words;
            opacity.Response = calm() ? 0.15 : 0.16;
            opacity.Retarget(0); // out first; the words swap at 0
        }
        Run();
    }

    /// <summary>Puts <paramref name="words"/> in place at once (a screen set up before it shows).</summary>
    public void Now(string words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(words);
        Reserve(words);
        Words = words;
        next = null;
        delay = 0;
        text.Text = words;
        opacity.Snap(1);
        text.Opacity = 1;
        AutomationProperties.SetName(this, words);
    }

    bool IMotion.Advance(double seconds)
    {
        if (delay > 0)
        {
            var used = Math.Min(delay, seconds);
            delay -= used;
            seconds -= used;
            if (delay > 0) return true;
        }
        var moving = opacity.Advance(seconds);
        if (!moving && opacity.Target == 0 && next is not null)
        {
            text.Text = next;
            next = null;
            delay = calm() ? 0 : HoldLine.ArrivalDelay;
            opacity.Response = calm() ? 0.15 : 0.24;
            opacity.Retarget(1);
            moving = true;
        }
        text.Opacity = Math.Clamp(opacity.Value, 0, 1);
        return moving;
    }

    private void Run()
    {
        if (animator is null)
        {
            if (next is not null) text.Text = next;
            next = null;
            delay = 0;
            opacity.Snap(1);
            text.Opacity = 1;
            return;
        }
        animator.Start(this);
    }
}
