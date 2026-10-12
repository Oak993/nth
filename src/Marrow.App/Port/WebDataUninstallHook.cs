// PORT-ONLY: web surfaces W23 (DESIGN 2.3 and 13.4, ED-NEW-web-w23-3). There is no Swift twin. Registers the web data part of Velopack's before-uninstall hook.
using Marrow.Services.Web;
using Velopack;

namespace Marrow.App.Port;

/// <summary>
/// Program.RunVelopackHook hands this the builder's <c>OnBeforeUninstallFastCallback</c> (Velopack adds callbacks to one multicast
/// delegate, so it runs beside the skills and install hooks). The callback deletes <c>%LOCALAPPDATA%\nth\WebView2</c>, the one
/// user data folder of every Visus profile (<see cref="WebDataUninstall"/>), before Velopack removes the app. It never throws
/// into Velopack (the process is about to be exited and a thrown hook would only add a crash dialog to an uninstall): a failure
/// leaves the folder as it was. Nothing here runs an installer or Velopack; the registration is a seam so a test can capture
/// the callback and call it.
/// </summary>
internal static class WebDataUninstallHook
{
    internal static void Register(Action<VelopackHook> register, Func<string> webDataFolder, Action<string>? body = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(webDataFolder);
        var run = body ?? (folder => WebDataUninstall.BeforeUninstall(folder));
        register(_ =>
        {
            try { run(webDataFolder()); }
            catch (Exception) { /* see the type's summary */ }
        });
    }
}
