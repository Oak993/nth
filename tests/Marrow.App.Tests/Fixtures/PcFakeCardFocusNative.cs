using Marrow.App.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The window side of CardFocus for tests: windows by handle, which one is in front, what was activated and focused.</summary>
public sealed class PcFakeCardFocusNative : ICardFocusNative
{
    public nint Front { get; set; }

    /// <summary>The windows that exist, with their process and class.</summary>
    public Dictionary<nint, (int Process, string Class)> Windows { get; } = [];

    public HashSet<nint> Minimized { get; } = [];

    /// <summary>The focused element's id per window.</summary>
    public Dictionary<nint, string> FocusedIn { get; } = [];

    /// <summary>SetForegroundWindow is refused (a foreground lock).</summary>
    public bool Refuse { get; set; }

    /// <summary>After SetForegroundWindow the focus lands here (null: where the window kept it).</summary>
    public string? LandsOn { get; set; }

    public List<nint> Activated { get; } = [];

    public List<string> Focused { get; } = [];

    public nint Foreground() => Front;

    public CardFocusTarget? Describe(nint window) =>
        Windows.TryGetValue(window, out var info) ? new CardFocusTarget(window, info.Process, info.Class, FocusedIn.GetValueOrDefault(window)) : null;

    public bool Same(CardFocusTarget target) =>
        Windows.TryGetValue(target.Window, out var info) && info.Process == target.ProcessId && info.Class == target.ClassName;

    public bool Hidden(nint window) => Minimized.Contains(window);

    public bool SetForeground(nint window)
    {
        if (Refuse) return false;
        Activated.Add(window);
        Front = window;
        if (LandsOn is { } element) FocusedIn[window] = element;
        return true;
    }

    public bool FocusIs(CardFocusTarget target) => FocusedIn.GetValueOrDefault(target.Window) == target.Element;

    public void Focus(CardFocusTarget target)
    {
        Focused.Add(target.Element!);
        FocusedIn[target.Window] = target.Element!;
    }
}
