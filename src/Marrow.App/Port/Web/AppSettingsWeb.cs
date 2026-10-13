using Marrow.App.Port.Web;

// PORT-ONLY: web surfaces W10 (DESIGN.md 9 "Settings"; ED-NEW-web-w10-2). The one place AppSettings meets the web settings: a property
// that makes them over the same store, on first use. Nothing is read or written until something asks, so the app's other settings are
// untouched and a build without these keys loads exactly as before.

namespace Marrow.App;

public sealed partial class AppSettings
{
    private WebSettings? web;

    /// <summary>The reader's choices for Visus, Tabula and the live answers (Settings › Visus, Settings › Tabula).</summary>
    public WebSettings Web => web ??= new WebSettings(defaults);
}
