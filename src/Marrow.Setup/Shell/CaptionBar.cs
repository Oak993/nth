using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Setup.Motion;
using Marrow.Setup.Platform;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Path = Avalonia.Controls.Shapes.Path;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Custom caption"; unit U1, ED-NEW-inst-u1-1). Where a point in the window falls, as
/// Windows asks it (WM_NCHITTEST): the caption band drags (HTCAPTION), Minimise and Close report HTMINBUTTON and HTCLOSE so
/// Windows treats them as caption buttons, and nothing is ever HTMAXBUTTON, so the snap-layout flyout never opens. The
/// window is not resizable, so there are no border codes.
/// </summary>
public static class CaptionHitTest
{
    public static int At(Point client, double windowWidth)
    {
        if (client.X < 0 || client.Y < 0 || client.X >= windowWidth) return Native.HTNOWHERE;
        if (client.Y >= CaptionBar.BandHeight) return Native.HTCLIENT;
        if (client.X >= windowWidth - CaptionBar.ButtonWidth) return Native.HTCLOSE;
        if (client.X >= windowWidth - 2 * CaptionBar.ButtonWidth) return Native.HTMINBUTTON;
        return Native.HTCAPTION;
    }

    public static bool IsButton(int hit) => hit is Native.HTMINBUTTON or Native.HTCLOSE;
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3; the canvas's <c>.tb</c> and <c>.cap</c>; unit U1). The window's own caption, 34 DIP:
/// the 16 px mark, the title ("nth Setup" or "Uninstall nth", 12, second ink), then Minimise and Close, 46 × 34 each with
/// 10 px glyphs. Hover fills Minimise with a faint wash and Close with Windows' red (0.12 s); a paused Close is drawn at
/// 35 % and does not light up (a contrast theme draws it in the disabled-text colour). The buttons take no keyboard focus;
/// their clicks are Windows' (the window's message hook), so they work wherever the pointer is captured.
/// </summary>
public sealed class CaptionBar : Grid
{
    public const double BandHeight = 34, ButtonWidth = 46;

    private readonly TextBlock title;
    private readonly Image icon;

    public CaptionBar(Animator animator, Bitmap? mark)
    {
        ArgumentNullException.ThrowIfNull(animator);
        Height = BandHeight;
        ColumnDefinitions = new ColumnDefinitions("12,16,8,*,46,46");
        Background = Brushes.Transparent;
        icon = new Image { Source = mark, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        SetColumn(icon, 1);
        title = SetupTheme.Text(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false }, TextRole.Caption, SetupTheme.Ink2);
        SetColumn(title, 3);
        Minimise = new CaptionButton(animator, "M0 5.5h10", close: false) { Name = "CaptionMinimise" };
        AutomationProperties.SetName(Minimise, SetupStrings.CaptionMinimise);
        SetColumn(Minimise, 4);
        Close = new CaptionButton(animator, "M.5.5l9 9M9.5.5l-9 9", close: true) { Name = "CaptionClose" };
        AutomationProperties.SetName(Close, SetupStrings.CaptionClose);
        SetColumn(Close, 5);
        Children.Add(icon);
        Children.Add(title);
        Children.Add(Minimise);
        Children.Add(Close);
    }

    public CaptionButton Minimise { get; }

    public CaptionButton Close { get; }

    public string Title
    {
        get => title.Text ?? "";
        set => title.Text = value;
    }

    public TextBlock TitleBlock => title;

    /// <summary>The 16 px mark, drawn from the icon frame nearest the window's DPI.</summary>
    public void SetMark(Bitmap mark) => icon.Source = mark;

    /// <summary>The button under the pointer (a hit-test code, 0 for none), from WM_NCMOUSEMOVE and WM_NCMOUSELEAVE.</summary>
    public void SetHover(int hit)
    {
        Minimise.IsHot = hit == Native.HTMINBUTTON;
        Close.IsHot = hit == Native.HTCLOSE;
    }

    /// <summary>The button pressed (a hit-test code, 0 for none).</summary>
    public void SetPressed(int hit)
    {
        Minimise.IsPressedDown = hit == Native.HTMINBUTTON;
        Close.IsPressedDown = hit == Native.HTCLOSE;
    }

    public void ApplyPalette(SetupPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Minimise.ApplyPalette(palette);
        Close.ApplyPalette(palette);
    }
}

/// <summary>One drawn caption button: a hover wash that fades in 0.12 s, a 10 px glyph, and a paused look for Close.</summary>
public sealed class CaptionButton : Border, IMotion
{
    private const double FadeSeconds = 0.12;

    private readonly Animator animator;
    private readonly bool close;
    private readonly Border wash = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Path glyph;
    private readonly Tween hot = new(0);
    private SetupPalette palette = SetupPalette.Dark;
    private bool isHot;
    private bool isPressedDown;
    private bool isPaused;

    public CaptionButton(Animator animator, string glyphPath, bool close)
    {
        this.animator = animator;
        this.close = close;
        Width = CaptionBar.ButtonWidth;
        Height = CaptionBar.BandHeight;
        Focusable = false;
        Background = Brushes.Transparent;
        glyph = new Path
        {
            Data = StreamGeometry.Parse(glyphPath),
            Width = 10,
            Height = 10,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Child = new Panel { Children = { wash, glyph } };
        PointerEntered += (_, _) => IsHot = true; // headless and a missed WM_NCMOUSEMOVE; the message hook drives it on Windows
        PointerExited += (_, _) => IsHot = false;
        Draw();
    }

    /// <summary>The pointer is over it.</summary>
    public bool IsHot
    {
        get => isHot;
        set
        {
            if (isHot == value) return;
            isHot = value;
            Fade();
        }
    }

    /// <summary>It is pressed.</summary>
    public bool IsPressedDown
    {
        get => isPressedDown;
        set
        {
            if (isPressedDown == value) return;
            isPressedDown = value;
            Fade();
        }
    }

    /// <summary>Close is paused: drawn disabled, no hover.</summary>
    public bool IsPaused
    {
        get => isPaused;
        set
        {
            if (isPaused == value) return;
            isPaused = value;
            Fade();
        }
    }

    /// <summary>How lit the hover wash is now (0..1).</summary>
    public double Lit => hot.Value;

    public IBrush? GlyphBrush => glyph.Stroke;

    public double GlyphOpacity => glyph.Opacity;

    public void ApplyPalette(SetupPalette next)
    {
        palette = next ?? throw new ArgumentNullException(nameof(next));
        Draw();
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = hot.Advance(seconds);
        Draw();
        return moving;
    }

    private void Fade()
    {
        var target = (isHot || isPressedDown) && !isPaused ? 1 : 0;
        if (hot.Target != target)
        {
            hot.Retarget(target, FadeSeconds * Math.Abs(target - hot.Value), CubicBezier.Ease);
            animator.Start(this);
        }
        Draw(); // a pause changes the glyph even when the wash does not move
    }

    private void Draw()
    {
        wash.Background = new SolidColorBrush(close ? palette.CloseHover : palette.CaptionHover);
        wash.Opacity = hot.Value;
        var ink = isPaused ? palette.CaptionDisabledInk : SetupPalette.Lerp(palette.Ink1, close ? palette.CloseHoverInk : palette.CaptionHoverInk, hot.Value);
        glyph.Stroke = new SolidColorBrush(ink);
        glyph.Opacity = isPaused ? palette.CaptionDisabledOpacity : 1;
    }
}
