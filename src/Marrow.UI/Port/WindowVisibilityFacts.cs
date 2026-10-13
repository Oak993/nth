namespace Marrow.UI.Port;

// PORT-ONLY: ED-NEW-dextra-smooth-10. Injectable HWND facts shared by two distinct policies.
internal readonly record struct WindowVisibilityFacts(bool Shown, bool Minimized, bool Cloaked, bool Covered, bool Focused, bool Foreground)
{
    internal bool Seen => Shown && !Minimized && !Cloaked && !Covered;
    internal bool Available => Seen && Focused && Foreground;
}
