// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-1, ED-NEW-pcctl-7). The shared shapes of nth's PC control tools:
// window, observation and element references, rectangles in physical pixels, the accessibility node the engine formats,
// the settings, the two permissions (see and act), locators and expectations. Pure values: no Win32, no UI framework.
using System.Globalization;

namespace Marrow.Core.Port.PcControl;

/// <summary>A rectangle in physical screen pixels (or window-relative, where a member says so).</summary>
public readonly record struct PcRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(PcPoint point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
    public PcPoint Center => new(X + (Width / 2), Y + (Height / 2));
    public PcRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The smallest rectangle holding every one given; empty when none is.</summary>
    public static PcRect Union(IEnumerable<PcRect> rects)
    {
        var list = rects.Where(r => !r.IsEmpty).ToList();
        if (list.Count == 0) return default;
        var left = list.Min(r => r.X);
        var top = list.Min(r => r.Y);
        return new PcRect(left, top, list.Max(r => r.Right) - left, list.Max(r => r.Bottom) - top);
    }
}

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct PcPoint(int X, int Y);

/// <summary>"w:&lt;hwnd hex&gt;": a window as the model names it. Opaque to the model.</summary>
public static class PcWindowRef
{
    public static string Format(nint handle) => "w:" + ((long)handle).ToString("x", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out nint handle)
    {
        handle = 0;
        if (text is null || !text.StartsWith("w:", StringComparison.Ordinal) || text.Length < 3 || text.Length > 18) return false;
        if (!long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || value <= 0) return false;
        handle = (nint)value;
        return true;
    }
}

/// <summary>"o:&lt;n&gt;": one observation. Element numbers and points are valid only for the observation that made them.</summary>
public static class PcObsId
{
    public static string Format(long number) => "o:" + number.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out long number)
    {
        number = 0;
        return text is not null && text.StartsWith("o:", StringComparison.Ordinal)
            && long.TryParse(text.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }
}

/// <summary>The two things a user agrees to separately (ED-NEW-pcctl-2): seeing an app, and acting in it.</summary>
public enum PcCapability
{
    See,
    Act,
}

/// <summary>The user's answer for one app and one capability.</summary>
public enum PcApproval
{
    Denied,
    Chat,
    Always,
    TimedOut,
}

public enum PcMouseButton
{
    Left,
    Right,
    Middle,
}

public enum PcScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum PcScrollUnit
{
    Page,
    Line,
}

/// <summary>States an accessibility element shows in the tree (between braces).</summary>
[Flags]
public enum PcStates
{
    None = 0,
    Focused = 1 << 0,
    Disabled = 1 << 1,
    Selected = 1 << 2,
    Checked = 1 << 3,
    Expanded = 1 << 4,
    Collapsed = 1 << 5,
    Password = 1 << 6,
    ReadOnly = 1 << 7,
    Offscreen = 1 << 8,
    Scrollable = 1 << 9,
    Mixed = 1 << 10,
}

/// <summary>What an element really supports (from its UIA patterns).</summary>
[Flags]
public enum PcActions
{
    None = 0,
    Invoke = 1 << 0,
    Toggle = 1 << 1,
    Select = 1 << 2,
    ExpandCollapse = 1 << 3,
    Value = 1 << 4,
    RangeValue = 1 << 5,
    Scroll = 1 << 6,
    ScrollIntoView = 1 << 7,
    Text = 1 << 8,
    Window = 1 << 9,
    Focus = 1 << 10,
}

/// <summary>
/// One accessibility element as the reader hands it over: its UIA RuntimeId (as text), its localized control type, name,
/// AutomationId, value, shortcut, states, supported actions, live bounds in physical screen pixels and its children.
/// </summary>
public sealed record PcUiNode(string RuntimeId, string Role, string Name)
{
    public string? AutomationId { get; init; }
    public string? Value { get; init; }

    /// <summary>The element's own keyboard shortcut (UIA AcceleratorKey, such as "Ctrl+S"), if it shows one.</summary>
    public string? Shortcut { get; init; }

    /// <summary>The UI framework that draws it (UIA FrameworkId: Win32, WinForm, WPF, XAML, DirectUI, Chrome, Qt...).</summary>
    public string? Framework { get; init; }

    /// <summary>For a scrollable element: how far down it is scrolled, 0 to 100 (UIA ScrollPattern), when known.</summary>
    public double? ScrollPercent { get; init; }

    /// <summary>The element's own window handle, for controls that are windows (Win32 Edit, Button): rung R2 addresses it.</summary>
    public nint NativeWindow { get; init; }

    /// <summary>The element's class name (UIA ClassName), such as Edit, RichEdit20W or Button.</summary>
    public string? ClassName { get; init; }

    /// <summary>The element's access key (UIA AccessKey, such as "Alt+S").</summary>
    public string? AccessKey { get; init; }

    /// <summary>The element's default action (MSAA DefaultAction, such as "Press" or "Send"), when it has one.</summary>
    public string? DefaultAction { get; init; }
    public PcStates States { get; init; }
    public PcActions Actions { get; init; }
    public PcRect Bounds { get; init; }
    public IReadOnlyList<PcUiNode> Children { get; init; } = [];
    public bool Has(PcStates state) => (States & state) == state;
    public bool Can(PcActions action) => (Actions & action) == action;
}

/// <summary>Who an app is: its exe path (lower-cased file name used for rules) and, for packaged apps, its AUMID.</summary>
public sealed record PcAppIdentity(string ExePath, string? Aumid = null)
{
    public string ExeName => Path.GetFileName(ExePath).ToLowerInvariant();

    /// <summary>The key an approval is stored under: the AUMID when there is one, else the full exe path, lower-cased.</summary>
    public string Key => Aumid is { Length: > 0 } aumid ? "aumid:" + aumid.ToLowerInvariant() : "exe:" + ExePath.ToLowerInvariant();
}

/// <summary>Screenshot detail (Settings): the size of the model grid.</summary>
public enum PcDetail
{
    Standard,
    High,
}

/// <summary>
/// Tactus's settings. Off by default (OD-0140); confirmations on (OD-0140); screenshots not kept in the history after the
/// turn (ED-NEW-pcctl-2, from legacy Marrow); typed text logged as a length only. Real mouse and keyboard (rung R4) stays
/// off until the physical-input gate that makes it safe is built (ED-NEW-pcctl-7).
/// </summary>
public sealed record PcSettings
{
    public bool Enabled { get; init; }
    public bool ConfirmGuardedActions { get; init; } = true;
    public bool ShowPointer { get; init; } = true;
    public PcDetail Detail { get; init; } = PcDetail.Standard;
    public bool ReadTextFromImages { get; init; }
    public bool LogTypedText { get; init; }
    public bool KeepScreenshots { get; init; }
    public bool RealInput { get; init; }

    /// <summary>"Fast helper for simple steps" (DESIGN.md 2.12): off by default; pc_delegate is listed only when on.</summary>
    public bool FastHelper { get; init; }
    public IReadOnlyList<string> UserDeniedApps { get; init; } = [];

    /// <summary>The stop chord (DESIGN.md 4.4, 10.10 `pcControl.stopChord`).</summary>
    public string StopChord { get; init; } = "Ctrl+Alt+Esc";

    /// <summary>The pause chord (DESIGN.md 10.1, 10.10 `pcControl.pauseChord`).</summary>
    public string PauseChord { get; init; } = "Ctrl+Alt+Space";

    /// <summary>"Answer Tactus", empty when not set (DESIGN.md 10.10 `pcControl.answerChord`).</summary>
    public string AnswerChord { get; init; } = "";

    /// <summary>The pill's anchor: "left", "centre" or "right" (DESIGN.md 10.10 `pcControl.pill.anchor`).</summary>
    public string PillAnchor { get; init; } = "centre";

    /// <summary>Where the pill shows: "latest", "main", or a monitor device path (DESIGN.md 10.10 `pcControl.pill.monitor`).</summary>
    public string PillMonitor { get; init; } = "latest";

    /// <summary>The chosen monitor's name, for display only (DESIGN.md 10.10 `pcControl.pill.monitorName`).</summary>
    public string? PillMonitorName { get; init; }

    /// <summary>"Answer approvals and questions in the pill" (DESIGN.md 10.10 `pcControl.pill.answerInPill`).</summary>
    public bool AnswerInPill { get; init; } = true;

    /// <summary>"When you resume, continue the chat" (DESIGN.md 10.1, 10.10 `pcControl.resumeContinuesChat`).</summary>
    public bool ResumeContinuesChat { get; init; } = true;

    /// <summary>"Sound when Tactus needs a moment" (DESIGN.md 4.7), off (`pcControl.sound`, ED-NEW-pcctl-29).</summary>
    public bool Sound { get; init; }

    /// <summary>"Use for new apps" in Separate workspace (DESIGN.md 4.7): "auto", "always" or "never" (`pcControl.workspace.use`).</summary>
    public string WorkspaceUse { get; init; } = "auto";

    /// <summary>The model grid for this setting (DESIGN.md 2.3, critique C27).</summary>
    public PcGridProfile Grid => Detail == PcDetail.High ? PcGridProfile.High : PcGridProfile.Standard;

    /// <summary>The long edge of the model grid (DESIGN.md 2.3: 1568 px, the largest Claude-class models take unresized).</summary>
    public int LongEdge => Grid.LongEdge;

    /// <summary>The most pixels the model grid holds.</summary>
    public int MaxPixels => Grid.MaxPixels;

    public static PcSettings Default { get; } = new();
}

/// <summary>
/// The size of the model grid for one model (DESIGN.md 2.3, critique C27): the long edge and the most pixels a screenshot
/// is downscaled to. Standard fits Claude-class models without their own resize (1568 px, 1.15 MP); High is for models
/// with tiled vision. Never upscaled.
/// </summary>
public sealed record PcGridProfile(int LongEdge, int MaxPixels)
{
    public static PcGridProfile Standard { get; } = new(1568, 1_150_000);
    public static PcGridProfile High { get; } = new(2048, 2_000_000);
}

/// <summary>
/// The rungs of the action ladder (DESIGN.md 2.6): R1 the element's own UI Automation pattern, R2 a message to one child
/// window, R3 a synthetic pen (optional, off until its probe), R4 a borrow of the real pointer or focus, R5 the separate
/// workspace. R0 (no GUI) is chosen by the model through other tools. Results name the rung used.
/// </summary>
public enum PcRung
{
    R1 = 1,
    R2 = 2,
    R3 = 3,
    R4 = 4,
    R5 = 5,
}

/// <summary>
/// An element named by what it is rather than by its number (DESIGN.md 2.12): its name, control type, AutomationId, the
/// name of a container it sits in, and which of several equal matches (1-based, in reading order). Lets a batch address an
/// element that appears only after an earlier step.
/// </summary>
public sealed record PcLocator(string? Name, string? Type = null, string? Id = null, string? Within = null, int? Nth = null)
{
    public bool Empty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Type);
}

/// <summary>A window an action is expected to open: its title or class (either may be left out), bound to a name in a batch.</summary>
public sealed record PcWindowMatch(string? Title, string? Class);

/// <summary>
/// What the model expects after an action (DESIGN.md 2.12). Each part is checked locally on the fresh tree before the call
/// returns; the first that does not hold stops the call (and a batch) with expect_failed and what Tactus saw.
/// </summary>
public sealed record PcExpect
{
    /// <summary>The target's value afterwards (compared after NFC and line-ending normalisation).</summary>
    public string? Value { get; init; }

    /// <summary>The target's name afterwards.</summary>
    public string? Name { get; init; }

    /// <summary>An element whose name holds this text is in the window.</summary>
    public string? Appears { get; init; }

    /// <summary>No element's name or value holds this text any more.</summary>
    public string? Gone { get; init; }

    public bool? Selected { get; init; }
    public bool? Checked { get; init; }

    /// <summary>The target has the app's keyboard focus (inside the app; Tactus never takes the user's).</summary>
    public bool? FocusedInApp { get; init; }

    /// <summary>A new window of the app opens.</summary>
    public PcWindowMatch? WindowOpens { get; init; }

    /// <summary>Some element's name or value holds this text.</summary>
    public string? TextInWindow { get; init; }

    /// <summary>In a batch: the name ("$dlg") later steps use for the window that opened.</summary>
    public string? Bind { get; init; }

    public bool Any => Value is not null || Name is not null || Appears is not null || Gone is not null || Selected is not null
        || Checked is not null || FocusedInApp is not null || WindowOpens is not null || TextInWindow is not null;
}
