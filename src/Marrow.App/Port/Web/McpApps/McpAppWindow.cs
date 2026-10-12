// PORT-ONLY: web surfaces W19, an MCP App popped out of the thread (DESIGN.md 7.6 "Pop-out (the spec's pip): a small floating
// glass window, always on top if pinned, draggable by its title strip and resizable with Splitter edge and corner grips (min
// 280 x 180)"; canvas .popw; ED-NEW-web-w19-5). No Swift counterpart. The window holds the app's one host, under a 34 DIP
// title strip that names the server (the trust strip of 8.2, outside the web rectangle). Closing it brings the app back to its
// card in the thread. It opens pinned (on top), and the pin in the strip lets it fall behind other windows. Its size is the
// server's: the last size the user gave it. It appears with opacity and a .94 scale on a spring (a short ease under Reduce
// Motion) while the host shows its snapshot, because a window's alpha does not reach WebView2's surface (DESIGN 2.4).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port.Web.McpApps;

public sealed class McpAppWindow
{
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 260;
    public const double MinimumWidth = 280;
    public const double MinimumHeight = 180;
    public const double TitleHeight = 34;
    public const double ScreenMargin = 48;
    public const string CloseText = "Back inline";
    public const string PinText = "Keep on top";
    public const string UnpinText = "Stop keeping on top";

    private readonly McpAppSession session;
    private readonly SplitterSpring opacity = new(0, epsilon: 0.002);
    private readonly SplitterSpring scale = new(0.94, epsilon: 0.0005);
    private readonly Border frame;
    private readonly Button pin;
    private readonly PaneGlyph pinGlyph;
    private IDisposable? openHold;
    private Size? resizeStart;
    private PixelPoint? moveStart;
    private Action<TimeSpan>? pendingFrame;
    private TimeSpan? lastFrame;
    private bool quietly;

    internal McpAppWindow(McpAppSession session)
    {
        this.session = session;
        var (width, height) = session.WindowSize;
        Size = Fit(new Size(width, height), Largest());

        var glyph = new PaneGlyph(PaneGlyphKind.App, 13);
        var name = new TextBlock
        {
            Text = session.Server,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        pinGlyph = new PaneGlyph(PaneGlyphKind.Pin, 12);
        pin = StripButton(pinGlyph, UnpinText, TogglePin);
        var close = StripButton(new PaneGlyph(PaneGlyphKind.Close, 12), CloseText, () => session.WindowClosed(this));
        var strip = new Grid
        {
            Height = TitleHeight,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 0, 6, 0),
            Background = Brushes.Transparent, // the strip takes the pointer: it drags the window
            Children = { glyph, name, pin, close },
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(pin, 2);
        Grid.SetColumn(close, 3);
        strip.PointerPressed += OnStripPressed;
        AutomationProperties.SetName(strip, session.Server);

        var hairline = new Border { Height = 0.5, VerticalAlignment = VerticalAlignment.Bottom }; // the canvas's .wt border-bottom
        hairline.Background = PaneTokens.Line.Brush(hairline);
        var top = new Panel { Children = { strip, hairline } };

        Slot = new Decorator();
        var body = new Border { Margin = new Thickness(6, 0, 6, 6), CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = Slot }; // the canvas .mweb: under the hairline
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { top, body } };
        Grid.SetRow(body, 1);

        var grips = Grips();
        frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Child = new Grid { Children = { new WindowBackdrop(0.5), column, grips } },
            RenderTransformOrigin = RelativePoint.Center,
        };
        name.Foreground = PaneTokens.Text1.Brush(name);
        glyph.Foreground = PaneTokens.Text2.Brush(glyph);

        Window = new AvaloniaWindow
        {
            Title = session.Server + " · " + session.Title,
            Width = Size.Width,
            Height = Size.Height,
            MinWidth = MinimumWidth,
            MinHeight = MinimumHeight,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur],
            Background = Brushes.Transparent,
            Content = frame,
        };
        Window.Closed += (_, _) =>
        {
            StopMotion();
            openHold?.Dispose();
            openHold = null;
            if (!quietly) session.WindowClosed(this);
        };
        Window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            session.WindowClosed(this);
        };
        Pinned = true;
        ApplyPin();
    }

    /// <summary>The window (tests read its parts; nothing else shows it but <see cref="ShowNear"/>).</summary>
    public AvaloniaWindow Window { get; }

    /// <summary>Where the app's host goes.</summary>
    public Decorator Slot { get; }

    /// <summary>The window's size in DIP.</summary>
    public Size Size { get; private set; }

    /// <summary>True while the window stays above other windows.</summary>
    public bool Pinned { get; private set; }

    /// <summary>The pin button (tests).</summary>
    internal Button PinButton => pin;

    /// <summary>A size within the limits: at least 280 x 180, at most the work area less a margin.</summary>
    public static Size Fit(Size wanted, Size largest) => new(
        Math.Clamp(wanted.Width, MinimumWidth, Math.Max(MinimumWidth, largest.Width)),
        Math.Clamp(wanted.Height, MinimumHeight, Math.Max(MinimumHeight, largest.Height)));

    /// <summary>Shows the window beside the card it came from (or at the main window's right), springing in.</summary>
    public void ShowNear(Control? anchor)
    {
        if (anchor is not null && TopLevel.GetTopLevel(anchor) is { } top)
        {
            var origin = anchor.PointToScreen(new Point(anchor.Bounds.Width - Size.Width, 0));
            var scaling = top.RenderScaling;
            Window.Position = Clamp(origin, scaling);
        }

        frame.Opacity = 0;
        frame.RenderTransform = new ScaleTransform(scale.Value, scale.Value);
        openHold = session.Host.HoldSnapshot();
        Window.Show();
        var reduce = Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
        opacity.SetTarget(1, reduce);
        if (reduce) scale.Snap(1);
        else scale.SetTarget(1);
        RequestFrame();
    }

    /// <summary>Brings the window forward (the app asked for pip again).</summary>
    public void Activate() => Window.Activate();

    /// <summary>Closes the window without telling the session (the session moved the app itself).</summary>
    internal void CloseQuietly()
    {
        quietly = true;
        McpAppSession.TakeOut(Slot);
        Window.Close();
    }

    private void TogglePin()
    {
        Pinned = !Pinned;
        ApplyPin();
    }

    private void ApplyPin()
    {
        Window.Topmost = Pinned;
        var label = Pinned ? UnpinText : PinText;
        ToolTip.SetTip(pin, label);
        AutomationProperties.SetName(pin, label);
        pinGlyph.Foreground = (Pinned ? PaneTokens.Text1 : PaneTokens.Text3).Brush(pinGlyph);
    }

    private Button StripButton(PaneGlyph content, string label, Action invoke)
    {
        content.Foreground = PaneTokens.Text2.Brush(content);
        var face = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Child = content };
        var button = PlainControls.Button(face, invoke);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.OnHover(on => face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent);
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    // ---- moving: the title strip drags the window ----

    private void OnStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Window).Properties.IsLeftButtonPressed || e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        Window.BeginMoveDrag(e);
    }

    // ---- resizing: Splitter grips on the edges and corners ----

    private Grid Grips()
    {
        var grid = new Grid();
        var widthLabel = $"Resize the {session.Server} window width";
        var heightLabel = $"Resize the {session.Server} window height";
        var bothLabel = $"Resize the {session.Server} window";
        grid.Children.Add(Grip(SplitterOrientation.Vertical, -1, 0, widthLabel, HorizontalAlignment.Left, VerticalAlignment.Stretch, null));
        grid.Children.Add(Grip(SplitterOrientation.Vertical, 1, 0, widthLabel, HorizontalAlignment.Right, VerticalAlignment.Stretch, null));
        grid.Children.Add(Grip(SplitterOrientation.Horizontal, 0, 1, heightLabel, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, null));
        grid.Children.Add(Grip(SplitterOrientation.Both, -1, 1, bothLabel, HorizontalAlignment.Left, VerticalAlignment.Bottom, SplitterCorner.BottomLeft));
        grid.Children.Add(Grip(SplitterOrientation.Both, 1, 1, bothLabel, HorizontalAlignment.Right, VerticalAlignment.Bottom, SplitterCorner.BottomRight));
        return grid;
    }

    /// <summary>The grips (tests).</summary>
    internal IEnumerable<Splitter> Splitters => ((Grid)((Grid)frame.Child!).Children[2]).Children.OfType<Splitter>();

    private Splitter Grip(SplitterOrientation orientation, int across, int down, string label, HorizontalAlignment horizontal, VerticalAlignment vertical, SplitterCorner? corner)
    {
        var width = across != 0;
        var height = down != 0;
        var splitter = new Splitter(orientation, new SplitterTarget
        {
            OnReport = () => Report(height && !width),
            OnBegin = () =>
            {
                resizeStart = Size;
                moveStart = Window.Position;
            },
            OnMove = travel =>
            {
                var start = resizeStart ?? Size;
                Resize(new Size(start.Width + travel.X * across, start.Height + travel.Y * down), across < 0);
            },
            OnEnd = _ =>
            {
                resizeStart = null;
                moveStart = null;
                session.RememberWindowSize(Size.Width, Size.Height);
            },
            OnReset = () =>
            {
                Resize(new Size(DefaultWidth, DefaultHeight), false);
                session.RememberWindowSize(Size.Width, Size.Height);
            },
            OnStep = delta =>
            {
                Resize(new Size(Size.Width + delta.X * Math.Sign(across), Size.Height + delta.Y * down), false);
                session.RememberWindowSize(Size.Width, Size.Height);
            },
            OnStepTo = maximum =>
            {
                var limit = maximum ? Largest() : new Size(MinimumWidth, MinimumHeight);
                Resize(new Size(width ? limit.Width : Size.Width, height ? limit.Height : Size.Height), false);
                session.RememberWindowSize(Size.Width, Size.Height);
            },
            OnSetValue = width && height ? null : value =>
            {
                Resize(width ? new Size(value, Size.Height) : new Size(Size.Width, value), false);
                session.RememberWindowSize(Size.Width, Size.Height);
            },
        })
        {
            Label = label,
            HelpHint = "Double-click for the default size",
            MirrorsInRightToLeft = false,
            FineInset = 0,
            TracksInScreenSpace = true, // the window moves under the pointer as it grows from its left edge
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
        };
        if (corner is { } at)
        {
            splitter.Width = Splitter.CornerSize;
            splitter.Height = Splitter.CornerSize;
            splitter.SetCorner(at);
        }
        else if (orientation == SplitterOrientation.Vertical) splitter.Width = Splitter.EdgeThickness;
        else splitter.Height = Splitter.EdgeThickness;
        return splitter;
    }

    private SplitterReport Report(bool height)
    {
        var largest = Largest();
        var limits = height
            ? new SplitterLimits(MinimumHeight, Math.Max(MinimumHeight, largest.Height), Math.Min(DefaultHeight, largest.Height))
            : new SplitterLimits(MinimumWidth, Math.Max(MinimumWidth, largest.Width), Math.Min(DefaultWidth, largest.Width));
        return SplitterMath.Report(height ? Size.Height : Size.Width, false, limits);
    }

    /// <summary>Sets the window's size within its limits; growing from the left edge keeps the right edge where it stood.</summary>
    internal void Resize(Size wanted, bool fromLeft)
    {
        var next = Fit(wanted, Largest());
        if (fromLeft && moveStart is { } start && resizeStart is { } before)
        {
            var scaling = Window.RenderScaling > 0 ? Window.RenderScaling : 1;
            Window.Position = new PixelPoint(start.X + (int)Math.Round((before.Width - next.Width) * scaling), Window.Position.Y);
        }

        Size = next;
        Window.Width = next.Width;
        Window.Height = next.Height;
        session.UpdateContext(Slot);
    }

    private Size Largest()
    {
        var screen = Window?.Screens.ScreenFromWindow(Window) ?? Window?.Screens.Primary;
        if (screen is null) return new Size(1600, 1000);
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        return new Size(screen.WorkingArea.Width / scaling - ScreenMargin, screen.WorkingArea.Height / scaling - ScreenMargin);
    }

    private PixelPoint Clamp(PixelPoint origin, double scaling)
    {
        var screen = Window.Screens.ScreenFromPoint(origin) ?? Window.Screens.Primary;
        if (screen is null) return origin;
        var area = screen.WorkingArea;
        var w = (int)Math.Round(Size.Width * scaling);
        var h = (int)Math.Round(Size.Height * scaling);
        var margin = (int)Math.Round(ScreenMargin / 2 * scaling);
        return new PixelPoint(
            Math.Clamp(origin.X, area.X + margin, Math.Max(area.X + margin, area.Right - w - margin)),
            Math.Clamp(origin.Y, area.Y + margin, Math.Max(area.Y + margin, area.Bottom - h - margin)));
    }

    // ---- the open motion ----

    private void RequestFrame()
    {
        if (pendingFrame is not null) return;
        pendingFrame = OnFrame;
        FrameClock.Source.Current.RequestFrame(pendingFrame);
    }

    private void StopMotion()
    {
        if (pendingFrame is { } pending) FrameClock.Source.Current.CancelFrame(pending);
        pendingFrame = null;
        lastFrame = null;
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        var dt = lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.034) : 1.0 / 60;
        lastFrame = now;
        var moving = opacity.Step(dt) | scale.Step(dt);
        frame.Opacity = Math.Clamp(opacity.Value, 0, 1);
        frame.RenderTransform = new ScaleTransform(scale.Value, scale.Value);
        if (moving)
        {
            RequestFrame();
            return;
        }

        lastFrame = null;
        frame.Opacity = 1;
        frame.RenderTransform = null;
        openHold?.Dispose(); // landed: the app goes live in its window
        openHold = null;
    }

    /// <summary>Tests: runs the open motion to its end.</summary>
    internal void Settle()
    {
        StopMotion();
        opacity.Settle();
        scale.Settle();
        frame.Opacity = 1;
        frame.RenderTransform = null;
        openHold?.Dispose();
        openHold = null;
    }

    internal bool IsOpening => openHold is not null;
}
