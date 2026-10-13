using System.Runtime.CompilerServices;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W10 (ED-NEW-web-w10-2). The web settings as the rest of the app reaches them. Once the app model exists
// (FinishStartup) it attaches its own settings here, and every seam reads them from then on: a tool thread asking whether live answers are on,
// a download asking where to save. Before that, and in a host with no app model (a test, a gallery), a seam reads null and keeps its designed
// default, so nothing makes the app model early or off the UI thread. The settings are plain fields, so a read from another thread sees a
// whole value; every write is the settings page's, on the main thread.

namespace Marrow.App.Port.Web;

public static class WebSettingsHost
{
    private static readonly ConditionalWeakTable<ISettingsStore, WebSettings> Stored = new();
    private static WebSettings? attached;

    /// <summary>The app's settings once attached, else null: what a seam reads so a host with no settings keeps the designed defaults.</summary>
    public static WebSettings? Active => Volatile.Read(ref attached);

    /// <summary>The settings now: the attached ones, else settings over the current UserDefaults store (one per store; a page or a gallery edits these).</summary>
    public static WebSettings Current => Active ?? Stored.GetValue(UserDefaults.Standard.Current, store => new WebSettings(store));

    /// <summary>The app attaches its settings; the result puts back what was there (tests).</summary>
    public static IDisposable Attach(WebSettings settings)
    {
        var previous = Interlocked.Exchange(ref attached, settings);
        return new Restore(previous);
    }

    private sealed class Restore(WebSettings? previous) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref attached, previous);
    }
}
