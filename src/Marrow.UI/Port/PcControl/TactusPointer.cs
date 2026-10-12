// PORT-ONLY: the Tactus pointer's drawing (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5 "The Tactus pointer", 10.1; canvas
// pc-control-ui/index.html 01, 02 and 10: `.tpt`, `.arr`, `.pbd`, `.rp`, `.pzb`, `.real`). A silver arrow with a dark
// outline and a soft shadow, the acting app's icon as a 13 DIP badge at its heel, a 32 DIP ripple ring where it presses,
// a pause badge while its app is paused, and a white glow while it borrows the real mouse. It is distinct from the user's
// white arrow in fill, outline and badge. The pose (PointerTrack) is applied through reused transforms only, so a frame
// changes no layout and allocates nothing. It never takes input and is hidden from assistive technology (the pill names
// the apps; the pointer is decoration).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port.PcControl.Pointer;
using Path = Avalonia.Controls.Shapes.Path;

namespace Marrow.UI.Port.PcControl;

public sealed class TactusPointer : Canvas
{
    /// <summary>The badge's size (DIP) and its place at the arrow's heel, from the tip.</summary>
    public const double BadgeSize = 13;

    public static readonly Point BadgeAt = new(12, 17);

    /// <summary>The pause badge's size (DIP) and place, from the tip.</summary>
    public const double PauseSize = 15;

    public static readonly Point PauseAt = new(-15, 16);

    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x10, 0x14));
    private static readonly IBrush Neutral = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40));

    private readonly Canvas body = new() { IsHitTestVisible = false };
    private readonly Canvas rippleHost = new() { IsHitTestVisible = false };
    private readonly MatrixTransform bodyTransform = new();
    private readonly MatrixTransform rippleHostTransform = new();
    private readonly ScaleTransform rippleScale = new();
    private readonly ScaleTransform pauseScale = new();
    private readonly Ellipse ripple;
    private readonly Path glow;
    private readonly Border badge;
    private readonly TextBlock badgeLetter;
    private readonly Border pause;
    private PointerPose pose = PointerPose.None;
    private Point origin;

    public TactusPointer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        ripple = new Ellipse
        {
            Width = PointerTrack.RippleSize,
            Height = PointerTrack.RippleSize,
            Stroke = new ImmutableSolidColorBrush(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.5,
            RenderTransform = rippleScale,
            RenderTransformOrigin = RelativePoint.Center,
            Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 10, Color = Colors.White, Opacity = 0.35 },
        };
        SetLeft(ripple, -PointerTrack.RippleSize / 2);
        SetTop(ripple, -PointerTrack.RippleSize / 2);
        rippleHost.Children.Add(ripple);
        rippleHost.RenderTransform = rippleHostTransform;
        rippleHost.RenderTransformOrigin = RelativePoint.TopLeft;

        glow = new Path { Data = Arrow, Fill = Brushes.White, Effect = new BlurEffect { Radius = 7 }, Opacity = 0 };
        var arrow = new Path
        {
            Data = Arrow,
            Fill = Silver,
            Stroke = Ink,
            StrokeThickness = 1.2,
            StrokeJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 2, BlurRadius = 3, Color = Colors.Black, Opacity = 0.6 },
        };
        badgeLetter = new TextBlock
        {
            FontSize = BadgeSize / 2,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        badge = new Border
        {
            Width = BadgeSize,
            Height = BadgeSize,
            CornerRadius = new CornerRadius(BadgeSize * 0.24),
            Background = Neutral,
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 0 0 1.5 #EB0E0E10, 0 1 3 0 #80000000"),
            Child = badgeLetter,
            IsVisible = false,
        };
        SetLeft(badge, BadgeAt.X);
        SetTop(badge, BadgeAt.Y);
        pause = new Border
        {
            Width = PauseSize,
            Height = PauseSize,
            CornerRadius = new CornerRadius(PauseSize / 2),
            Background = new LinearGradientBrush
            {
                StartPoint = RelativePoint.TopLeft,
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0xF6, 0xF6, 0xF8), 0), new GradientStop(Color.FromRgb(0xCD, 0xCD, 0xD2), 1) },
            },
            BoxShadow = BoxShadows.Parse("0 0 0 1.5 #FF121214"),
            Child = new Path { Data = PauseGlyph, Fill = new ImmutableSolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            RenderTransform = pauseScale,
            RenderTransformOrigin = RelativePoint.Center,
            IsVisible = false,
        };
        SetLeft(pause, PauseAt.X);
        SetTop(pause, PauseAt.Y);
        body.Children.Add(glow);
        body.Children.Add(arrow);
        body.Children.Add(badge);
        body.Children.Add(pause);
        body.RenderTransform = bodyTransform;
        body.RenderTransformOrigin = RelativePoint.TopLeft;
        Children.Add(rippleHost);
        Children.Add(body);
        Apply();
    }

    /// <summary>The arrow, with its tip at (0, 0): the canvas's 22 x 26 path less its (2, 1.5) offset.</summary>
    public static Geometry Arrow { get; } = StreamGeometry.Parse("M0,0 L0,19.2 L4.8,14.8 L8,22 L11.4,20.5 L8.3,13.4 L14.9,13.1 Z");

    /// <summary>The two bars of the pause glyph, 9 DIP square.</summary>
    public static Geometry PauseGlyph { get; } = StreamGeometry.Parse("M2.475,2.025 h1.35 v4.95 h-1.35 Z M5.175,2.025 h1.35 v4.95 h-1.35 Z");

    /// <summary>The arrow's silver: the canvas's `silv2` gradient.</summary>
    public static IBrush Silver { get; } = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = new RelativePoint(0.6, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0xFB, 0xFB, 0xFC), 0),
            new GradientStop(Color.FromRgb(0xD9, 0xD9, 0xDE), 0.55),
            new GradientStop(Color.FromRgb(0x9C, 0x9C, 0xA4), 1),
        },
    }.ToImmutable();

    /// <summary>The pose to draw.</summary>
    public PointerPose Pose
    {
        get => pose;
        set
        {
            pose = value;
            Apply();
        }
    }

    /// <summary>Where this control's top-left is in the pose's coordinates (the pointer window's origin, monitor DIP).</summary>
    public Point Origin
    {
        get => origin;
        set
        {
            origin = value;
            Apply();
        }
    }

    /// <summary>The acting app's icon; null draws <see cref="BadgeLetter"/> on a neutral tile.</summary>
    public IImage? BadgeImage
    {
        get => (badge.Child as Image)?.Source;
        set
        {
            badge.Child = value is null ? badgeLetter : new Image { Source = value, Width = BadgeSize, Height = BadgeSize, Stretch = Stretch.UniformToFill };
            badge.IsVisible = value is not null || !string.IsNullOrEmpty(badgeLetter.Text);
        }
    }

    /// <summary>The app's first letter, drawn when it has no icon.</summary>
    public string? BadgeLetter
    {
        get => badgeLetter.Text;
        set
        {
            badgeLetter.Text = value;
            badge.IsVisible = badge.Child is Image || !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>The ripple ring, for tests and the frame checks.</summary>
    internal Ellipse Ripple => ripple;

    internal Border PauseBadge => pause;

    internal Path Glow => glow;

    internal Canvas Body => body;

    private void Apply()
    {
        var x = pose.Tip.X - origin.X;
        var y = pose.Tip.Y - origin.Y;
        bodyTransform.Matrix = Matrix.CreateScale(pose.Scale, pose.Scale) * Matrix.CreateTranslation(x, y);
        body.Opacity = pose.Opacity;
        rippleHostTransform.Matrix = Matrix.CreateTranslation(x, y);
        ripple.IsVisible = pose.Rippling;
        rippleScale.ScaleX = pose.RippleScale;
        rippleScale.ScaleY = pose.RippleScale;
        ripple.Opacity = pose.RippleOpacity;
        pause.IsVisible = pose.PauseBadge > 0;
        pauseScale.ScaleX = pose.PauseBadge;
        pauseScale.ScaleY = pose.PauseBadge;
        glow.Opacity = 0.75 * pose.Glow;
        glow.IsVisible = pose.Glow > 0;
    }
}
