using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B2 1fbf81e), :1-443.

namespace Marrow.App.Views.Terminal;

/// <summary>:4-112 The thread's terminal drawer.</summary>
public sealed class TerminalPanel : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly string directory;
    private readonly Border panelBorder;
    private readonly Grid surface;
    private readonly StackPanel content;
    private readonly StackPanel tabStrip;
    private readonly Border terminalArea;
    private readonly TerminalResizeHandle resizeHandle;
    private AppModel? model;
    private double? dragOrigin;
    private double? dragHeight;

    /// <param name="runtime">:7</param>
    /// <param name="directory">:8</param>
    // :15-110
    public TerminalPanel(ThreadRuntime runtime, string directory)
    {
        this.runtime = runtime;
        this.directory = directory;

        tabStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var scrollingTabs = new ScrollViewer
        {
            Content = tabStrip,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,0,Auto"),
            Height = 38,
            Margin = new Thickness(10, 0),
            ColumnSpacing = 6,
        };
        Grid.SetColumn(scrollingTabs, 0);
        header.Children.Add(scrollingTabs);

        var add = MakeIconButton("plus", "New terminal", () =>
        {
            if (model is { } current)
                current.Terminals.Open(runtime.ThreadID, directory);
        });
        Grid.SetColumn(add, 1);
        header.Children.Add(add);

        var hide = MakeIconButton("chevron.down", "Hide terminal", () =>
            runtime.IsTerminalVisible = false);
        Grid.SetColumn(hide, 3);
        header.Children.Add(hide);

        terminalArea = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 6, 6),
        };

        content = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        content.Children.Add(header);
        content.Children.Add(terminalArea);

        resizeHandle = new TerminalResizeHandle(
            begin: BeginResize,
            change: ChangeResize,
            end: EndResize)
        {
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 1,
        };

        surface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        surface.Children.Add(content);
        surface.Children.Add(resizeHandle);

        // :100-108 Rounded top corners; the panel reads as a sheet rising into the conversation.
        panelBorder = new Border
        {
            Child = surface,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(16, 16, 0, 0),
        };
        Child = panelBorder;
        UpdatePanelColor();
        ActualThemeVariantChanged += (_, _) => UpdatePanelColor();

        // :109 Register every tracker before attachment. Each reads the inherited AppModel
        // independently, so none depends on another attachment handler running first.
        this.Track(() => AppEnvironment.GetAppModel(this), attachedModel =>
        {
            model = attachedModel;
            attachedModel.Terminals.EnsureTerminal(runtime.ThreadID, directory);
        });
        TrackPanelState();

        // :80 Stored height is used verbatim; only a drag clamps it. dragHeight is a plain field
        // (not ObservationTracking-backed), so Track() on it would only fire once at attach; the
        // resize callbacks below call ApplyHeight() directly instead.
        ApplyHeight();
    }

    /// <summary>The drag-only clamp from the resize callback.</summary>
    public static double ClampDragHeight(double height) =>
        Math.Min(760, Math.Max(140, height)); // :89

    private void TrackPanelState()
    {
        this.Track(
            () =>
            {
                var current = AppEnvironment.GetAppModel(this);
                return (current, current.Terminals.Sessions(runtime.ThreadID).ToArray(),
                    current.Terminals.Selected(runtime.ThreadID));
            },
            state => RebuildBody(state.Item1, state.Item2, state.Item3));

        this.Track(
            () =>
            {
                var current = AppEnvironment.GetAppModel(this);
                return (current, current.Settings.TerminalHeight);
            },
            state =>
            {
                model = state.Item1;
                ApplyHeight();
            });
    }

    private void RebuildBody(
        AppModel current,
        TerminalSession[] sessions,
        TerminalSession? selected)
    {
        tabStrip.Children.Clear();

        foreach (var session in sessions)
        {
            var tab = new TerminalTab(
                session,
                session.Id == selected?.Id,
                () => current.Terminals.Selection =
                    current.Terminals.Selection.Mutating(map => map[runtime.ThreadID] = session.Id),
                () => current.Terminals.Restart(session),
                () => current.Terminals.Close(session));
            tabStrip.Children.Add(tab);
        }

        if (selected is null)
        {
            terminalArea.Child = MakeEmptyState(current);
            return;
        }

        // :58-63 Keep and host the session's existing terminal view. Switching tabs reparents
        // this control rather than creating a replacement terminal.
        selected.ApplyAppearance(isDark: SwiftTheme.IsDark(this));
        var host = new TerminalHost(selected, runtime, () => runtime.IsTerminalVisible = false);
        terminalArea.Child = host;
    }

    private Control MakeEmptyState(AppModel current)
    {
        var label = new TextBlock
        {
            Text = "No shell open in this thread", // :68
            Foreground = Chrome.SecondaryText.Brush(this), // :70
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        SwiftFont.Body.Apply(label);

        var create = new Button
        {
            Content = "New shell", // :71
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(create, "New shell");
        AutomationProperties.SetName(create, "New shell");
        create.Click += (_, _) => current.Terminals.Open(runtime.ThreadID, directory);

        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { label, create },
        };
    }

    private Button MakeIconButton(string symbol, string help, Action action)
    {
        var image = SwiftImage.SystemName(symbol);
        image.Font(Chrome.IconFont);
        var button = new Button
        {
            Content = image,
            Padding = new Thickness(9, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, help);
        AutomationProperties.SetName(button, help);
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplyHeight()
    {
        if (model is null) return;
        // :80 No load-time clamp.
        Height = dragHeight ?? model.Settings.TerminalHeight;
    }

    // :86-90
    private void BeginResize()
    {
        if (model is null) return;
        dragOrigin = model.Settings.TerminalHeight;
        runtime.IsTerminalResizing = true;
    }

    private void ChangeResize(double delta)
    {
        if (model is null) return;
        var origin = dragOrigin ?? model.Settings.TerminalHeight;
        dragHeight = ClampDragHeight(origin + delta);
        ApplyHeight();
    }

    // :91-96
    private void EndResize()
    {
        if (model is { } current && dragHeight is { } committed)
            current.Settings.TerminalHeight = committed;
        dragHeight = null;
        dragOrigin = null;
        runtime.IsTerminalResizing = false;
        ApplyHeight();
    }

    private static Color PanelColor(bool isDark) =>
        isDark ? Color.FromRgb(20, 20, 20) : Color.FromRgb(251, 251, 251); // :107

    private void UpdatePanelColor()
    {
        panelBorder.Background = new SolidColorBrush(PanelColor(SwiftTheme.IsDark(this)));
    }
}

/// <summary>:114-256 The drawer's top-edge resize strip.</summary>
public sealed class TerminalResizeHandle : Control
{
    private readonly Action begin;
    private readonly Action<double> change;
    private readonly Action end;
    private Point? pointerOrigin;
    private TopLevel? dragRoot;
    private bool isHovering;
    private bool isDragging;

    public TerminalResizeHandle(Action begin, Action<double> change, Action end)
    {
        this.begin = begin;
        this.change = change;
        this.end = end;

        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => FinishDrag();
        PointerEntered += (_, _) => { isHovering = true; InvalidateVisual(); };
        PointerExited += (_, _) => { isHovering = false; InvalidateVisual(); };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var shown = isHovering || isDragging;
        var width = shown ? 28 : 12;
        var opacity = shown ? 0.6 : 0;
        var bar = new Rect((Bounds.Width - width) / 2, (Bounds.Height - 2.5) / 2, width, 2.5);
        context.DrawRectangle(
            new SolidColorBrush(Chrome.PrimaryText.Resolve(ActualThemeVariant), opacity),
            null,
            bar,
            1.25,
            1.25);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragRoot = TopLevel.GetTopLevel(this);
        if (dragRoot is null) return;

        pointerOrigin = e.GetPosition(dragRoot);
        isDragging = true;
        begin();
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (pointerOrigin is not { } origin || dragRoot is null) return;
        var current = e.GetPosition(dragRoot);
        // DOM/Avalonia y increases downward; invert it so upward travel grows the drawer.
        change(origin.Y - current.Y);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (pointerOrigin is not { } origin || dragRoot is null) return;
        var current = e.GetPosition(dragRoot);
        change(origin.Y - current.Y);
        FinishDrag();
    }

    private void FinishDrag()
    {
        if (pointerOrigin is null) return;
        pointerOrigin = null;
        dragRoot = null;
        isDragging = false;
        end();
        InvalidateVisual();
    }
}

/// <summary>:258-311 A terminal tab chip.</summary>
internal sealed class TerminalTab : Border
{
    private readonly TerminalSession session;
    private readonly bool isSelected;
    private bool isHovering;
    private readonly Button closeButton;

    public TerminalTab(
        TerminalSession session,
        bool isSelected,
        Action select,
        Action restart,
        Action close)
    {
        this.session = session;
        this.isSelected = isSelected;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        var terminalIcon = SwiftImage.SystemName("terminal");
        terminalIcon.Font(SwiftFont.Caption);
        row.Children.Add(terminalIcon);

        var title = new TextBlock
        {
            Text = session.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(title);

        if (!session.IsRunning)
        {
            var restartButton = IconAction("arrow.clockwise", "Start a new shell in this tab", restart);
            AutomationProperties.SetName(restartButton, "Restart " + session.Title);
            row.Children.Add(restartButton);
        }

        closeButton = IconAction("xmark", "Close terminal", close);
        AutomationProperties.SetName(closeButton, "Close " + session.Title);
        row.Children.Add(closeButton);

        Child = row;
        Padding = new Thickness(10, 5);
        CornerRadius = new CornerRadius(999);
        Background = isSelected
            ? Chrome.PrimaryText.Opacity(0.08).Brush(this)
            : Brushes.Transparent;
        Opacity = session.IsRunning ? 1 : 0.55;
        ToolTip.SetTip(this, session.IsRunning
            ? session.Title
            : session.Title + " \U000000B7 the shell has exited"); // U+00B7 MIDDLE DOT
        AutomationProperties.SetName(this, session.IsRunning
            ? session.Title
            : session.Title + ", the shell has exited");

        closeButton.Opacity = isSelected ? 1 : 0;
        closeButton.IsHitTestVisible = isSelected;

        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                select();
        };
    }

    private void SetHover(bool hovering)
    {
        isHovering = hovering;
        closeButton.Opacity = isHovering || isSelected ? 1 : 0;
        closeButton.IsHitTestVisible = isHovering || isSelected;
    }

    private static Button IconAction(string symbol, string help, Action action)
    {
        var image = SwiftImage.SystemName(symbol);
        image.Font(SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold));
        image.ForegroundStyle(Chrome.SecondaryText);
        var button = new Button
        {
            Content = image,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, help);
        button.Click += (_, _) => action();
        return button;
    }
}

/// <summary>:313-376 Hosts the terminal's rendering surface. Marrow.App.Views.Terminal.Port.TerminalHost.cs
/// documents TerminalHostView as carrying no rendering yet: it starts the real shell over a pseudoconsole
/// and exposes the properties TerminalSession assigns, but the visible ANSI grid (SwiftTerm's Windows
/// equivalent) is "a future UI unit" (that file's own header comment, :1-5). This host can therefore only
/// place a placeholder where the real terminal surface will go; see the STOP note at the end of
/// TerminalPanelTests.cs.</summary>
public sealed class TerminalHost : Decorator
{
    public TerminalHost(TerminalSession session, ThreadRuntime runtime, Action onEscapeAtPrompt)
    {
        // :351-353 Appearance is applied on host creation; TerminalSession ignores redundant values.
        session.ApplyAppearance(SwiftTheme.IsDark(this));

        // TerminalHostView is not an Avalonia Control (Port/TerminalHost.cs:24-27): no visible ANSI grid
        // exists yet to embed here. A placeholder stands in until that unit lands.
        Child = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // :354-364 Give a newly opened terminal focus after it is attached to the visual tree, once the
        // real terminal surface exists to receive it. TerminalHostView has no Focus() equivalent yet.
        AttachedToVisualTree += (_, _) =>
        {
            if (!runtime.TerminalWantsFocus) return;
            runtime.TerminalWantsFocus = false;
            Focus();
        };

        // :339-346 Escape-at-prompt: TerminalSession.ForegroundJobIsTheShell (TerminalStore.cs:127-133) IS
        // implemented (ProcessDescendants.HasLiveDescendant), so that dependency is resolved. What blocks
        // this is the same missing terminal render surface: the Swift source reads the key from the SwiftTerm
        // NSView's own first-responder/key-monitor plumbing, which has no Windows equivalent to attach to
        // until a real terminal control exists. Wiring a KeyDown handler here now, with no real terminal
        // control beneath it to receive keys otherwise, would be inert and untestable. See the STOP note.
        _ = onEscapeAtPrompt;
    }
}

/// <summary>:382-443 Avalonia host for a session's existing terminal view.</summary>
public sealed class TerminalHostContainer : Decorator
{
    public Control? HostedView
    {
        get => Child;
        set => Child = value;
    }
}

/// <summary>Theme bridge used by this panel.</summary>
internal static class SwiftTheme
{
    public static bool IsDark(Control control) =>
        control.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
}