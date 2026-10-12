// PORT-ONLY. DEVIATION OD-0151 (ED-NEW-settingsinapp-1): Settings opens INSIDE the main window, as a large glass panel over the app,
// instead of the source's separate Settings window (WindowManager.swift:16, :32-38, :80-84). The owner, 2026-10-11: "clicking outside of
// settings window should close it. and settings window shouldn't open a second window but rather be part of the app itself?" and "Like
// claude's native app does?". The panel is centred over the window's content, inset from the window's edges as Claude's own is (ED-NEW-settingsinapp-2,
// SettingsStage; clear of the caption buttons above it), the rest of the window is dimmed, and the app stays visible round it. It closes on a
// press outside it, Escape, its x button and the Settings shortcut (WindowManager.ToggleSettings). It opens as a popover does, scale 0.96 to 1
// with a fade over 0.25 s, and closes over 0.17 s (PopoverAppear.CloseSeconds); Reduce Motion fades only. It holds the window's keyboard while
// it shows: focus moves in (the search field), Tab cycles inside it, keys never reach the content under it, and focus goes back where it was.
// The content is SettingsView as it was: only its host changed. Its window-glass backdrop (Acrylic under a scrim) is replaced by the app's
// glass over the app (WindowBackdrop overAppContent, the scripts sheet's, ED-333), which frosts the window's own content under the panel.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

/// <summary>The Settings panel over the main window's content (see the file note).</summary>
internal sealed class SettingsOverlay : Panel
{
    // ED-NEW-settingsinapp-2: the panel's frame follows Claude's desktop app as the owner measured it (2026-10-11, 100 % scaling): a window
    // 1296 x 1125 keeps about 22 points beside the panel and about 50 above and below it (just under the caption buttons); a maximized
    // 2560 x 1393 window keeps it centred at about 1680 x 1262, with about 65 above and below. Between the two the room round the panel grows
    // smoothly (no step, so a live resize never jumps): the width follows the window less a fixed side margin up to WidthKnee, then eases
    // towards WidthCap; the vertical margin eases from MinimumVerticalMargin to LargeVerticalMargin. The content reflows; nothing scales.

    /// <summary>The least room kept beside the panel, left and right, in a small window.</summary>
    internal const double SideMargin = 22;

    /// <summary>The panel width up to which it follows the window less <see cref="SideMargin"/> each side.</summary>
    internal const double WidthKnee = 1250;

    /// <summary>The width the panel eases towards in a very wide window (it never reaches it).</summary>
    internal const double WidthCap = 1700;

    /// <summary>The least room kept above and below the panel: the window's caption buttons sit over the overlay, so it starts just below them.</summary>
    internal static double MinimumVerticalMargin => WindowChrome.CaptionTop + CaptionButton.ButtonHeight + 2;

    /// <summary>The room above and below the panel in a tall window.</summary>
    internal const double LargeVerticalMargin = 65;

    /// <summary>The heights between which the vertical margin eases from its least to its large value.</summary>
    internal const double ShortHeight = 1125, TallHeight = 1393;

    /// <summary>The room kept above and below the panel in a window <paramref name="height"/> pixels tall, its least clear of the caption
    /// buttons drawn at <paramref name="scale"/> (WindowChrome.SetCaptionScale).</summary>
    internal static double VerticalMargin(double height, double scale = 1)
    {
        var least = MinimumVerticalMargin * scale;
        var t = Math.Clamp((height - ShortHeight) / (TallHeight - ShortHeight), 0, 1);
        return Math.Max(least, least + (LargeVerticalMargin - least) * t * t * (3 - 2 * t));
    }

    /// <summary>The panel's width in a window <paramref name="width"/> wide: the window less the side margins, easing (with an unbroken slope) towards <see cref="WidthCap"/> past <see cref="WidthKnee"/>.</summary>
    internal static double PanelWidth(double width)
    {
        var room = Math.Max(0, width - 2 * SideMargin);
        if (room <= WidthKnee) return room;
        var span = WidthCap - WidthKnee;
        return WidthKnee + span * (1 - Math.Exp(-(room - WidthKnee) / span));
    }

    internal const double OpenSeconds = 0.25;

    internal const double CloseSeconds = 0.17; // PopoverAppear's close

    internal const double StartScale = 0.96;

    private readonly AvaloniaWindow window;
    private readonly Panel cardHost = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Action<TimeSpan> tick;
    private IInputElement? focusedBefore;
    private bool pressStartedOnDim;
    private bool transientWasNewer;
    private bool open;
    private double shown; // 0 closed, 1 open
    private double from;
    private bool opening;
    private ContinuousInstant start;
    private bool ticking;
    private Control? attachedSheetBefore;
    private Control? turnedRoot;

    public SettingsOverlay(AvaloniaWindow window, AppModel model, WindowLiveResize liveResize, Control content)
    {
        this.window = window;
        Content = content;
        View = content as SettingsView;
        Name = "SettingsOverlay";
        IsVisible = false;
        tick = Tick;

        // The dim over the rest of the window: a press there closes the panel. One strength in both appearances: the panel's glass
        // frosts what is drawn under it, dim included, so a dim that changed with the appearance would turn over as a product of
        // two easing layers under the panel's own, off the turnover's curve (ED-254).
        Dim = new Border { Name = "SettingsDim", Opacity = 0 };
        Dim.Background = SwiftColor.Black.Opacity(DimStrength).Brush(Dim);
        Dim.PointerPressed += OnDimPressed;
        Dim.PointerReleased += OnDimReleased;

        // The panel: the app's glass over the window's own content, the Settings content, and the close button at its top right.
        Backdrop = new WindowBackdrop(model.Settings.BackdropOpacity, overAppContent: true, roundedHairline: true);
        Backdrop.Track(() => model.Settings.BackdropOpacity, value => Backdrop.Update(value, null));
        if (View is { } view)
        {
            // The view's own backdrop is the window glass's (the desktop's Acrylic under a scrim): over the app the panel's backdrop stands in for it.
            view.RiseBackdrop.IsVisible = false;
            // The Settings window's drag region would drag the main window from inside the panel.
            foreach (var drag in view.GetLogicalDescendants().OfType<WindowDragArea>()) drag.IsHitTestVisible = false;
        }
        content.Width = double.NaN; // SettingsView sizes itself as the old window's 780 x 580; the panel sizes it now
        content.Height = double.NaN;
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        CloseButton = new ChromeCircleButton("xmark", "Close Settings", () => Close())
        {
            Name = "SettingsCloseButton",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
        };
        Card = new Border { Name = "SettingsCard", ClipToBounds = true, Child = new Panel { Children = { Backdrop, content, CloseButton } } }
            .RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);
        AutomationProperties.SetName(Card, "Settings");
        Shadow = new Border
        {
            Name = "SettingsCardShadow",
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(Chrome.WindowCornerRadius),
            BoxShadow = new BoxShadows(
                new BoxShadow { OffsetY = 24, Blur = 64, Color = Color.FromArgb(90, 0, 0, 0) },
                [new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(46, 0, 0, 0) }]),
        };
        cardHost.Children.Add(Shadow);
        cardHost.Children.Add(Card);
        cardHost.RenderTransformOrigin = RelativePoint.Center;
        cardHost.RenderTransform = scale;
        cardHost.Opacity = 0;
        KeyboardNavigation.SetTabNavigation(Card, KeyboardNavigationMode.Cycle);
        Stage = new SettingsStage(cardHost);
        // Every scroll area in Settings fades its content out at the edge it scrolls under (ScrollEdgeFade): the page under its title row, the sidebar, the lists inside a page.
        Stage.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
        {
            Setters =
            {
                new Setter(ScrollEdgeFade.IsEnabledProperty, true),
                new Setter(ScrollEdgeFade.TopProperty, TopFade),
                new Setter(ScrollEdgeFade.BottomProperty, BottomFade),
            },
        });
        // The zoom, the accent and the environment of a hosted window root, as the Settings window had (WindowManager.HostedRoot).
        var hosted = new HostedRoot(model, liveResize, Stage);
        // DEVIATION OD-0088 (ED-254), kept: an appearance change eases inside Settings over 0.32 s.
        AppearanceTurn.SetTurnsOver(this, true);
        SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, SwiftColor.LabelColor.Brush(this));
        Children.Add(Dim);
        Children.Add(hosted);
        window.AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The dim's black over the rest of the window, in either appearance.</summary>
    internal const double DimStrength = 0.3;

    /// <summary>The top fade of a Settings scroll area, the band its content dissolves in as it scrolls under what is above it.</summary>
    internal const double TopFade = 28;

    /// <summary>The bottom fade, softer: the content below the fold.</summary>
    internal const double BottomFade = 20;

    /// <summary>What Settings shows: <see cref="WindowManager.SettingsContent"/>'s control (SettingsView in the app).</summary>
    public Control Content { get; }

    /// <summary>The real Settings content, when it is SettingsView.</summary>
    public SettingsView? View { get; }

    public Border Dim { get; }

    public Control Card { get; }

    public Border Shadow { get; }

    public Control CloseButton { get; }

    public WindowBackdrop Backdrop { get; }

    internal SettingsStage Stage { get; }

    /// <summary>The window the panel is in.</summary>
    public AvaloniaWindow Window => window;

    /// <summary>Whether Settings is open (or opening): false from the moment a close starts.</summary>
    public bool IsOpen => open;

    /// <summary>How far the panel is shown, 0 to 1 (the open and close animate it).</summary>
    public double Shown => shown;

    /// <summary>Opens the panel (an open panel stays as it is).</summary>
    public void Open()
    {
        if (open) return;
        open = true;
        IsVisible = true;
        Dim.IsHitTestVisible = true;
        cardHost.IsHitTestVisible = true;
        focusedBefore = window.FocusManager?.GetFocusedElement() is { } focused && !IsInside(focused as Visual) ? focused : focusedBefore;
        WindowTransients.Show(this, this, () => Close());
        if (window is ThreadWindow thread)
        {
            attachedSheetBefore = thread.AttachedSheet;
            thread.AttachedSheet = Card; // the thread chords stand still under it (ThreadWindow.swift:12-14, :26)
            thread.OverlayKeys = OnKey;
        }
        AddHandler(KeyDownEvent, OnBubbledKey, RoutingStrategies.Bubble);
        TurnWindow(true);
        Animate(opening: true);
        TakeFocus();
    }

    /// <summary>Closes the panel; the content is kept, so it opens again where it was.</summary>
    public void Close()
    {
        if (!open) return;
        open = false;
        WindowTransients.Hide(this);
        RemoveHandler(KeyDownEvent, OnBubbledKey);
        if (window is ThreadWindow thread)
        {
            if (ReferenceEquals(thread.AttachedSheet, Card)) thread.AttachedSheet = attachedSheetBefore;
            if (thread.OverlayKeys == OnKey) thread.OverlayKeys = null;
        }
        attachedSheetBefore = null;
        Dim.IsHitTestVisible = false;
        cardHost.IsHitTestVisible = false;
        var back = focusedBefore;
        focusedBefore = null;
        if (back is Visual visual && visual.IsAttachedToVisualTree() && TopLevel.GetTopLevel(visual) == window) back.Focus();
        else if (IsInside(window.FocusManager?.GetFocusedElement() as Visual)) window.Focus();
        Animate(opening: false);
    }

    // The keyboard goes into the panel: the Settings search field, else the first control that takes it.
    private void TakeFocus()
    {
        bool Focus()
        {
            if (!open) return true;
            var field = View?.RiseSearch.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
            if (field is not null && field.IsEffectivelyVisible && field.Focus(NavigationMethod.Tab)) return true;
            var first = Card.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(e => e.Focusable && e.IsEffectivelyEnabled && e.IsEffectivelyVisible);
            return first?.Focus(NavigationMethod.Tab) == true;
        }
        if (!Focus()) Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    private bool IsInside(Visual? visual) => visual is not null && (ReferenceEquals(visual, this) || this.IsVisualAncestorOf(visual));

    // A press anywhere in the window, before anything in it sees it: whether a menu or popover was open over the panel then (that press closes
    // it, and only it).
    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!open) return;
        var newest = WindowTransients.Newest(window);
        transientWasNewer = newest is not null && !ReferenceEquals(newest, this);
    }

    private void OnDimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!open || !e.GetCurrentPoint(Dim).Properties.IsLeftButtonPressed) return;
        pressStartedOnDim = !transientWasNewer;
        e.Handled = true;
    }

    private void OnDimReleased(object? sender, PointerReleasedEventArgs e)
    {
        var started = pressStartedOnDim;
        pressStartedOnDim = false;
        e.Handled = true;
        if (started && open && e.InitialPressMouseButton == MouseButton.Left) Close();
    }

    /// <summary>
    /// The window's keys, before anything else in it sees them (ThreadWindow's class handler): Escape closes the panel unless a menu, popover or
    /// sheet is open over it (that one closes) or a text field with text holds the keyboard (it may use Escape; one that does not, closes the panel as the
    /// key comes back, OnBubbledKey); Ctrl+W closes it as it closed the Settings window (ThreadWindow.swift:58-64); a key whose target is the
    /// content under the panel never reaches it.
    /// </summary>
    internal bool OnKey(KeyEventArgs e)
    {
        if (!open || e.Handled) return false;
        var newest = WindowTransients.Newest(window);
        var over = newest is not null && !ReferenceEquals(newest, this);
        var modifiers = (ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers;
        if (e.Key == Key.Escape && modifiers == ChordModifiers.None)
        {
            if (SheetKeyboard.IsTaken(window)) return false;
            // A field being typed in keeps its Escape (ThreadWindow.swift:49-51, as the Settings window's). One in the panel with nothing in it
            // has nothing to undo, so Escape closes the panel: the search field holds the focus from the open and would otherwise take every
            // Escape (it empties itself and goes no further).
            if (window.FocusManager?.GetFocusedElement() is TextBox field && (!IsInside(field) || !string.IsNullOrEmpty(field.Text))) return false;
            // OD-0088 (ED-253), as the Settings window did: a popover or menu open over the panel closes first, and the panel stays.
            if (over) return WindowTransients.DismissNewest(window);
            Close();
            return true;
        }
        if (e.PhysicalKey == PhysicalKey.W && modifiers == ChordModifiers.Control && !over
            && (window is not ThreadWindow thread || ReferenceEquals(thread.AttachedSheet, Card))) // a sheet attached over it keeps the chord (ThreadWindow.swift:59)
        {
            Close();
            return true;
        }
        if (e.Source is Visual source && !IsInside(source) && Parent is Panel root
            && root.Children.TakeWhile(child => !ReferenceEquals(child, this)).Any(under => SheetKeyboard.Within(source, under)))
            return true;
        return false;
    }

    // An Escape a text field in the panel did not use closes it.
    private void OnBubbledKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !open || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        var newest = WindowTransients.Newest(window);
        if (newest is not null && !ReferenceEquals(newest, this)) return;
        e.Handled = true;
        Close();
    }

    // The open and the close: the dim and the panel fade together, the panel growing from 0.96 (Reduce Motion: the fade only).
    private void Animate(bool opening)
    {
        this.opening = opening;
        from = shown;
        if (!FrameClock.IsBound)
        {
            Apply(opening ? 1 : 0);
            Finish();
            return;
        }
        start = SwiftClock.Now;
        if (!ticking)
        {
            ticking = true;
            FrameClock.Source.Current.RequestFrame(tick);
        }
    }

    private void Tick(TimeSpan _)
    {
        ticking = false;
        var duration = opening ? OpenSeconds : CloseSeconds;
        var p = Math.Clamp((SwiftClock.Now - start).TotalSeconds / duration, 0, 1);
        if (opening)
        {
            var eased = 1 - Math.Pow(1 - p, 3); // ease-out
            Apply(from + (1 - from) * eased);
        }
        else
        {
            var eased = p * p * (3 - 2 * p); // ease-in-out
            Apply(from * (1 - eased));
        }
        if (p >= 1)
        {
            Finish();
            return;
        }
        ticking = true;
        FrameClock.Source.Current.RequestFrame(tick);
    }

    private void Apply(double value)
    {
        shown = value;
        Dim.Opacity = value;
        cardHost.Opacity = value;
        var size = EnvironmentValues.GetAccessibilityReduceMotion(this) ? 1 : StartScale + (1 - StartScale) * value;
        scale.ScaleX = size;
        scale.ScaleY = size;
    }

    private void Finish()
    {
        if (opening)
        {
            Apply(1);
            return;
        }
        Apply(0);
        IsVisible = false;
        TurnWindow(false);
    }

    // DEVIATION OD-0088 (ED-254), with the panel: the Settings window turned over as a whole. The panel is in the main window,
    // whose content shows dimmed around it and whose caption buttons lie over it, outside its tree, and the main window
    // changes appearance at once (ED-254). While the panel shows, the window's root (the panel's parent: the content, the
    // panel and the caption layer) turns over with it, its plain text held as the panel's is; once it has closed the window is
    // as it was before it opened.
    private void TurnWindow(bool on)
    {
        if (on)
        {
            if (turnedRoot is not null || Parent is not Control root || AppearanceTurn.GetTurnsOver(root)) return;
            AppearanceTurn.SetTurnsOver(root, true);
            root.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, SwiftColor.LabelColor.Brush(root));
            turnedRoot = root;
        }
        else if (turnedRoot is { } root)
        {
            root.ClearValue(AppearanceTurn.TurnsOverProperty);
            root.ClearValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty);
            turnedRoot = null;
        }
    }

    /// <summary>Lays the panel out: inset from the window as <see cref="PanelWidth"/> and <see cref="VerticalMargin"/> say, centred, clear of the caption buttons.</summary>
    /// <remarks>ED-NEW-settingsinapp-4: the owner measured the frame in the window's pixels, and the stage lays out in points drawn at the
    /// zoom (ZoomMotion's scale, AppZoom.BaseScale at 100 %, ED-NEW-textsize-2). So the frame is found in pixels and laid out in points: the
    /// room round the panel is the owner's at every zoom and through the zoom's motion, the content inside reflows at the zoom's sizes, and
    /// the least room above and below stays clear of the caption buttons, which are drawn at the same scale.</remarks>
    internal sealed class SettingsStage : Panel
    {
        private readonly Control card;

        public SettingsStage(Control card)
        {
            this.card = card;
            Children.Add(card);
        }

        /// <summary>The panel's size in pixels in a window of <paramref name="pixels"/>, its content drawn at <paramref name="scale"/>.</summary>
        internal static Size CardSize(Size pixels, double scale = 1)
        {
            return new Size(PanelWidth(pixels.Width), Math.Max(0, pixels.Height - 2 * VerticalMargin(pixels.Height, scale)));
        }

        /// <summary>The scale the stage is drawn at: its window root's zoom (1 where nothing zooms it).</summary>
        private double Scale => ZoomMotion.For(this)?.Value is { } scale && scale > 0 ? scale : 1;

        /// <summary>The panel's size in the stage's points, for <paramref name="space"/> points of it.</summary>
        private Size CardPoints(Size space)
        {
            var scale = Scale;
            var pixels = CardSize(new Size(space.Width * scale, space.Height * scale), scale);
            return new Size(pixels.Width / scale, pixels.Height / scale);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var scale = Scale;
            var space = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : WidthCap / scale,
                double.IsFinite(availableSize.Height) ? availableSize.Height : TallHeight / scale);
            card.Measure(CardPoints(space));
            return default; // the stage never sizes its host (the window's content does)
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = CardPoints(finalSize);
            card.Arrange(new Rect(new Point((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2), size));
            return finalSize;
        }
    }
}
