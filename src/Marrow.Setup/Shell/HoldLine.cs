using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Close", and 3b; the canvas's <c>.hold</c>; unit U1, ED-NEW-inst-u1-1). The line at
/// the bottom of Installing, Update and Removing that says why Close is paused ("You can close this window once nth is
/// safely in place."). It sits under the screen's flexible space, so its arrival moves nothing above it. Reveal: it fades
/// in 60 ms after it is asked for (0.24 s, critically damped) with a 5 DIP settle from above (response 0.3 s); it leaves
/// by fading out (0.16 s) with a 4 DIP lift and only then gives up its place; asked back before it has gone, it simply
/// fades back. A try to close while it shows brightens it (hint ink to the first ink and back, the canvas's 1.4 s ping),
/// from wherever its colour is. Calm: no movement, 0.15 s fades, and a try brightens it and keeps it bright.
/// The line is a polite live region inside a container that is always there, so Narrator reads it when it appears.
/// </summary>
public sealed class HoldLine : Decorator, IMotion
{
    public static readonly StyledProperty<Color> DimColorProperty = AvaloniaProperty.Register<HoldLine, Color>(nameof(DimColor));
    public static readonly StyledProperty<Color> BrightColorProperty = AvaloniaProperty.Register<HoldLine, Color>(nameof(BrightColor));

    // The canvas's ping: 1.4 s; bright from 18 % to 62 % of it.
    public const double PingRise = 0.18 * 1.4, PingHoldUntil = 0.62 * 1.4, PingFall = 1.4 - PingHoldUntil;
    public const double ArrivalDelay = 0.06, SettleFrom = -5, LeaveLift = -4;

    private readonly TextBlock text;
    private readonly TranslateTransform shift = new();
    private readonly Spring opacity = new(0.24, 1, 0, 0.002);
    private readonly Spring offset = new(0.3, 1, 0, 0.01);
    private readonly Tween brightness = new(0);
    private readonly Func<bool> calm;
    private Animator? animator;
    private double delay;
    private double holdLeft;
    private bool wanted;

    public HoldLine(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        text = SetupTheme.Text(new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 }, TextRole.Hint, ink: null);
        text.RenderTransform = shift;
        text.Opacity = 0;
        Child = text;
        HorizontalAlignment = HorizontalAlignment.Center;
        IsVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        SetupTheme.BindColor(this, DimColorProperty, SetupTheme.Ink3);
        SetupTheme.BindColor(this, BrightColorProperty, SetupTheme.Ink1);
    }

    public Color DimColor
    {
        get => GetValue(DimColorProperty);
        set => SetValue(DimColorProperty, value);
    }

    public Color BrightColor
    {
        get => GetValue(BrightColorProperty);
        set => SetValue(BrightColorProperty, value);
    }

    /// <summary>The words shown (or last shown).</summary>
    public string? Words => text.Text;

    /// <summary>True from the moment it is asked for until it is asked to leave.</summary>
    public bool IsWanted => wanted;

    /// <summary>The line's drawn opacity (0 when gone).</summary>
    public double LineOpacity => text.Opacity;

    /// <summary>The line's drawn vertical offset in DIP.</summary>
    public double LineOffset => shift.Y;

    /// <summary>0 at the hint ink, 1 at the first ink.</summary>
    public double Brightness => brightness.Value;

    /// <summary>The frame loop it moves on (the window's).</summary>
    public void Attach(Animator frames) => animator = frames;

    /// <summary>Asks for the line with <paramref name="words"/>. Arriving from nothing it settles in; leaving, it fades back.</summary>
    public void Show(string words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(words);
        text.Text = words;
        if (wanted) return;
        wanted = true;
        if (!IsVisible)
        {
            IsVisible = true; // the commit: its place appears under the flexible space, nothing above moves
            opacity.Snap(0);
            offset.Snap(calm() ? 0 : SettleFrom);
            delay = ArrivalDelay;
            brightness.Snap(0);
            holdLeft = 0;
        }
        opacity.Response = calm() ? 0.15 : 0.24;
        opacity.Retarget(1);
        offset.Response = 0.3;
        offset.Retarget(0);
        Run();
    }

    /// <summary>Asks the line to leave: it fades out with a small lift, then gives up its place.</summary>
    public void Hide()
    {
        if (!wanted) return;
        wanted = false;
        delay = 0;
        opacity.Response = calm() ? 0.15 : 0.16;
        opacity.Retarget(0);
        offset.Response = 0.16;
        offset.Retarget(calm() ? 0 : LeaveLift);
        Run();
    }

    /// <summary>A try to close while Close is paused: the line brightens and settles back (Calm: it stays bright).</summary>
    public void Ping()
    {
        if (!wanted) return;
        if (calm())
        {
            brightness.Retarget(1, 0.2, CubicBezier.Linear);
            holdLeft = double.PositiveInfinity;
        }
        else
        {
            var rise = PingRise * (1 - brightness.Value); // from wherever it is, never a jump
            brightness.Retarget(1, Math.Max(rise, 1e-3), CubicBezier.Ease);
            holdLeft = PingHoldUntil - rise; // the fall starts 62 % into the ping, counted from the try
        }
        Run();
    }

    /// <summary>Puts the line straight into its shown or hidden state (a screen set up before it fades in).</summary>
    public void Now(string? words)
    {
        wanted = words is not null;
        if (words is not null) text.Text = words;
        IsVisible = wanted;
        opacity.Snap(wanted ? 1 : 0);
        offset.Snap(0);
        brightness.Snap(0);
        delay = 0;
        holdLeft = 0;
        Draw();
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = false;
        if (delay > 0)
        {
            var used = Math.Min(delay, seconds);
            delay -= used;
            moving = true;
            if (delay > 0)
            {
                // the 5 DIP settle rides with the commit; only the fade waits
                moving |= offset.Advance(seconds);
                Draw();
                return true;
            }
            seconds -= used;
        }
        moving |= opacity.Advance(seconds);
        moving |= offset.Advance(seconds);
        if (!brightness.IsAtRest) brightness.Advance(seconds);
        else if (holdLeft > 0 && !double.IsPositiveInfinity(holdLeft))
        {
            holdLeft -= seconds;
            if (holdLeft <= 0)
            {
                holdLeft = 0;
                brightness.Retarget(0, PingFall, CubicBezier.Ease);
            }
        }
        // still moving while bright and waiting to fall (the frame the rise ends must not stop the loop)
        moving |= !brightness.IsAtRest || (holdLeft > 0 && !double.IsPositiveInfinity(holdLeft));
        if (!wanted && opacity.IsAtRest && opacity.Value <= 0)
        {
            IsVisible = false; // gone: it gives up its place
            brightness.Snap(0);
            holdLeft = 0;
        }
        Draw();
        return moving;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DimColorProperty || change.Property == BrightColorProperty) Draw();
    }

    private void Run()
    {
        if (animator is null)
        {
            // no frame loop (not in a window yet): go straight to the end state
            opacity.Snap(opacity.Target);
            offset.Snap(offset.Target);
            brightness.Snap(brightness.Target);
            delay = 0;
            if (!wanted) IsVisible = false;
            Draw();
            return;
        }
        Draw();
        animator.Start(this);
    }

    private void Draw()
    {
        text.Opacity = Math.Clamp(opacity.Value, 0, 1);
        shift.Y = offset.Value;
        text.Foreground = new SolidColorBrush(SetupPalette.Lerp(DimColor, BrightColor, brightness.Value));
    }
}
