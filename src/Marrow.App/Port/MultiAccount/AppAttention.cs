// PORT-ONLY: ED-NEW-perf-idle-5. Whether anyone can see the app, for the account-limits poll: the numbers it reads are shown
// (Settings, the pills, the Handshake card) or acted on (the Handshake near a limit) only while a window of the app can be seen
// or a turn is running. The seam is a class a test replaces (Marrow.UI's WindowPresence needs real Win32 windows).
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Runtime;

public class AppAttention
{
    /// <summary>The process's attention (tests override it for their own flow).</summary>
    public static Ambient<AppAttention> Instance { get; } = new(new AppAttention());

    /// <summary>Whether any window of the app can be seen now (true when none is tracked).</summary>
    public virtual bool Seen => WindowPresence.AppSeen;

    /// <summary>Raised on the UI thread when <see cref="Seen"/> may have changed.</summary>
    public virtual event Action? Changed
    {
        add => WindowPresence.AppSeenChanged += value;
        remove => WindowPresence.AppSeenChanged -= value;
    }
}
