// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1). The shared shapes of nth's PC control tools: window, observation and
// element references, rectangles in physical pixels, the accessibility node the engine formats, the settings and the
// two permissions (see and act). Pure values: no Win32, no UI framework.
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
/// AutomationId, value, states, supported actions, live bounds in physical screen pixels and its children.
/// </summary>
public sealed record PcUiNode(string RuntimeId, string Role, string Name)
{
    public string? AutomationId { get; init; }
    public string? Value { get; init; }
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

/// <summary>Screenshot detail (Settings): the long edge of the model grid.</summary>
public enum PcDetail
{
    Standard,
    High,
}

/// <summary>
/// PC control's settings. Off by default (OD-0140); confirmations on (OD-0140); screenshots not kept in the history after
/// the turn (ED-NEW-pcctl-2, from legacy Marrow); typed text logged as a length only.
/// </summary>
public sealed record PcSettings
{
    public bool Enabled { get; init; }
    public bool ConfirmGuardedActions { get; init; } = true;
    public bool ShowClickRing { get; init; } = true;
    public PcDetail Detail { get; init; } = PcDetail.Standard;
    public bool ReadTextFromImages { get; init; }
    public bool LogTypedText { get; init; }
    public bool KeepScreenshots { get; init; }
    public IReadOnlyList<string> UserDeniedApps { get; init; } = [];

    public int LongEdge => Detail == PcDetail.High ? 1600 : 1280;

    public static PcSettings Default { get; } = new();
}
