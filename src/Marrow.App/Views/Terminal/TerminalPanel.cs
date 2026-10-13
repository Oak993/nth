using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.App.Views.Terminal.Port;
using Marrow.App.Window;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), :1-318.

namespace Marrow.App.Views.Terminal;

/// <summary>:4-111 The thread's terminal drawer.</summary>
public sealed class TerminalPanel : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly string directory;
    private readonly Border panelBorder;
    private readonly Grid surface;
    private readonly Grid content;
    private readonly StackPanel tabStrip;
    private readonly Border terminalArea;
    private readonly Splitter resizeHandle;
    private readonly SplitterValue resizeValue;
    private AppModel? model;
    private double? dragHeight;
    private bool closePending;

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

        // PORT-ONLY (web W4): the third column is the source's empty one (:25 the Spacer's place before the chevron); it holds the
        // drawer's "Move to the panes" button once the drawer is inside a pane workspace, and stays 0 wide before that.
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            Height = 38,
            Margin = new Thickness(10, 0),
            ColumnSpacing = 6,
            Background = Brushes.Transparent, // web W4: the empty header area takes the press that lifts the drawer (DESIGN 3.6)
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

        // PORT-ONLY (web W4, DESIGN 3.1, 3.6): the drawer can go to the pane grid. A drag of the header's empty area does it by
        // pointer (DrawerLiftGesture); this button is the way without one.
        var dock = MakeIconButton("rectangle.righthalf.inset.fill", UiText.Get("panes.terminal.move_to_panes"), () =>
            runtime.Workspace = runtime.Workspace.MoveTerminalToGrid());
        dock.IsVisible = false;
        Grid.SetColumn(dock, 2);
        header.Children.Add(dock);
        // Shown inside a pane workspace; enabled where the terminal then fits beside a readable chat (a narrower window would turn
        // the workspace into tabs, DESIGN 3.8), re-read when the workspace's size or the thread's workspace changes.
        PaneWorkspace? dockHost = null;
        void UpdateDock()
        {
            var host = this.FindAncestorOfType<PaneWorkspace>();
            dock.IsVisible = host is not null;
            dock.IsEnabled = host?.CanDockTerminalToGrid(runtime.Workspace) == true;
            if (ReferenceEquals(host, dockHost)) return;
            if (dockHost is not null) dockHost.SizeChanged -= OnDockHostResized;
            dockHost = host;
            if (host is not null) host.SizeChanged += OnDockHostResized;
        }
        void OnDockHostResized(object? sender, SizeChangedEventArgs e) => UpdateDock();
        AttachedToVisualTree += (_, _) => UpdateDock();
        DetachedFromVisualTree += (_, _) =>
        {
            if (dockHost is not null) dockHost.SizeChanged -= OnDockHostResized;
            dockHost = null;
        };
        this.Track(() => runtime.Workspace, _ => UpdateDock());
        DrawerLiftGesture.Attach(header, () => this.FindAncestorOfType<PaneWorkspace>(), () => this);

        // :52 .help("Hide terminal\(ShortcutStore.hint(for: .toggleTerminal))"): the chord is read live, so a remapped or
        // cleared Toggle terminal shortcut corrects the tooltip. :53 the accessibility label never carries it.
        var hide = MakeIconButton("chevron.down", "Hide terminal", () =>
            runtime.IsTerminalVisible = false);
        ToolTip.SetTip(hide, "Hide terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal));
        this.Track(() => ShortcutStore.Hint(AppShortcut.ToggleTerminal), hint => ToolTip.SetTip(hide, "Hide terminal" + hint));
        Grid.SetColumn(hide, 3);
        header.Children.Add(hide);

        terminalArea = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 6, 6),
        };

        content = new Grid
        {
            RowDefinitions = new RowDefinitions("38,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(header, 0);
        content.Children.Add(header);
        Grid.SetRow(terminalArea, 1);
        content.Children.Add(terminalArea);

        // DESIGN 4.3 (web W1, ED-NEW-web-w1-4): the drawer's top edge is the shared Splitter (it replaces
        // TerminalResizeHandle's 8-point DragHandle strip, :113-131). It is centred on the edge, half above the drawer and
        // half inside it, outside the rounded clip. The numbers are the source's: 140 to 760 (:89), 260 by default, saved to
        // the global terminalHeight on release (:91-96); released under 84 DIP the drawer closes (new).
        resizeValue = new SplitterValue(this, SplitterOrientation.Horizontal, ResizeLimits, new SplitterState(ResizeLimits.Default), ShowResize, CommitResize);
        resizeHandle = new Splitter(SplitterOrientation.Horizontal, new SplitterTarget
        {
            OnReport = resizeValue.Report,
            OnSettle = () =>
            {
                if (resizeValue.IsAnimating) resizeValue.Settle();
            },
            OnBegin = () =>
            {
                runtime.IsTerminalResizing = true; // :87
                resizeValue.Begin();
            },
            OnMove = resizeValue.Move,
            OnEnd = moved =>
            {
                resizeValue.End(moved);
                if (!closePending) runtime.IsTerminalResizing = false; // :95
            },
            OnReset = resizeValue.Reset,
            OnStep = resizeValue.Step,
            OnStepTo = resizeValue.StepTo,
            OnCollapse = resizeValue.Collapse,
            OnSetValue = resizeValue.SetValue,
        })
        {
            Label = UiText.Get("panes.terminal.resize"),
            CollapseSide = SplitterSide.Trailing,
            Height = Splitter.DividerThickness,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -Splitter.DividerThickness / 2, 0, 0),
        };

        surface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        surface.Children.Add(content);

        // :100-108 Rounded top corners; the panel reads as a sheet rising into the conversation.
        panelBorder = new Border
        {
            Child = surface,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(16, 16, 0, 0),
        };
        Child = new Panel { Children = { panelBorder, resizeHandle } };
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
        Math.Min(ResizeLimits.Maximum, Math.Max(ResizeLimits.Minimum, height)); // :89

    /// <summary>DESIGN 4.3: 140 · 260 · 760 (:89); released under 84 DIP (56 past the minimum) the drawer closes.</summary>
    public static SplitterLimits ResizeLimits => new(140, 760, 260)
    {
        Collapse = SplitterCollapse.BelowMinimum,
        CollapsedValue = 0,
        Growth = -1,
        CollapsedText = UiText.Get("panes.terminal.closed"),
    };

    /// <summary>The drawer's top-edge splitter.</summary>
    internal Splitter ResizeSplitter => resizeHandle;

    private void TrackPanelState()
    {
        this.Track(
            () =>
            {
                var current = AppEnvironment.GetAppModel(this);
                var sessions = current.Terminals.Sessions(runtime.ThreadID).ToArray();
                // :24-27 Only store membership/selection belong to the panel; each tab tracks its own session.
                return (current, sessions, current.Terminals.Selected(runtime.ThreadID));
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
                resizeValue.Load(new SplitterState(state.Item2)); // the stored height changed (here or in another window)
                ApplyHeight();
            });

        // A drawer closed by a drag and opened again before it left (the same panel comes back) opens at the stored height.
        this.Track(() => runtime.IsTerminalVisible, visible =>
        {
            if (!visible || model is null || resizeValue.Model.IsDragging) return;
            closePending = false;
            resizeValue.Load(new SplitterState(model.Settings.TerminalHeight));
            if (!resizeValue.IsAnimating) dragHeight = null;
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
            // :64-78 The empty state's VStack is framed to the whole area below the row: the leading 12, trailing 6 and
            // bottom 6 insets (:61-63) belong to the TerminalHost alone.
            terminalArea.Margin = new Thickness(0);
            terminalArea.Child = MakeEmptyState(current);
            return;
        }
        terminalArea.Margin = new Thickness(12, 0, 6, 6); // :61-63

        // :58-63 Keep and host the session's existing terminal view. Switching tabs reparents
        // this control rather than creating a replacement terminal.
        selected.ApplyAppearance(isDark: SwiftTheme.IsDark(this));
        var host = new TerminalHost(selected, runtime, () => runtime.IsTerminalVisible = false);
        terminalArea.Child = host;
    }

    /// <summary>
    /// :224-239 The drawer taken back while it is still closing (ChatView.RefreshTerminal through
    /// SwiftTransition.InsertContinuing) is the same panel, still in the visual tree, so its host's attach handler does not
    /// run again; the reopen's focus request (ThreadRuntime.swift:240-243) is spent here, on the host it shows. With no
    /// session there is no host, and the request stays pending as the Mac's does with no TerminalHost to update.
    /// </summary>
    internal void TakeKeyboardIfWanted() => (terminalArea.Child as TerminalHost)?.TakeKeyboardIfWanted();

    private Control MakeEmptyState(AppModel current) => EmptyState(this, current, runtime, directory);

    /// <summary>
    /// :64-78 The empty state: no shell is open in this thread. Shared with the terminal pane (web W4); <paramref name="owner"/>
    /// is the control whose theme the label's colour follows.
    /// </summary>
    internal static Control EmptyState(Control owner, AppModel current, ThreadRuntime runtime, string directory)
    {
        var label = new TextBlock
        {
            Text = "No shell open in this thread", // :68
            Foreground = Chrome.SecondaryText.Brush(owner), // :70
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        SwiftFont.Body.Apply(label);

        // :71-75 Button("New shell").buttonStyle(.glass).controlSize(.small), no .help: the glass capsule button
        // (it was a plain Button with an invented tooltip).
        var create = Marrow.App.Views.Chat.TimelineRows.GlassCommand("New shell",
            () => current.Terminals.Open(runtime.ThreadID, directory), prominent: false, small: true);
        create.HorizontalAlignment = HorizontalAlignment.Center;
        create.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(create, "New shell");

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
        return button.Chip(); // :41, :51 .buttonStyle(.chip)
    }

    private void ApplyHeight()
    {
        if (model is null) return;
        // :80 No load-time clamp.
        Height = dragHeight ?? model.Settings.TerminalHeight;
    }

    // :86-90 Every drawn height of a drag, a key or a reset (the splitter's spring) is the drawer's height until it lands on
    // the stored one; idle, the stored height is used verbatim (:80).
    private void ShowResize(double height)
    {
        dragHeight = height;
        ApplyHeight();
        if (resizeValue is null || resizeValue.Model.IsDragging || resizeValue.Model.Shown.IsMoving) return;
        if (closePending)
        {
            Close();
            return;
        }
        if (model is { } current && !resizeValue.Model.State.IsCollapsed && height == current.Settings.TerminalHeight) dragHeight = null;
    }

    // :91-96 A release (or a key) commits the height to the global terminalHeight key. Released collapsed, the drawer closes
    // once its spring has landed at nothing; the stored height stays the one it had before the drag.
    private void CommitResize(SplitterState state)
    {
        if (model is not { } current) return;
        if (state.IsCollapsed)
        {
            closePending = true;
            if (!resizeValue.Model.Shown.IsMoving) Close();
            return;
        }
        current.Settings.TerminalHeight = state.Value;
        if (!resizeValue.Model.Shown.IsMoving) dragHeight = null;
        ApplyHeight();
    }

    private void Close()
    {
        closePending = false;
        runtime.IsTerminalResizing = false;
        runtime.IsTerminalVisible = false;
    }

    internal static Color PanelColor(bool isDark) =>
        isDark ? Color.FromRgb(20, 20, 20) : Color.FromRgb(251, 251, 251); // :107

    private void UpdatePanelColor()
    {
        panelBorder.Background = new SolidColorBrush(PanelColor(SwiftTheme.IsDark(this)));
    }
}

/// <summary>:133-186 A terminal tab chip.</summary>
internal sealed class TerminalTab : Border
{
    private readonly TerminalSession session;
    private readonly bool isSelected;
    private bool isHovering;
    private readonly Button closeButton;
    private readonly Button restartButton;
    private readonly StackPanel row;
    private readonly TextBlock title;
    private readonly Action select;

    public TerminalTab(
        TerminalSession session,
        bool isSelected,
        Action select,
        Action restart,
        Action close)
    {
        this.session = session;
        this.isSelected = isSelected;
        this.select = select; // :177-179 the whole capsule selects, including its automation button

        row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        var terminalIcon = SwiftImage.SystemName("terminal");
        terminalIcon.Font(SwiftFont.Caption);
        terminalIcon.ForegroundStyle(isSelected ? SwiftColor.Primary : SwiftColor.Secondary); // :175
        row.Children.Add(terminalIcon);

        title = new TextBlock
        {
            Text = session.Title,
            MaxLines = 1, // :146 .lineLimit(1)
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.Callout.Apply(title); // :172 .font(.callout), inherited by the title
        title.Foreground = (isSelected ? SwiftColor.Primary : SwiftColor.Secondary).Brush(title); // :175
        row.Children.Add(title);
        // PORT-ONLY: DESIGN 5.6, ED-NEW-web-w9-3. The discovered address is an explicit Open preview gesture.
        var previewLabel = new TextBlock { FontSize = 11 };
        var preview = new Button { Content = previewLabel }.Chip();
        ToolTip.SetTip(preview, UiText.Get("panes.preview.open"));
        preview.Click += (_, e) => { session.OpenPreview?.Invoke(); e.Handled = true; };
        void PreviewReady(string? address)
        {
            if (address is null) { row.Children.Remove(preview); return; }
            previewLabel.Text = UiText.Format("panes.preview.ready", Marrow.Core.Web.WebPolicy.SourceName(address));
            AutomationProperties.SetName(preview, previewLabel.Text);
            if (!row.Children.Contains(preview)) row.Children.Insert(row.Children.IndexOf(closeButton), preview);
        }

        restartButton = IconAction("arrow.clockwise", "Start a new shell in this tab", restart);

        closeButton = IconAction("xmark", "Close terminal", close);
        AutomationProperties.SetName(closeButton, "Close " + session.Title);
        row.Children.Add(closeButton);
        PreviewReady(session.PreviewUrl); this.Track(() => session.PreviewUrl, PreviewReady);

        Child = row;
        Padding = new Thickness(10, 5);
        CornerRadius = new CornerRadius(999);
        Background = isSelected
            ? Chrome.PrimaryText.Opacity(0.08).Brush(this)
            : Brushes.Transparent;
        // :140,143-184 Keep @State hover on this tab when only its observable session changes.
        // Initial application also supports a chip constructed before it is attached to a window.
        UpdateSession(session.Title, session.IsRunning);
        this.Track(() => (session.Title, session.IsRunning), state => UpdateSession(state.Title, state.IsRunning));

        closeButton.Opacity = isSelected ? 1 : 0;
        closeButton.IsHitTestVisible = isSelected;

        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        // :178 .onTapGesture(perform: select): a tap completes when the pointer is lifted inside the capsule, so a press
        // that leaves the chip before its release selects nothing (the same port of a tap as HydraPanel's capsule).
        var pressed = false;
        PointerPressed += (_, e) => pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        PointerExited += (_, _) => pressed = false;
        PointerCaptureLost += (_, _) => pressed = false;
        PointerReleased += (_, _) =>
        {
            if (!pressed) return;
            pressed = false;
            select();
        };
    }

    // :146,148-158,166,180-184 Session updates redraw only this chip, preserving hover and control identity.
    private void UpdateSession(string sessionTitle, bool isRunning)
    {
        title.Text = sessionTitle;
        AutomationProperties.SetName(closeButton, "Close " + sessionTitle);
        AutomationProperties.SetName(restartButton, "Restart " + sessionTitle);
        if (isRunning)
            row.Children.Remove(restartButton);
        else if (!row.Children.Contains(restartButton))
            row.Children.Insert(2, restartButton);
        Opacity = isRunning ? 1 : 0.55;
        ToolTip.SetTip(this, isRunning ? sessionTitle : sessionTitle + " \U000000B7 the shell has exited");
        AutomationProperties.SetName(this, isRunning ? sessionTitle : sessionTitle + ", the shell has exited");
    }

    // :182-184 .accessibilityElement(children: .contain), button and optional selected traits.
    protected override AutomationPeer OnCreateAutomationPeer() => new TabPeer(this);

    private sealed class TabPeer(TerminalTab owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        // :182 .contain: expose the row's children instead of an unnamed layout-panel peer.
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => ((StackPanel)owner.Child!).Children
            .Select(CreatePeerForElement).OfType<AutomationPeer>().ToArray();
        bool ISelectionItemProvider.IsSelected => owner.isSelected;
        ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
        void ISelectionItemProvider.AddToSelection() => owner.select();
        void ISelectionItemProvider.RemoveFromSelection() { } // selecting another tab owns deselection
        void ISelectionItemProvider.Select() => owner.select();
        void IInvokeProvider.Invoke() => owner.select(); // :178 onTapGesture(perform: select)
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

/// <summary>:191-251 Hosts the long-lived terminal surface and its Windows key bridge.</summary>
public sealed class TerminalHost : Decorator
{
    private readonly TerminalSession session;
    private readonly ThreadRuntime runtime;
    private readonly Action? onEscapeAtPrompt;
    private bool escapeListenerAttached;
    private static int activeEscapeListenerCount;

    /// <summary>Test readback for PR-TERM-032: one scoped listener per attached host.</summary>
    internal static int ActiveEscapeListenerCount => activeEscapeListenerCount;

    /// <param name="onEscapeAtPrompt">
    /// What a bare Escape at the shell's prompt does (:214-221: the drawer closes). Null in a pane (web W4): a pane stays until it
    /// is closed, so Escape always reaches the shell.
    /// </param>
    public TerminalHost(TerminalSession session, ThreadRuntime runtime, Action? onEscapeAtPrompt)
    {
        this.session = session;
        this.runtime = runtime;
        this.onEscapeAtPrompt = onEscapeAtPrompt;

        // :351-353 Appearance is applied on host creation; TerminalSession ignores redundant values.
        session.ApplyAppearance(SwiftTheme.IsDark(this));

        // :225-234 attach(to:) re-parents the same long-lived SwiftTerm view into a container. The port's
        // TerminalHostView is that view and is never rebuilt while this session survives.
        Child = new TerminalHostContainer { HostedView = session.View };

        // :224-239 Give a newly opened terminal focus after it is attached to the visual tree, once, so keyboard input
        // goes to the terminal surface rather than the drawer chrome.
        AttachedToVisualTree += (_, _) =>
        {
            session.ApplyAppearance(SwiftTheme.IsDark(this));
            AttachEscapeListener();
            TakeKeyboardIfWanted();
        };
        DetachedFromVisualTree += (_, _) => DetachEscapeListener();

        // :59,225-227 `isDark: colorScheme == .dark` is an input of the host: a light/dark change reaches updateNSView, which
        // calls session.applyAppearance(isDark:) unconditionally (the session itself skips an unchanged value).
        ActualThemeVariantChanged += (_, _) => session.ApplyAppearance(SwiftTheme.IsDark(this));
    }

    /// <summary>
    /// :224-239 updateNSView spends runtime.terminalWantsFocus on any update of the host that is in its window, not only on
    /// its first attach: a terminal the reader just opened takes the keyboard, once. The attach handler and a drawer taken
    /// back while it is still closing (TerminalPanel.TakeKeyboardIfWanted; it never left the visual tree) both come here.
    /// :236's `let window = view.window else { return }` holds at both by construction: the attach handler runs in a tree, and
    /// ChatView observes isTerminalVisible only while it is attached (ViewLifetime.Track), so a host out of its window is
    /// never asked and the request stays pending until the host is attached (ChatViewParityRound5Tests).
    /// </summary>
    internal void TakeKeyboardIfWanted()
    {
        if (!runtime.TerminalWantsFocus) return;
        runtime.TerminalWantsFocus = false;
        Dispatcher.UIThread.Post(() => session.View.Focus(), DispatcherPriority.Input);
    }

    private void AttachEscapeListener()
    {
        if (escapeListenerAttached) return;
        session.View.KeyDown += HandleTerminalKeyDown;
        escapeListenerAttached = true;
        activeEscapeListenerCount++;
    }

    private void DetachEscapeListener()
    {
        if (!escapeListenerAttached) return;
        session.View.KeyDown -= HandleTerminalKeyDown;
        escapeListenerAttached = false;
        activeEscapeListenerCount--;
    }

    // :214-221 NSEvent localMonitor: only an unmodified Escape from the first-responder terminal view closes the
    // drawer when the shell owns the foreground. A foreground child receives the Escape instead.
    private void HandleTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var modifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;
        var bare = (e.KeyModifiers & modifiers) == KeyModifiers.None;
        if (bare && onEscapeAtPrompt is not null && session.View.IsFocused && session.IsRunning && TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId))
        {
            onEscapeAtPrompt();
            e.Handled = true;
            return;
        }

        // The source leaves a foreground program's Escape in the terminal event stream. The key-down route has
        // already reached this host, so feed the byte explicitly before swallowing the routed event.
        session.View.SendBytes([0x1B]);
        e.Handled = true;
    }
}

/// <summary>:257-318 Avalonia host for a session's existing terminal view.</summary>
public sealed class TerminalHostContainer : Decorator
{
    private CancellationTokenSource? pendingSync;
    private WindowLiveResize? liveResize;
    private PropertyChangedEventHandler? resizeStarted;
    private PropertyChangedEventHandler? resizeEnded;
    private bool wasLiveResizing;

    // PORT-ONLY (web W3b, DESIGN 3.7): the terminal is a content scope. The shell gets every key first (Ctrl+D is end of input,
    // Ctrl+Backslash is quit); nth takes only the reserved chords from here.
    public TerminalHostContainer() => ShortcutScopeHost.SetScope(this, ShortcutScope.Content);

    /// <summary>Actual registrations for the two will-start/did-end observers in Swift.</summary>
    internal int ResizeObserverCount => (resizeStarted is null ? 0 : 1) + (resizeEnded is null ? 0 : 1);

    /// <summary>Whether autoresizing is currently following the window frame.</summary>
    internal bool IsAutoResizing { get; private set; } = true;

    /// <summary>Whether the 50 ms resize coalescer has a pending callback.</summary>
    internal bool IsResizePending => pendingSync is not null;

    /// <summary>Number of frame synchronizations that changed the hosted surface.</summary>
    internal int SyncFrameCount { get; private set; }

    /// <summary>The timer boundary; tests observe the requested interval while still running the real delay.</summary>
    internal Func<TimeSpan, CancellationToken, Task> BeatDelay { get; set; } = Task.Delay;

    public Control? HostedView
    {
        get => Child;
        set
        {
            if (ReferenceEquals(Child, value)) return;
            // :244 attach(to:) starts with view.removeFromSuperview(): the long-lived view leaves whichever container
            // held it (a drawer that was hidden, or another thread's panel) before this one takes it.
            if (value?.Parent is Decorator previous && !ReferenceEquals(previous, this)) previous.Child = null;
            Child = value;
            if (value is not null)
            {
                value.HorizontalAlignment = HorizontalAlignment.Stretch;
                value.VerticalAlignment = VerticalAlignment.Stretch;
                if (value is TerminalHostView terminal)
                {
                    // The Swift view starts at 640x240, then attach(to:) immediately lets the container own its
                    // frame. Avalonia Width/Height would otherwise keep the initial size as a hard arrange constraint.
                    terminal.Width = double.NaN;
                    terminal.Height = double.NaN;
                }
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachResizeObservers();
        Dispatcher.UIThread.Post(() => SyncFrame(force: true), DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingSync();
        if (liveResize is { } oldResize)
        {
            if (resizeStarted is { } start) oldResize.PropertyChanged -= start;
            if (resizeEnded is { } end) oldResize.PropertyChanged -= end;
        }
        resizeStarted = null;
        resizeEnded = null;
        liveResize = null;
        wasLiveResizing = false;
        IsAutoResizing = true;
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachResizeObservers()
    {
        liveResize = ResolveLiveResize();
        if (liveResize is null) return;

        // The Swift view registers exactly two window notifications: willStartLiveResize and didEndLiveResize.
        var currentResize = liveResize;
        resizeStarted = (_, change) =>
        {
            if (!IsReshapingProperty(change.PropertyName) || !Reshaping(currentResize)) return;
            wasLiveResizing = true;
            IsAutoResizing = false;
        };
        resizeEnded = (_, change) =>
        {
            if (IsReshapingProperty(change.PropertyName) &&
                !Reshaping(currentResize) && wasLiveResizing) FinishResize();
        };
        currentResize.PropertyChanged += resizeStarted;
        currentResize.PropertyChanged += resizeEnded;
    }

    // PORT-ONLY (web W4): a pane gutter being dragged, or the chat column springing beside the terminal, reshapes the terminal like
    // a window resize does: its grid follows at the 50 ms beat, not once per frame (DESIGN 2.6, 4.2 "Live reflow").
    private static bool Reshaping(WindowLiveResize resize) => resize.IsActive || resize.IsWorkspaceResizing;

    private static bool IsReshapingProperty(string? name) =>
        name == nameof(WindowLiveResize.IsActive) || name == nameof(WindowLiveResize.IsWorkspaceResizing);

    private WindowLiveResize? ResolveLiveResize()
    {
        if (this.GetValue(AppEnvironment.WindowLiveResizeProperty) is { } attached) return attached;
        return TopLevel.GetTopLevel(this) is AvaloniaWindow window ? new WindowLiveResize(window) : null;
    }

    // :268-303 resizeSubviews: park the child frame for the duration of a Win32 size loop and coalesce frame changes
    // to one 50 ms sync while the loop continues.
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child) return finalSize;
        var active = liveResize is { } resize && Reshaping(resize);
        if (active)
        {
            wasLiveResizing = true;
            IsAutoResizing = false;
        }
        else if (wasLiveResizing)
        {
            // WindowLiveResize publishes through the main-actor observation queue. Reading the state here also keeps
            // the Win32 path correct when a native resize message and Avalonia's arrange pass share one turn.
            FinishResize();
        }
        var target = active && child.Bounds.Width > 0 && child.Bounds.Height > 0
            ? child.Bounds.Size
            : finalSize;
        var frameChanged = child.Bounds.Size != target;
        child.Arrange(new Rect(new Point(), target));
        if (active) ScheduleSync();
        else if (frameChanged && child is TerminalHostView terminal)
            terminal.ResizeTo(target);
        return finalSize;
    }

    private void ScheduleSync()
    {
        if (pendingSync is not null) return;
        var pending = new CancellationTokenSource();
        pendingSync = pending;
        _ = RunSyncAfterBeat(pending);
    }

    private async Task RunSyncAfterBeat(CancellationTokenSource pending)
    {
        try
        {
            await BeatDelay(TimeSpan.FromMilliseconds(50), pending.Token).ConfigureAwait(false);
            if (pending.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (pending.IsCancellationRequested || !ReferenceEquals(pendingSync, pending)) return;
                pendingSync = null;
                pending.Dispose();
                SyncFrame(force: false);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
    }

    // :305-312 finishResize: cancel the coalescer, restore width/height autoresizing, and sync exactly once.
    private void FinishResize()
    {
        wasLiveResizing = false;
        CancelPendingSync();
        IsAutoResizing = true;
        SyncFrame(force: false);
    }

    private void CancelPendingSync()
    {
        pendingSync?.Cancel();
        pendingSync?.Dispose();
        pendingSync = null;
    }

    // :313-318 syncFrame: the hosted view is moved to the container's current bounds, retaining the object itself.
    private void SyncFrame(bool force)
    {
        if (Child is not { } child || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = Bounds.Size;
        var changed = child.Bounds.Size != size;
        if (changed) child.Arrange(new Rect(new Point(), size));
        if (force || changed)
        {
            if (child is TerminalHostView terminal) terminal.ResizeTo(size);
            SyncFrameCount++;
        }
    }
}

/// <summary>Theme bridge used by this panel.</summary>
internal static class SwiftTheme
{
    public static bool IsDark(Control control) =>
        control.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
}
