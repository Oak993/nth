using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/ShortcutsSettingsPage.swift (B3 52bc792), :1-676.
//
// Every key combination Droppy Code answers to: two recordable sections (Threads, Window and panels) followed by
// one fixed Composer section, one recorder per row (:4-16). DEVIATION OD-0008 B7 throughout: chords and modifier
// caps read in Windows notation (Ctrl, Alt, Shift, joined with "+"), never the macOS glyphs U+2303/U+2325/U+21E7/
// U+2318; Command and Control both become Control (ED-050/ED-099). Haptics.perform is a no-op shim per
// porting/port_impacts.md:434 entry 213 (X6): the same 9 call sites stay comparable, nothing substituted.

/// <summary>ShortcutsSettingsPage (:6-16). PR-SETTINGS-212.</summary>
public sealed class ShortcutsSettingsPage : Decorator
{
    public ShortcutsSettingsPage()
    {
        // :8 LazyVStack(alignment: .leading, spacing: Chrome.sectionSpacing). Avalonia has no lazy-stack primitive
        // (ChromeCard's own note says the same); rows are built eagerly, the fill-not-clip visual result reproduced.
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Chrome.SectionSpacing, // :8
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var section in SwiftEnum<AppShortcutSection>.AllCases) stack.Children.Add(new ShortcutSectionCard(section)); // :9-11
        stack.Children.Add(new FixedShortcutsCard()); // :12
        // :14 .focusEffectDisabled(). PlainControls.Button strips the themed template (its FuncControlTemplate draws
        // only the content), so nothing in this tree paints a focus ring to begin with; no further action is needed.
        Child = stack;
    }
}

/// <summary>ShortcutSectionHeader (:18-37). PR-SETTINGS-213, UI-SETTINGS-043.</summary>
public sealed class ShortcutSectionHeader : Decorator
{
    public ShortcutSectionHeader(string title, string symbol, Control? accessory)
    {
        var badge = new SidebarIconBadge(() => new SidebarSymbol(symbol)); // :25-27
        var text = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center }; // :28, :31; :24 alignment: .center
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(text); // :29
        text.Foreground = Chrome.SecondaryText.Brush(text); // :30
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8, // :24 HStack(alignment: .center, spacing: 8)
            VerticalAlignment = VerticalAlignment.Center,
            Children = { badge, text },
        };
        var grid = new Grid { Height = 22 }; // :35 .frame(height: 22)
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 8 }); // :32 Spacer(minLength: 8)
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        if (accessory is not null) // :33 accessory
        {
            accessory.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(accessory, 2);
            grid.Children.Add(accessory);
        }
        Child = grid;
    }
}

/// <summary>The section card's Reset accessory (:42-53). PR-SETTINGS-213, UI-SETTINGS-043.</summary>
public sealed class ShortcutResetPill : Decorator
{
    public ShortcutResetPill(AppShortcutSection section)
    {
        var store = ShortcutStore.Shared.Current;
        var label = new TextBlock { Text = "Reset", MaxLines = 1, TextWrapping = TextWrapping.NoWrap }; // :47
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(label); // :272
        label.Foreground = Chrome.PrimaryText.Brush(label); // :273
        var content = new Border
        {
            Child = label,
            Height = 22, // :275
            Padding = new Thickness(10, 0), // :274
        }.Capsule(RoundedCornerStyle.Continuous); // :276
        var background = new AnimatedColor(content);
        content.Background = background.Brush;
        var pressed = false;
        void Restyle() => background.Set(Chrome.Overlay(pressed ? 0.16 : 0.1), null); // :276
        Restyle();
        content.AddHandler(InputElement.PointerPressedEvent, (_, _) => { pressed = true; Restyle(); }, RoutingStrategies.Tunnel);
        content.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { pressed = false; Restyle(); }, RoutingStrategies.Tunnel);
        content.PointerCaptureLost += (_, _) => { pressed = false; Restyle(); };

        var button = PlainControls.Button(content, () => Transaction.WithAnimation(Chrome.Hover, () => store.Reset(section))); // :47-49
        AutomationProperties.SetName(button, "Reset");
        button.Opacity = store.HasCustomizations(section) ? 1 : 0; // :46 if store.hasCustomizations(in: section) { ... }.transition(.opacity)
        button.IsHitTestVisible = button.Opacity > 0;
        button.Transitions = [new Avalonia.Animation.DoubleTransition
        {
            Property = OpacityProperty,
            Duration = TimeSpan.FromSeconds(Chrome.Hover.Duration),
            Easing = Chrome.Hover.ToEasing(),
        }];
        this.Track(() => store.HasCustomizations(section), visible =>
        {
            button.Opacity = visible ? 1 : 0;
            button.IsHitTestVisible = visible;
        });
        Child = button;
    }
}

/// <summary>ShortcutSectionCard (:39-64). PR-SETTINGS-212, PR-SETTINGS-213.</summary>
public sealed class ShortcutSectionCard : Decorator
{
    public ShortcutSectionCard(AppShortcutSection section)
    {
        var header = new ShortcutSectionHeader(section.Title(), section.Symbol(), new ShortcutResetPill(section)); // :45-53
        var card = new ChromeCard(() => // :54-61
        {
            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            var shortcuts = section.Shortcuts();
            for (var i = 0; i < shortcuts.Count; i++)
            {
                if (i > 0) rows.Children.Add(ShortcutRowMetrics.Divider()); // :56-58
                rows.Children.Add(new ShortcutEntryRow(shortcuts[i])); // :59
            }
            return rows;
        });
        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Chrome.SectionHeaderSpacing, // :44
            Children = { header, card },
        };
    }
}

/// <summary>FixedShortcutsCard (:66-102). PR-SETTINGS-222, UI-SETTINGS-048.</summary>
public sealed class FixedShortcutsCard : Decorator
{
    // :68-75. DEVIATION OD-0008 B7: "Shift+Return", "Ctrl+Return" and "Ctrl+1–9" replace "⇧ Return",
    // "⌘ Return" and "⌘ 1–9"; "Return" and "↑" are unchanged.
    private static readonly (string Title, string Detail, string Chord)[] Rows =
    [
        ("Send", "Sends the message in the composer.", "Return"), // :69
        ("New line", "Adds a line without sending.", "Shift+Return"), // :70
        ("Steer a chat", "While a turn runs, stops it and sends right away.", "Return"), // :71
        ("Recall an earlier prompt", "Works in an empty composer.", "↑"), // :72
        ("Approve a request", "Allows what the agent asked for.", "Ctrl+Return"), // :73
        ("Jump to a thread", "Selects one of the first nine threads.", "Ctrl+1–9"), // :74 U+2013 EN DASH
    ];

    public FixedShortcutsCard()
    {
        var header = new ShortcutSectionHeader("Composer", "text.cursor", null); // :79-81 EmptyView()
        var card = new ChromeCard(() =>
        {
            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            for (var i = 0; i < Rows.Length; i++)
            {
                if (i > 0) rows.Children.Add(ShortcutRowMetrics.Divider()); // :84-86
                var (title, detail, chord) = Rows[i];
                rows.Children.Add(ShortcutRowMetrics.Row(title, detail, BuiltInChord(chord))); // :87-97
            }
            return rows;
        });
        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Chrome.SectionHeaderSpacing, // :78
            Children = { header, card },
        };
    }

    private static Control BuiltInChord(string chord) // :88-96
    {
        var text = new TextBlock { Text = chord, MaxLines = 1, TextWrapping = TextWrapping.NoWrap };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(text); // :89
        text.Foreground = Chrome.SecondaryText.Brush(text); // :90
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var plate = new Border
        {
            Child = text,
            Width = ShortcutRowMetrics.ControlWidth, // :91
            Height = ShortcutRowMetrics.ControlHeight, // :91
        }.RoundedRectangle(8, RoundedCornerStyle.Continuous); // :93-94
        plate.Background = Chrome.Overlay(0.07).Brush(plate); // :95
        ToolTip.SetTip(plate, "Built in"); // :96
        return plate;
    }
}

/// <summary>ShortcutRowMetrics (:104-110) and the row/divider factories every card shares.</summary>
public static class ShortcutRowMetrics
{
    public const double RowPadding = 12; // :105
    public const double DividerLeadingInset = 16; // :106
    public const double ControlSpacing = 16; // :107
    public const double ControlWidth = 168; // :108
    public const double ControlHeight = 24; // :109

    /// <summary>ShortcutSettingsRow body (:118-135): title left, one line, truncated tail with its detail as a
    /// tooltip; the accessory right-aligned, fixed size, never shrunk.</summary>
    public static Control Row(string title, string detail, Control accessory, string? mark = null) // PR-SETTINGS-214
    {
        var text = new TextBlock
        {
            Text = title, // :120
            MaxLines = 1, // :123
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, // :124
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.Callout.Apply(text); // :121
        text.Foreground = SwiftColor.Primary.Brush(text); // :122
        ToolTip.SetTip(text, detail); // :125
        accessory.Margin = new Thickness(ControlSpacing, 0, 0, 0); // :119 spacing: 16
        accessory.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid { MinHeight = 24 }; // :132
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Control leading = text;
        if (mark is not null)
        {
            // PORT-ONLY (web surfaces W10, ED-NEW-web-w10-4): the mark of a chord that works inside pages sits beside the title, where it is read, not only in the tooltip.
            var note = new TextBlock { Text = mark, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            SwiftFont.Caption.Apply(note);
            note.Foreground = SwiftColor.Secondary.Brush(note);
            var pair = new Grid { VerticalAlignment = VerticalAlignment.Center };
            pair.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            pair.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(note, 1);
            pair.Children.Add(text);
            pair.Children.Add(note);
            leading = pair;
        }
        Grid.SetColumn(leading, 0);
        Grid.SetColumn(accessory, 1);
        grid.Children.Add(leading);
        grid.Children.Add(accessory);
        return new Border { Child = grid, Padding = new Thickness(RowPadding) }; // :133
    }

    /// <summary>The plain <c>Divider()</c> between rows (:56-58, :84-86), inset 16 leading. Not ChromeDivider
    /// (Chrome.cs:541), which is a vertical bar used inside capsule groups: this is SwiftUI's own horizontal
    /// hairline, so the colour token is reused (Chrome.cs:327's 0.22 primary-text wash) but drawn full width.</summary>
    public static Control Divider()
    {
        var bar = new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(DividerLeadingInset, 0, 0, 0),
        };
        bar.Background = Chrome.PrimaryText.Opacity(0.22).Brush(bar);
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
        return bar;
    }
}

/// <summary>ShortcutEntryRow (:137-145).</summary>
public sealed class ShortcutEntryRow : Decorator
{
    /// <summary>The mark on a reserved chord (DESIGN 3.7): it works inside pages and the terminal.</summary>
    public static string WorksInsidePages => UiText.Get("settings.shortcuts.works_inside_pages");

    /// <summary>The same words as the visible mark beside the row's title (W10), without the sentence's full stop.</summary>
    public static string WorksInsidePagesMark => UiText.Get("settings.shortcuts.works_inside_pages_mark");

    public ShortcutEntryRow(AppShortcut shortcut)
    {
        // PORT-ONLY (web W3b, DESIGN 3.7): a chord nth takes even from a web page or the terminal says so.
        var detail = shortcut.WorksInsidePages() ? UiText.Format("settings.shortcuts.detail_works_inside_pages", shortcut.Detail()) : shortcut.Detail();
        Child = ShortcutRowMetrics.Row(shortcut.Title(), detail, new ShortcutRecorder(shortcut), shortcut.WorksInsidePages() ? WorksInsidePagesMark : null);
    }
}

// MARK: - Recorder

/// <summary>ShortcutRecorder (:151-223). PR-SETTINGS-215, PR-SETTINGS-216, PR-SETTINGS-217, PR-SETTINGS-220,
/// UI-SETTINGS-044.</summary>
public sealed class ShortcutRecorder : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isPresenting; // :155

        public bool IsPresenting { get => Read(isPresenting); set => Set(ref isPresenting, value); }
    }

    public ShortcutRecorder(AppShortcut shortcut)
    {
        var store = ShortcutStore.Shared.Current;
        var session = new ShortcutRecordingSession();
        var state = new ViewState();

        var label = new TextBlock { MaxLines = 1, TextWrapping = TextWrapping.NoWrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var content = new Panel { Width = ShortcutRowMetrics.ControlWidth, Height = ShortcutRowMetrics.ControlHeight, Children = { label } }; // :181
        var background = new AnimatedColor(content);
        var foreground = new AnimatedColor(label);
        label.Foreground = foreground.Brush;
        var scale = new ScaleTransform(1, 1);
        var border = new Border { Child = content, RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center }
            .RoundedRectangle(8, RoundedCornerStyle.Continuous); // :255
        border.Background = background.Brush;
        scale.Transitions =
        [
            new Avalonia.Animation.DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromSeconds(0.08), Easing = Animation.EaseOut(0.08).ToEasing() }, // :261
            new Avalonia.Animation.DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromSeconds(0.08), Easing = Animation.EaseOut(0.08).ToEasing() },
        ];
        AutomationProperties.SetName(border, string.Empty); // set per chord below (:173-174)

        var isPressed = false;
        var isHovering = false;

        void Restyle(KeyChord? chord)
        {
            var isSet = chord is not null;
            var isRecording = state.IsPresenting;
            SwiftColor fill = isRecording
                ? Chrome.Accent.Opacity(isPressed ? 0.34 : 0.26) // :244
                : isSet
                    ? Chrome.Overlay(isPressed ? 0.34 : isHovering ? 0.30 : 0.24) // :245
                    : Chrome.Overlay(isPressed ? 0.14 : isHovering ? 0.11 : 0.07); // :246
            var animation = isPressed ? Animation.EaseOut(0.08) : Transaction.Current ?? Chrome.Hover;
            background.Set(fill, animation); // :185,261-263 chord changes carry the outer spring
            var ink = !border.IsEnabled
                ? Chrome.SecondaryText.Opacity(0.6) // :250
                : isSet || isRecording ? Chrome.PrimaryText : Chrome.SecondaryText; // :251
            foreground.Set(ink, animation);
            scale.ScaleX = scale.ScaleY = isPressed ? 0.97 : 1; // :260

            if (chord is { } c) // :164-174
            {
                label.Text = c.Description;
                SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(label); // :168
                AutomationProperties.SetName(border, c.Description); // :174
            }
            else // :175-179
            {
                label.Text = "Record shortcut";
                SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Apply(label); // :177
                AutomationProperties.SetName(border, "Record shortcut");
            }
        }

        void Refresh() => Restyle(store.Chord(shortcut));
        Refresh();

        border.AddHandler(InputElement.PointerPressedEvent, (_, _) => { isPressed = true; Restyle(store.Chord(shortcut)); }, RoutingStrategies.Tunnel);
        border.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { isPressed = false; Restyle(store.Chord(shortcut)); }, RoutingStrategies.Tunnel);
        border.PointerCaptureLost += (_, _) => { isPressed = false; Restyle(store.Chord(shortcut)); };
        border.OnHover(hovering => { isHovering = hovering; Transaction.WithAnimation(Chrome.Hover, () => Restyle(store.Chord(shortcut))); }); // :264

        var button = PlainControls.Button(border, () => state.IsPresenting = !state.IsPresenting); // :160-162

        List<ShortcutRecordingAction> Actions(KeyChord? current) // :204-222
        {
            var actions = new List<ShortcutRecordingAction>();
            if (current is not null)
            {
                actions.Add(new ShortcutRecordingAction("clear", "Clear", true, () => // :208-212
                {
                    Haptics.Perform(HapticPattern.Generic);
                    store.Set(null, shortcut);
                    state.IsPresenting = false;
                }));
            }
            if (store.IsCustomized(shortcut) && shortcut.DefaultChord() is not null) // :214
            {
                actions.Add(new ShortcutRecordingAction("reset", "Reset to default", false, () => // :215-219
                {
                    Haptics.Perform(HapticPattern.Generic);
                    store.Reset(shortcut);
                    state.IsPresenting = false;
                }));
            }
            return actions;
        }

        var popup = PlainControls.Popover(
            button,
            new Binding<bool>(() => state.IsPresenting, value => state.IsPresenting = value),
            () => new ShortcutRecordingPopoverContent(session)); // :186-189
        popup.Placement = PlacementMode.Top; // :186 arrowEdge: .top

        var input = new ShortcutInputSource(border); // PORT-ONLY: :545 the OS-level NSEvent monitor's Windows counterpart.

        button.Track(() => state.IsPresenting, presenting => // :190-201
        {
            if (presenting)
            {
                input.Attach(session);
                session.Begin(shortcut, Actions(store.Chord(shortcut)), input.CurrentModifiers(), () => state.IsPresenting = false, captured => // :192-196
                {
                    store.Set(captured, shortcut);
                });
            }
            else
            {
                input.Detach();
                session.Cancel(); // :198
            }
            Refresh();
        });
        border.DetachedFromVisualTree += (_, _) => { input.Detach(); session.Cancel(); }; // :201 .onDisappear
        this.Track(() => store.Chord(shortcut), _ =>
            Transaction.WithAnimation(Animation.Spring(0.3, 0.8, 0), Refresh)); // :185

        Child = new Panel { Children = { button, popup } };
    }
}

/// <summary>PORT-ONLY: the Windows counterpart of the session's local NSEvent monitor and resign observer
/// (:545-562, windows_path). A window-level capture-phase key listener with preventDefault on every keydown,
/// keyup and modifier change while the popover is open, plus a blur handler that cancels. Ctrl, Alt and Shift
/// going down or up are read as flagsChanged (there is no discrete Windows modifier-key-down event otherwise);
/// every other key is keyDown/keyUp. Bare Escape cancels (:600-603); Escape with a modifier is an ordinary key
/// (port_impacts.md:424 entry 208: one of five separate Escape policies in the family, decided per site).</summary>
public sealed class ShortcutInputSource
{
    private const long EscapeKeyCode = 53; // :600, kept as the session's own sentinel (never reached from Windows key translation; the Escape key itself is filtered before HandleKeyDown is called, see HandleKey below).

    private readonly Control anchor;
    private ShortcutRecordingSession? session;
    private TopLevel? topLevel;
    private readonly HashSet<Key> heldKeys = [];

    public ShortcutInputSource(Control anchor) => this.anchor = anchor;

    public ChordModifiers CurrentModifiers()
    {
        return ToChordModifiers(CurrentKeyModifiers());
    }

    public void Attach(ShortcutRecordingSession target)
    {
        session = target;
        topLevel = TopLevel.GetTopLevel(anchor);
        if (topLevel is null) return;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (topLevel is Avalonia.Controls.Window window) window.Deactivated += OnDeactivated; // :555-561 didResignActiveNotification
    }

    public void Detach()
    {
        if (topLevel is { } t)
        {
            t.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            t.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            if (t is Avalonia.Controls.Window w) w.Deactivated -= OnDeactivated;
        }
        topLevel = null;
        session = null;
        heldKeys.Clear();
    }

    private void OnDeactivated(object? sender, EventArgs e) => session?.Cancel(); // :559-561

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (session is null) return;
        var isModifierKey = IsModifierKey(e.Key);
        if (isModifierKey)
        {
            session.HandleFlagsChanged(ToChordModifiers(e.KeyModifiers)); // :583-594 flagsChanged
        }
        else
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { session.Cancel(); e.Handled = true; return; } // :600-603
            var isRepeat = !heldKeys.Add(e.Key); // :597 event.isARepeat
            if (KeyCode(e.PhysicalKey) is { } code)
                session.HandleKeyDown(code, ToChordModifiers(e.KeyModifiers), isRepeat);
        }
        e.Handled = true; // NSEvent.addLocalMonitorForEvents returning nil for every handled event
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (session is null) return;
        if (IsModifierKey(e.Key)) session.HandleFlagsChanged(ToChordModifiers(e.KeyModifiers));
        else
        {
            heldKeys.Remove(e.Key);
            if (KeyCode(e.PhysicalKey) is { } code) session.HandleKeyUp(code);
        }
        e.Handled = true;
    }

    // PORT-ONLY, ENGINEERING DECISION: Avalonia exposes no cross-platform "currently held modifiers" query outside
    // an event; the session's held-modifiers seed (:541 NSEvent.modifierFlags) is approximated as none at
    // popover-open and corrected by the first keyDown/flagsChanged the input source delivers.
    private KeyModifiers CurrentKeyModifiers() => KeyModifiers.None;

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    // :598 NSEvent.keyCode -> Windows Set-1 physical scan code (ED-099).
    // The table is shared in meaning with MenuKeyEquivalents, copied here to keep the unit's edit scope.
    private static long? KeyCode(PhysicalKey key) => key switch
    {
        PhysicalKey.A => 0x1E,
        PhysicalKey.S => 0x1F,
        PhysicalKey.D => 0x20,
        PhysicalKey.F => 0x21,
        PhysicalKey.H => 0x23,
        PhysicalKey.G => 0x22,
        PhysicalKey.Z => 0x2C,
        PhysicalKey.X => 0x2D,
        PhysicalKey.C => 0x2E,
        PhysicalKey.V => 0x2F,
        PhysicalKey.B => 0x30,
        PhysicalKey.Q => 0x10,
        PhysicalKey.W => 0x11,
        PhysicalKey.E => 0x12,
        PhysicalKey.R => 0x13,
        PhysicalKey.Y => 0x15,
        PhysicalKey.T => 0x14,
        PhysicalKey.Digit1 => 0x02,
        PhysicalKey.Digit2 => 0x03,
        PhysicalKey.Digit3 => 0x04,
        PhysicalKey.Digit4 => 0x05,
        PhysicalKey.Digit6 => 0x07,
        PhysicalKey.Digit5 => 0x06,
        PhysicalKey.Equal => 0x0D,
        PhysicalKey.Digit9 => 0x0A,
        PhysicalKey.Digit7 => 0x08,
        PhysicalKey.Minus => 0x0C,
        PhysicalKey.Digit8 => 0x09,
        PhysicalKey.Digit0 => 0x0B,
        PhysicalKey.BracketRight => 0x1B,
        PhysicalKey.O => 0x18,
        PhysicalKey.U => 0x16,
        PhysicalKey.BracketLeft => 0x1A,
        PhysicalKey.I => 0x17,
        PhysicalKey.P => 0x19,
        PhysicalKey.L => 0x26,
        PhysicalKey.J => 0x24,
        PhysicalKey.Quote => 0x28,
        PhysicalKey.K => 0x25,
        PhysicalKey.Semicolon => 0x27,
        PhysicalKey.Backslash => 0x2B,
        PhysicalKey.Comma => 0x33,
        PhysicalKey.Slash => 0x35,
        PhysicalKey.N => 0x31,
        PhysicalKey.M => 0x32,
        PhysicalKey.Period => 0x34,
        PhysicalKey.Backquote => 0x29,
        PhysicalKey.Enter => 0x1C, // Return
        PhysicalKey.Tab => 0x0F,
        PhysicalKey.Space => 0x39,
        PhysicalKey.Backspace => 0x0E, // macOS Delete (51)
        PhysicalKey.Escape => 0x01,
        PhysicalKey.Delete => 0xE053, // macOS forward delete (117)
        PhysicalKey.ArrowLeft => 0xE04B,
        PhysicalKey.ArrowRight => 0xE04D,
        PhysicalKey.ArrowDown => 0xE050,
        PhysicalKey.ArrowUp => 0xE048,
        PhysicalKey.Home => 0xE047,
        PhysicalKey.End => 0xE04F,
        PhysicalKey.PageUp => 0xE049,
        PhysicalKey.PageDown => 0xE051,
        PhysicalKey.F1 => 0x3B,
        PhysicalKey.F2 => 0x3C,
        PhysicalKey.F3 => 0x3D,
        PhysicalKey.F4 => 0x3E,
        PhysicalKey.F5 => 0x3F,
        PhysicalKey.F6 => 0x40,
        PhysicalKey.F7 => 0x41,
        PhysicalKey.F8 => 0x42,
        PhysicalKey.F9 => 0x43,
        PhysicalKey.F10 => 0x44,
        PhysicalKey.F11 => 0x57,
        PhysicalKey.F12 => 0x58,
        _ => null,
    };

    private static ChordModifiers ToChordModifiers(KeyModifiers modifiers)
    {
        var result = ChordModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= ChordModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= ChordModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= ChordModifiers.Shift;
        return result;
    }
}

/// <summary>The recorder's ButtonStyle fill/ink ladder is inlined into ShortcutRecorder.Restyle above (:225-266);
/// this type exists only so the source's own type name (ShortcutCombinedControlStyle) is greppable.</summary>
public static class ShortcutCombinedControlStyle
{
    // Source: DroppyCode/App/Views/Settings/ShortcutsSettingsPage.swift:226-266 (B2 1fbf81e). See ShortcutRecorder.Restyle.
}

// MARK: - Keycaps

public enum KeycapPhase { Pressed, Ghost } // :284-287

/// <summary>Keycap (:283-315). PR-SETTINGS-221, UI-SETTINGS-045.</summary>
public sealed record Keycap(string Id, string Symbol, string? Name, KeycapPhase Phase)
{
    public bool IsModifier => Name is not null; // :294

    // :296-303 modifierCaps(for:phase:). DEVIATION OD-0008 B7: Ctrl, Alt, Shift replace the fixed macOS order
    // control/option/shift/command (Command merges into Control, ED-050, so a fourth cap never appears). The exact
    // per-cap symbol and name are UNRESOLVED (UI-SETTINGS-045 notes): "Ctrl"/"ctrl" etc. are the nearest match to
    // Core.Models.Shortcuts.KeyNames.ModifierSymbols' own wording, kept in the source's symbol-over-name layout.
    public static IReadOnlyList<Keycap> ModifierCaps(ChordModifiers flags, KeycapPhase phase)
    {
        var caps = new List<Keycap>();
        if (flags.HasFlag(ChordModifiers.Control)) caps.Add(new Keycap("control", "Ctrl", "ctrl", phase)); // :298
        if (flags.HasFlag(ChordModifiers.Alt)) caps.Add(new Keycap("option", "Alt", "alt", phase)); // :299
        if (flags.HasFlag(ChordModifiers.Shift)) caps.Add(new Keycap("shift", "Shift", "shift", phase)); // :300
        return caps;
    }

    public static Keycap KeyCap(long keyCode, KeycapPhase phase) => new("key", KeyNames.Label(keyCode), null, phase); // :305-307

    public static IReadOnlyList<Keycap> CapsFor(KeyChord chord) => // :309-311
        [.. ModifierCaps(chord.ModifierFlags, KeycapPhase.Pressed), KeyCap(chord.KeyCode, KeycapPhase.Pressed)];

    public static Keycap GhostCommand { get; } = new("control", "Ctrl", "ctrl", KeycapPhase.Ghost); // :313, DEVIATION OD-0008 B7 (was "command"/"⌘"/"cmd")

    public static Keycap GhostKey { get; } = new("key", "A", null, KeycapPhase.Ghost); // :314
}

/// <summary>KeycapTint (:317-324). Fixed colours regardless of theme: rgb(0.94, 0.43, 0.46) rejected, rgb(0.30,
/// 0.84, 0.45) accepted.</summary>
public enum KeycapTint { Neutral, Rejected, Accepted }

public static class KeycapColors
{
    public static SwiftColor Rejected { get; } = new(0.94, 0.43, 0.46); // :323
    public static SwiftColor Accepted { get; } = new(0.30, 0.84, 0.45); // :323
}

/// <summary>KeycapView (:326-417). PR-SETTINGS-221, UI-SETTINGS-045: a dark key that sits down when pressed, with
/// a lip that lights in the verdict's colour.</summary>
public sealed class KeycapView : Decorator
{
    private const double CapHeight = 44, ModifierWidth = 64, KeyMinWidth = 38, Radius = 8, KeyTravel = 2;

    private Keycap cap;
    private KeycapTint tint;
    private readonly Border faceLayer;
    private readonly Border lip;
    private readonly Control label;
    private readonly TranslateTransform faceOffset = new();
    private readonly TranslateTransform labelOffset = new();
    private readonly List<AnimatedColor> inks = [];
    private readonly AnimatedColor lipColorAnimation;

    public KeycapView(Keycap cap, KeycapTint tint)
    {
        this.cap = cap;
        this.tint = tint;

        var baseLayer = new Border { Height = CapHeight }.RoundedRectangle(Radius, RoundedCornerStyle.Continuous); // :353
        baseLayer.Background = new SwiftColor(0.07).Brush(baseLayer);
        baseLayer.RenderTransform = new TranslateTransform(0, KeyTravel); // :354 .offset(y: keyTravel)

        faceLayer = new Border { RenderTransform = faceOffset }.RoundedRectangle(Radius, RoundedCornerStyle.Continuous); // :355-362, :370
        faceLayer.Background = new LinearGradientBrush // :356
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(new SwiftColor(0.225).Resolve(Avalonia.Styling.ThemeVariant.Light), 0),
                new GradientStop(new SwiftColor(0.155).Resolve(Avalonia.Styling.ThemeVariant.Light), 1),
            },
        };
        faceLayer.BorderThickness = new Thickness(1); // :358-362
        faceLayer.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(0.09 * 255), 255, 255, 255), 0), // :359
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };

        lip = new Border { Height = 1.5, Margin = new Thickness(7, 0, 7, 2.5), VerticalAlignment = VerticalAlignment.Bottom }.Capsule(RoundedCornerStyle.Continuous); // :364-368

        label = BuildLabel(); // :375-398

        var facePanel = new Panel { Children = { faceLayer, lip } };
        var stack = new Panel { Children = { baseLayer, facePanel } }; // :352 ZStack
        var frame = new Panel { Height = CapHeight, Children = { stack, label } };
        if (cap.IsModifier) frame.Width = ModifierWidth; // :341
        else frame.MinWidth = KeyMinWidth; // :342
        frame.Effect = SwiftShadow.DropShadowEffect(Colors.Black, opacity: 0.45, radius: 1.5, x: 0, y: 1); // :344 (sigma 1.5, ED-327)

        var accessibilityLabel = cap.Name ?? cap.Symbol; // :347
        AutomationProperties.SetName(frame, accessibilityLabel);

        Child = frame;
        foreach (var text in Descendants<TextBlock>(this))
        {
            var inkAnimation = new AnimatedColor(text);
            inks.Add(inkAnimation);
            text.Foreground = inkAnimation.Brush;
        }
        lipColorAnimation = new AnimatedColor(lip);
        lip.Background = lipColorAnimation.Brush;
        Apply();
    }

    private Control BuildLabel() // :374-398
    {
        if (cap.IsModifier)
        {
            var symbol = new TextBlock { Text = cap.Symbol, HorizontalAlignment = HorizontalAlignment.Right };
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Apply(symbol); // :379
            var name = new TextBlock { Text = cap.Name ?? "", HorizontalAlignment = HorizontalAlignment.Right };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(name); // :381
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { symbol, name } };
            var padded = new Border { Child = stack, Padding = new Thickness(8, 6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, RenderTransform = labelOffset };
            return padded;
        }
        var text = new TextBlock { Text = cap.Symbol, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; // :390-392
        SwiftFont.System(14, weight: SwiftFont.Weight.Semibold).Apply(text); // :390
        var keyPad = new Border { Child = text, Padding = new Thickness(10, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransform = labelOffset }; // :394
        return keyPad;
    }

    public void Update(Keycap next, KeycapTint nextTint)
    {
        var glyphChanged = cap.Symbol != next.Symbol; // :395 .id(cap.symbol)
        var tintChanged = tint != nextTint;
        var colorsChanged = tintChanged || cap.Phase != next.Phase;
        cap = next;
        tint = nextTint;
        if (glyphChanged && !cap.IsModifier && label is Border { Child: TextBlock text }) text.Text = cap.Symbol;
        else if (glyphChanged && cap.IsModifier) { } // modifier caps never swap glyph in place; a new KeycapRow row is used instead.
        if (tintChanged) Transaction.WithAnimation(Animation.EaseOut(0.14), () => Apply(colorsChanged)); // :465
        else Apply(colorsChanged);
    }

    public Keycap RepresentedCap => cap; // PORT-ONLY: lets the popover re-tint an existing row without a parallel list.

    private bool IsPressed => cap.Phase == KeycapPhase.Pressed; // :337

    private void Apply(bool colorsChanged = true)
    {
        var pressed = IsPressed;
        Opacity = pressed ? 1 : 0.36; // :345
        var y = pressed ? KeyTravel - 0.5 : 0; // :370, :387, :397
        faceOffset.Y = y;
        labelOffset.Y = y;
        var ink = tint switch // :401-407
        {
            KeycapTint.Neutral => new SwiftColor(1, 0.92),
            KeycapTint.Rejected => KeycapColors.Rejected,
            KeycapTint.Accepted => KeycapColors.Accepted,
            _ => throw new UnreachableException(),
        };
        if (colorsChanged)
            foreach (var inkAnimation in inks) inkAnimation.Set(ink, Transaction.Current); // :465
        var lipColor = !pressed
            ? new SwiftColor(1, 0.10) // :410
            : tint switch // :411-415
            {
                KeycapTint.Neutral => new SwiftColor(1, 0.55),
                KeycapTint.Rejected => KeycapColors.Rejected.Opacity(0.9),
                KeycapTint.Accepted => KeycapColors.Accepted.Opacity(0.9),
                _ => throw new UnreachableException(),
            };
        if (colorsChanged) lipColorAnimation.Set(lipColor, Transaction.Current); // :465
    }

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}

// MARK: - Recording popover

/// <summary>ShortcutRecordingAction (:421-426).</summary>
public sealed record ShortcutRecordingAction(string Id, string Title, bool IsDestructive, Action Perform);

/// <summary>ShortcutRecordingPopoverContent (:428-469). PR-SETTINGS-217, PR-SETTINGS-220, UI-SETTINGS-045,
/// UI-SETTINGS-046, UI-SETTINGS-047.</summary>
public sealed class ShortcutRecordingPopoverContent : Decorator
{
    public ShortcutRecordingPopoverContent(ShortcutRecordingSession session)
    {
        var capsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center }; // :434, :433 a VStack centres each child at its own width
        var capRows = new Dictionary<string, KeycapView>();

        var message = new TextBlock { MaxLines = 1, TextWrapping = TextWrapping.NoWrap, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center }; // :441-445
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium).Apply(message); // :442
        message.Foreground = KeycapColors.Rejected.Brush(message); // :443

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center }; // :448-459
        var actionButtons = new Dictionary<string, Control>();

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Children = { capsRow, message, actionsRow } }; // :433
        var padded = new Border { Child = stack, Padding = new Thickness(14) }; // :462
        AutomationProperties.SetName(padded, "Press keys…"); // :467 U+2026 HORIZONTAL ELLIPSIS
        Child = padded;

        void ApplyCaps()
        {
            var seen = new HashSet<string>();
            var index = 0;
            foreach (var cap in session.Caps)
            {
                seen.Add(cap.Id);
                if (capRows.TryGetValue(cap.Id, out var view)) view.Update(cap, session.Tint);
                else
                {
                    view = new KeycapView(cap, session.Tint).Transition(SwiftTransition.Scale(0.5).Combined(SwiftTransition.Opacity)); // :437
                    capRows[cap.Id] = view;
                    SwiftTransition.Insert(capsRow, Math.Min(index, capsRow.Children.Count), view);
                }
                index++;
            }
            foreach (var gone in capRows.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                SwiftTransition.Remove(capsRow, capRows[gone]);
                capRows.Remove(gone);
            }
        }

        void ApplyMessage()
        {
            var text = session.Message;
            message.Text = text;
            message.IsVisible = text is not null; // :440
        }

        void ApplyActions()
        {
            var seen = new HashSet<string>();
            foreach (var action in session.Actions)
            {
                seen.Add(action.Id);
                if (actionButtons.ContainsKey(action.Id)) continue;
                var control = ActionPill(action); // :449-458
                actionButtons[action.Id] = control;
                actionsRow.Children.Add(control);
            }
            foreach (var gone in actionButtons.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                actionsRow.Children.Remove(actionButtons[gone]);
                actionButtons.Remove(gone);
            }
            actionsRow.IsVisible = session.Actions.Count > 0; // :447
        }

        ApplyCaps();
        ApplyMessage();
        ApplyActions();
        this.Track(() => session.Caps, _ => Transaction.WithAnimation(
            EnvironmentValues.GetAccessibilityReduceMotion(this) ? Animation.EaseOut(0.12) : Animation.Spring(0.3, 0.8, 0),
            ApplyCaps)); // :430,464
        this.Track(() => session.Tint, _ => Transaction.WithAnimation(Animation.EaseOut(0.14),
            () => { foreach (var v in capRows.Values) v.Update(v.RepresentedCap, session.Tint); ApplyMessage(); })); // :465
        this.Track(() => session.Message, _ => ApplyMessage());
        this.Track(() => session.Actions, _ => ApplyActions());
    }

    private static Control ActionPill(ShortcutRecordingAction action) // :450-457, :471-481
    {
        var text = new TextBlock { Text = action.Title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap }; // :452-454
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(text); // :452
        text.Foreground = (action.IsDestructive ? KeycapColors.Rejected : Chrome.PrimaryText).Brush(text); // :476
        var padded = new Border { Child = text, Padding = new Thickness(12, 0), Height = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; // :454-455
        var glass = new Border { Child = padded, Background = Brushes.Transparent }.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive()); // :477 no tint
        var scale = new ScaleTransform(1, 1);
        glass.RenderTransform = scale;
        glass.RenderTransformOrigin = RelativePoint.Center;
        // :478 The action pill's press scale has no animation modifier.
        glass.AddHandler(InputElement.PointerPressedEvent, (_, _) => { scale.ScaleX = scale.ScaleY = 0.97; }, RoutingStrategies.Tunnel); // :478
        glass.PointerReleased += (_, _) => { scale.ScaleX = scale.ScaleY = 1; };
        glass.PointerCaptureLost += (_, _) => { scale.ScaleX = scale.ScaleY = 1; };
        return PlainControls.Button(glass, action.Perform);
    }
}

/// <summary>PORT-ONLY accessor so ShortcutRecordingPopoverContent can re-tint an existing KeycapView row without
/// keeping a parallel cap list.</summary>
public static class KeycapViewExtensions
{
}

/// <summary>Haptics (:483-487). Port shim: a no-op, per porting/port_impacts.md:434 entry 213 (X6). Never
/// substitute a sound or an animation.</summary>
public enum HapticPattern { Generic, Alignment, LevelChange }

public static class Haptics
{
    /// <summary>PORT-ONLY (ED-261): where each haptic goes. The shipped sink does nothing
    /// (port_impacts X6: no sound or animation substitutes for it); a test overrides it for its own flow so the
    /// call sites of the Swift (AboutUpdateViews.swift:804,809,812) and their times can be observed.</summary>
    public static Ambient<Action<HapticPattern>> Sink { get; } = new((Action<HapticPattern>)Ignore);

    public static void Perform(HapticPattern pattern) => Sink.Current(pattern); // :484-486

    private static void Ignore(HapticPattern pattern) { }
}

/// <summary>ShortcutRecordingSession (:489-676). @MainActor @Observable final class. PR-SETTINGS-217,
/// PR-SETTINGS-218.</summary>
public sealed class ShortcutRecordingSession : MainActorModel
{
    private ValueList<Keycap> capsField = new([Keycap.GhostCommand]); // :500
    private ShortcutVerdict verdictField = ShortcutVerdict.Listening; // :501
    private ValueList<ShortcutRecordingAction> actionsField = ValueList<ShortcutRecordingAction>.Empty; // :502
    private string? messageField; // :503

    private bool isActive; // :505
    private AppShortcut? shortcut; // :506
    private Action? onClose; // :507
    private Action<KeyChord>? onCommit; // :508
    private ChordModifiers heldModifiers; // :509
    private long? heldKeyCode; // :510
    private KeyChord? latched; // :511
    private bool isFinishing; // :512
    private TaskHandle? finishTask; // :515

    public ValueList<Keycap> Caps { get => Read(capsField); private set => Set(ref capsField, value); }

    public ShortcutVerdict Verdict { get => Read(verdictField); private set => Set(ref verdictField, value); }

    public ValueList<ShortcutRecordingAction> Actions { get => Read(actionsField); private set => Set(ref actionsField, value); }

    public string? Message { get => Read(messageField); private set => Set(ref messageField, value); }

    public bool IsActive => isActive; // PORT-ONLY: exposed for the input source and tests; not a Swift stored/computed member.

    public KeycapTint Tint => Verdict switch // :517-523
    {
        ShortcutVerdict.Listening => KeycapTint.Neutral,
        ShortcutVerdict.Rejected => KeycapTint.Rejected,
        ShortcutVerdict.Accepted => KeycapTint.Accepted,
        _ => throw new UnreachableException(),
    };

    public void Begin(AppShortcut shortcut, IReadOnlyList<ShortcutRecordingAction> actions, ChordModifiers currentModifiers, Action onClose, Action<KeyChord> onCommit) // :525-562
    {
        if (isActive) Close(); // :531
        this.shortcut = shortcut; // :532
        Actions = new ValueList<ShortcutRecordingAction>(actions); // :533
        this.onClose = onClose; // :534
        this.onCommit = onCommit; // :535
        heldKeyCode = null; // :536
        latched = null; // :537
        isFinishing = false; // :538
        Verdict = ShortcutVerdict.Listening; // :539
        Message = null; // :540
        heldModifiers = currentModifiers & KeyChord.AllowedModifiers; // :541
        RefreshCaps(); // :542
        isActive = true; // :543
        // :545-562 install the OS monitor and resign observer: done by ShortcutInputSource, which calls
        // HandleKeyDown/HandleKeyUp/HandleFlagsChanged/Cancel below.
    }

    public void Cancel() // :564-567
    {
        if (!isActive) return; // :565
        Close(); // :566
    }

    private void Close() // :569-581
    {
        finishTask?.Cancel(); // :570
        finishTask = null; // :571
        // :572-575 the monitor/observer teardown is ShortcutInputSource.Detach, called by the recorder view.
        isActive = false; // :576
        isFinishing = false; // :577
        var close = onClose; // :578
        onClose = null; // :579
        close?.Invoke(); // :580
    }

    public void HandleFlagsChanged(ChordModifiers flags) // :583-594
    {
        flags &= KeyChord.AllowedModifiers; // :584
        var added = (flags & ~heldModifiers) != ChordModifiers.None; // :585
        heldModifiers = flags; // :586
        if (isFinishing) return; // :587
        if (added) Haptics.Perform(HapticPattern.Alignment); // :588
        if (flags == ChordModifiers.None && heldKeyCode is null) LetGo(); // :589-590
        else RefreshCaps(); // :592
    }

    public void HandleKeyDown(long keyCode, ChordModifiers flags, bool isAutoRepeat) // :596-608
    {
        if (isAutoRepeat) return; // :597
        flags &= KeyChord.AllowedModifiers; // :599
        if (keyCode == 53 && flags == ChordModifiers.None) { Cancel(); return; } // :600-603, the session's own bare-Escape rule (the input source already filters most of this; kept for parity)
        if (isFinishing) return; // :604
        heldKeyCode = keyCode; // :605
        heldModifiers = flags; // :606
        Judge(new KeyChord(keyCode, flags)); // :607
    }

    public void HandleKeyUp(long keyCode) // :610-619
    {
        if (heldKeyCode != keyCode) return; // :611
        heldKeyCode = null; // :612
        if (isFinishing) return; // :613
        if (heldModifiers == ChordModifiers.None) LetGo(); // :614-615
        else RefreshCaps(); // :617
    }

    private void Judge(KeyChord chord) // :621-633
    {
        latched = chord; // :622
        var refusal = shortcut is { } s ? ShortcutStore.Shared.Current.Refusal(chord, s) : null; // :623
        if (refusal is { } text)
        {
            Verdict = ShortcutVerdict.Rejected; // :624
            Message = text; // :625
            Haptics.Perform(HapticPattern.Generic); // :626
        }
        else
        {
            Verdict = ShortcutVerdict.Accepted; // :628
            Message = null; // :629
            Haptics.Perform(HapticPattern.LevelChange); // :630
        }
        RefreshCaps(); // :632
    }

    private void LetGo() // :635-657
    {
        if (latched is not { } chord)
        {
            RefreshCaps(); // :637
            return;
        }
        switch (Verdict)
        {
            case ShortcutVerdict.Accepted: // :641-649
                isFinishing = true; // :642
                RefreshCaps(); // :643
                finishTask = MainActor.Spawn(async () =>
                {
                    await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(300)); // :645
                    if (SwiftTask.IsCancelled || !isActive) return; // :646
                    onCommit?.Invoke(chord); // :647
                    Close(); // :648
                });
                break;
            case ShortcutVerdict.Rejected: // :650-653
                latched = null; // :651
                Verdict = ShortcutVerdict.Listening; // :652
                RefreshCaps(); // :653
                break;
            case ShortcutVerdict.Listening: // :654-655
                RefreshCaps();
                break;
        }
    }

    private void RefreshCaps() // :659-674
    {
        ValueList<Keycap> next;
        if (latched is { } chord && Verdict != ShortcutVerdict.Listening) // :661
        {
            next = new ValueList<Keycap>(Keycap.CapsFor(chord)); // :662
        }
        else
        {
            var modifiers = Keycap.ModifierCaps(heldModifiers, KeycapPhase.Pressed); // :664
            if (modifiers.Count == 0) next = new ValueList<Keycap>([Keycap.GhostCommand]); // :665-666
            else if (heldKeyCode is { } code) next = new ValueList<Keycap>(modifiers.Append(Keycap.KeyCap(code, KeycapPhase.Pressed))); // :667-668
            else next = new ValueList<Keycap>(modifiers.Append(Keycap.GhostKey)); // :670
        }
        if (!next.Equals(Caps)) Caps = next; // :673
    }
}

public enum ShortcutVerdict { Listening, Rejected, Accepted } // :494-498
