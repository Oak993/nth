using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Platform;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Path = Avalonia.Controls.Shapes.Path;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, step 3, and 3b's table, "Welcome: the signature line"; the canvas's first fact; unit
/// U1, ED-NEW-inst-u1-1). Welcome's first fact, read from the setup's own signature at start: a seal and "Signed by
/// ‹Publisher›" in the facts' ink, or a warning sign and "Unsigned test build, not for sharing" in the warning colour.
/// The check hashes the whole file, so it finishes after the window shows: until then the line keeps its place (the
/// height of its longest wording at the current width and text size) and shows nothing, then fades in (0.24 s, 60 ms
/// after) with no movement. A later change cross-fades: out (0.16 s), the words swap, back in. Nothing around it moves.
/// </summary>
public sealed class SignatureLine : Decorator, IMotion
{
    private const string SealPath = "M8 1.5l5.2 2v4.1c0 3.2-2.2 5.6-5.2 6.9-3-1.3-5.2-3.7-5.2-6.9V3.5z M5.6 8.1l1.7 1.7 3.2-3.4";
    private const string WarnOutline = "M8 2.2l6 10.6H2z";
    private const string WarnMark = "M8 6.6v3M8 11.3v.1";

    private readonly StackPanel row;
    private readonly TextBlock text;
    private readonly TextBlock reserve;
    private readonly Path first = new() { StrokeThickness = 1.2, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round };
    private readonly Path second = new() { StrokeThickness = 1.3, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round };
    private readonly Spring opacity = new(0.24, 1, 0, 0.002);
    private readonly Func<bool> calm;
    private Animator? animator;
    private SignatureResult shown = SignatureResult.Pending;
    private SignatureResult next = SignatureResult.Pending;
    private double delay;

    public SignatureLine(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        var glyph = new Viewbox
        {
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 2.5, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Canvas { Width = 16, Height = 16, Children = { first, second } },
        };
        AutomationProperties.SetAccessibilityView(glyph, AccessibilityView.Raw);
        text = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap }, TextRole.Facts, ink: null);
        row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { glyph, text }, Opacity = 0 };
        reserve = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(24, 0, 0, 0) }, TextRole.Facts, ink: null);
        AutomationProperties.SetAccessibilityView(reserve, AccessibilityView.Raw);
        reserve.Text = SetupStrings.WelcomeUnsigned;
        Child = new Grid { Children = { reserve, row } };
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    /// <summary>What the line shows now.</summary>
    public SignatureResult Shown => shown;

    public string? Words => text.Text;

    public double LineOpacity => row.Opacity;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>The check's answer (or a later one). Invalid shows nothing here: the window goes to Problem.</summary>
    public void Set(SignatureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        next = result;
        reserve.Text = Longest(result);
        if (result.State is SignatureState.Unknown or SignatureState.Invalid)
        {
            opacity.Response = calm() ? 0.15 : 0.16;
            opacity.Retarget(0);
        }
        else if (shown.State is SignatureState.Unknown or SignatureState.Invalid)
        {
            Draw(result);
            opacity.Snap(0);
            delay = calm() ? 0 : HoldLine.ArrivalDelay;
            opacity.Response = calm() ? 0.15 : 0.24;
            opacity.Retarget(1);
        }
        else if (!SameWords(shown, result))
        {
            opacity.Response = calm() ? 0.15 : 0.16;
            opacity.Retarget(0); // out first; the words swap at 0 (Advance)
        }
        Run();
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
        if (!moving && opacity.Target == 0 && next.State is SignatureState.Signed or SignatureState.Unsigned && !ReferenceEquals(shown, next))
        {
            Draw(next);
            opacity.Response = calm() ? 0.15 : 0.24;
            opacity.Retarget(1);
            moving = true;
        }
        row.Opacity = Math.Clamp(opacity.Value, 0, 1);
        return moving;
    }

    private void Run()
    {
        if (animator is null)
        {
            if (opacity.Target == 0 && next.State is SignatureState.Signed or SignatureState.Unsigned) Draw(next);
            opacity.Snap(next.State is SignatureState.Signed or SignatureState.Unsigned ? 1 : 0);
            delay = 0;
            row.Opacity = opacity.Value;
            return;
        }
        animator.Start(this);
    }

    private void Draw(SignatureResult result)
    {
        shown = result;
        var signed = result.State == SignatureState.Signed;
        text.Text = WordsFor(result);
        first.Data = StreamGeometry.Parse(signed ? SealPath : WarnOutline);
        first.StrokeThickness = 1.2;
        second.Data = signed ? null : StreamGeometry.Parse(WarnMark);
        var ink = signed ? SetupTheme.Ink2 : SetupTheme.Warn;
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(ink));
        first.Bind(Shape.StrokeProperty, first.GetResourceObservable(ink));
        second.Bind(Shape.StrokeProperty, second.GetResourceObservable(ink));
    }

    /// <summary>The words for a result: "Signed by ‹Publisher›" or the unsigned line.</summary>
    public static string WordsFor(SignatureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.State == SignatureState.Signed && result.Publisher is { } publisher ? SetupStrings.WelcomeSigned(publisher) : SetupStrings.WelcomeUnsigned;
    }

    private static string Longest(SignatureResult result)
    {
        var unsigned = SetupStrings.WelcomeUnsigned;
        var signed = result.State == SignatureState.Signed ? WordsFor(result) : "";
        return signed.Length > unsigned.Length ? signed : unsigned;
    }

    private static bool SameWords(SignatureResult a, SignatureResult b) => a.State == b.State && string.Equals(WordsFor(a), WordsFor(b), StringComparison.Ordinal);
}
