using System.Runtime.CompilerServices;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W10 (ED-NEW-web-w10-2). The web settings as the rest of the app reaches them: the running app's, read where they
// are needed (a tool thread asking whether live answers are on, a download asking where to save). The settings are plain fields, so a read from
// another thread sees a whole value; every write is the settings page's, on the main thread. Until Program.Main binds the app's, a host with
// no app model (a test, a gallery) reads settings made over the current UserDefaults store, one per store.

namespace Marrow.App.Port.Web;

public static class WebSettingsHost
{
    private static readonly ConditionalWeakTable<ISettingsStore, WebSettings> Stored = new();

    /// <summary>Where the app's settings come from; Program.Main binds the running app model's. Null: settings over the current store.</summary>
    public static Func<WebSettings>? Source { get; set; }

    /// <summary>The settings now.</summary>
    public static WebSettings Current => Source?.Invoke() ?? Stored.GetValue(UserDefaults.Standard.Current, store => new WebSettings(store));
}
