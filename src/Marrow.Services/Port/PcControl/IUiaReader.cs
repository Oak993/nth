// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1; DESIGN.md 3.1). The accessibility seam: a window's
// tree, one live element by its RuntimeId, the focused element, element actions through UIA patterns, and an element's
// full text. The engine sees only this interface; tests script it (no test ever reads a real app's tree).
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>What an element is asked to do through its UIA pattern.</summary>
public enum PcElementAction
{
    Invoke,
    Toggle,
    Select,
    Expand,
    Collapse,
    Focus,
    ScrollIntoView,
    SetValue,
    Scroll,
    Minimize,
    Maximize,
    Restore,
    Close,
}

/// <summary>An element command: the action, and the value or the scroll direction, unit and count it needs.</summary>
public sealed record PcElementCommand(PcElementAction Action)
{
    public string? Value { get; init; }
    public PcScrollDirection Direction { get; init; }
    public PcScrollUnit Unit { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>A pattern call's outcome: done, or an error code with its reason.</summary>
public sealed record PcUiaOutcome(PcErrorCode? Error, string? Reason)
{
    public bool Done => Error is null;
    public static PcUiaOutcome Ok { get; } = new(null, null);
    public static PcUiaOutcome Fail(PcErrorCode code, string reason) => new(code, reason);
}

/// <summary>A UIA call that ran past its time (a hung app): only that window is reported as timed out.</summary>
public sealed class PcUiaTimeoutException(string message) : Exception(message);

public interface IUiaReader
{
    /// <summary>The window's control-view tree, the window itself as the root; null when the window is gone.</summary>
    Task<PcUiNode?> Tree(nint window, CancellationToken cancellationToken);

    /// <summary>The live element (no children) with this RuntimeId inside the window; null when it is gone.</summary>
    Task<PcUiNode?> Element(nint window, string runtimeId, CancellationToken cancellationToken);

    /// <summary>The element with keyboard focus, when it is inside the window; else null.</summary>
    Task<PcUiNode?> Focused(nint window, CancellationToken cancellationToken);

    /// <summary>Runs an element command through the element's UIA pattern.</summary>
    Task<PcUiaOutcome> Perform(nint window, string runtimeId, PcElementCommand command, CancellationToken cancellationToken);

    /// <summary>The element's whole text: a document's text, a field's value, or a list's item names one per line.</summary>
    Task<string?> ReadText(nint window, string runtimeId, CancellationToken cancellationToken);
}
