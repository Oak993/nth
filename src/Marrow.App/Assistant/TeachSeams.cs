// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 16.2; unit AS-19; ED-NEW-dextra-as19-1, -9). No Swift twin.
// The five things teach mode needs from the PC, each behind a seam so the recorder is tested without a desktop: the input the owner makes
// (clicks, typed bursts, shortcuts), the element under a click and the field in focus (UI Automation, called directly until Tactus's lookup
// lands), the screen as a frame, and the browser steps Visus gives (none until Visus lands). The Windows implementations are in
// Marrow.App/Assistant/Teach (hooks, UI Automation, GDI) and have Native tests the lead runs.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Something the owner did, as the input hook sees it. The hook holds no keystroke: a burst of typing is a count, a shortcut is its chord.</summary>
public abstract record TeachInput
{
    /// <summary>A click at a point on the screen. <see cref="Verb"/> is click, right-click or double-click.</summary>
    public sealed record Click(int X, int Y, string Verb) : TeachInput;

    /// <summary>A burst of typing began. The recorder notes which field has the focus now, so the text is read from it however fast the owner moves on.</summary>
    public sealed record TypingStarted : TeachInput;

    /// <summary>A burst of typing ended: this many characters were typed. What they said is read from the field, not from the keys.</summary>
    public sealed record Typing(int Count) : TeachInput;

    /// <summary>A shortcut: a key pressed with Ctrl, Alt or the Windows key held, as text ("Ctrl+S"), or Enter, Tab or Escape alone.</summary>
    public sealed record Chord(string Text) : TeachInput;
}

/// <summary>
/// Watches the mouse and keyboard while a recording runs (a low-level hook on a thread of its own, installed only between Start and Stop).
/// <paramref name="onInput"/> is called on the hook's thread and must only queue.
/// </summary>
public interface ITeachInput : IDisposable
{
    /// <summary>Installs the hooks. Throws <see cref="IOException"/> when Windows refuses them.</summary>
    void Start(Action<TeachInput> onInput);

    /// <summary>
    /// Removes them; nothing is heard after this returns. A burst of typing that had not ended yet is returned (not raised) so the recorder
    /// keeps it before it pauses or ends; null when there is none.
    /// </summary>
    TeachInput? Stop();
}

/// <summary>
/// The control in keyboard focus. Its text is read when <see cref="ReadValue"/> is called, not when the focus is noted, because the text is
/// what the field holds when typing stops. A control that holds a password is never read (the reader answers null). A text that cannot be read is null.
/// </summary>
public sealed record TeachFocus(TeachElement Element, Func<string?> ReadValue)
{
    /// <summary>A focus whose text is already known (a test's, or a control that was read at once).</summary>
    public TeachFocus(TeachElement element, string? value) : this(element, () => value)
    {
    }
}

/// <summary>Looks at the screen the way UI Automation does. Every call may be slow or fail; a failure is a null.</summary>
public interface IElementProbe
{
    /// <summary>The control under a point, with the app and window it is in.</summary>
    TeachElement? At(int x, int y);

    /// <summary>The control in keyboard focus, with a way to read its text later.</summary>
    TeachFocus? Focused();

    /// <summary>The foreground window's app and title, with no control.</summary>
    TeachElement? Foreground();
}

/// <summary>Takes a frame of the screen as a JPEG, no wider than <see cref="TeachLimits.FrameWidth"/>. Null when nothing can be taken now.</summary>
public interface IFrameSource
{
    byte[]? Capture();
}

/// <summary>One thing the owner did in Visus's browser (CDP): an address opened, a control clicked, text typed, a form sent.</summary>
public sealed record TeachBrowserStep(string Verb, string? Url, string? Text, TeachElement? Element)
{
    /// <summary>The page marked the field as a password.</summary>
    public bool IsPassword { get; init; }

    /// <summary>How many characters a typing step typed.</summary>
    public int Length { get; init; }
}

/// <summary>The browser steps of a Visus tab, while a recording runs. None is bound until Visus lands (16.2).</summary>
public interface IBrowserSteps
{
    IDisposable Subscribe(Action<TeachBrowserStep> onStep);
}
