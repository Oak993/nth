// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; PLAN unit U3). Program.Main's one Bind line: IgnisSeams.Controller
// hands out the app's single controller, made on first use over the running AppModel, the "ignis" settings key and the
// unit U1 parts (IgnisRuntimeSeams.Engine / Points / Ledger). While any U1 part is unbound the seam answers null and
// every Ignis hook does nothing. The lead binds the U1 parts and points IgnisSessionHooks at unit U2's session methods
// here at merge (see the ED entry).
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Port.Ignis.Runtime;

public static class IgnisRuntimeStartup
{
    private static IgnisController? controller;

    public static void Bind() => IgnisSeams.Controller.SetProcessDefault(Running);

    private static IIgnisController? Running()
    {
        if (controller is not null) return controller;
        if (IgnisRuntimeSeams.Engine.Current() is not { } engine
            || IgnisRuntimeSeams.Points.Current() is not { } points
            || IgnisRuntimeSeams.Ledger.Current() is not { } ledger) return null;
        var app = Marrow.App.Window.WindowManager.Shared.Model;
        controller = new IgnisController(new IgnisSettings(UserDefaults.Standard.Current), points, ledger, engine, new AppIgnisEnvironment(app));
        return controller;
    }
}
