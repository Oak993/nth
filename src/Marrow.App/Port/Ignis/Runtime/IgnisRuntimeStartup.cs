// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; PLAN unit U3, lead integration). Program.Main's one Bind line:
// IgnisSeams.Controller hands out the app's single controller, made on first use over the running AppModel, the "ignis"
// settings key and the unit U1 parts (IgnisRuntimeSeams.Engine / Points / Ledger, bound here to the engine, the points
// store and the savings ledger under Storage.Root/ignis). It also points the session hooks at unit U2's methods
// (API keep-warm and tidy-up, Claude's /compact instructions), the Settings "Rebuild points" table at the engine's
// tidy point, and Claude's scout entry at the points' helper model while Smart helpers is on. A test overrides any
// seam per async flow, so none of this runs there unless it binds it.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.App.Runtime;

namespace Marrow.App.Port.Ignis.Runtime;

public static class IgnisRuntimeStartup
{
    private static IgnisController? controller;
    private static readonly Lazy<IgnisPointsStore> points = new(() => new IgnisPointsStore(Storage.Root));
    private static readonly Lazy<IgnisSavingsLedger> ledger = new(() => new IgnisSavingsLedger(Storage.Root));

    public static void Bind()
    {
        IgnisRuntimeSeams.Engine.SetProcessDefault(() => IgnisEngine.Shared);
        IgnisRuntimeSeams.Points.SetProcessDefault(() => points.Value);
        IgnisRuntimeSeams.Ledger.SetProcessDefault(() => ledger.Value);
        IgnisRebuildPoints.For.SetProcessDefault(IgnisEngine.RebuildPoint);
        BindSessionHooks();
        IgnisSeams.HelperAgents.SetProcessDefault(query => HelperModel(IgnisSeams.Controller.Current(), query));
        IgnisSeams.Controller.SetProcessDefault(Running);
    }

    /// <summary>Points IgnisSessionHooks at the U2 session methods (process-wide).</summary>
    public static void BindSessionHooks()
    {
        IgnisSessionHooks.KeepWarm.SetProcessDefault(KeepWarm);
        IgnisSessionHooks.Tidy.SetProcessDefault(Tidy);
        IgnisSessionHooks.TidyEndsOnReturn.SetProcessDefault(TidyEndsOnReturn);
    }

    /// <summary>API sessions keep warm with a silent request; the CLIs have none (they use hidden ping turns).</summary>
    public static Func<Task>? KeepWarm(ProviderSession session) =>
        session is OpenAICompatibleSession api ? () => api.KeepWarm() : null;

    /// <summary>
    /// API: the exact tidy-up (summary + the last turns word for word); one that tidied nothing fails, so no "Tidied up"
    /// line is written. Claude: /compact with the keep-N instructions (a request the CLI may honour). Others: plain compact.
    /// </summary>
    public static async Task Tidy(ProviderSession session, int keepTurns)
    {
        switch (session)
        {
            case OpenAICompatibleSession api:
                if (!await api.Tidy(keepTurns)) throw new InvalidOperationException("Ignis tidy-up: nothing was tidied.");
                break;
            case ClaudeSession claude:
                await claude.Compact(ClaudeSession.KeepTurnsInstructions(keepTurns));
                break;
            default:
                await session.Compact();
                break;
        }
    }

    /// <summary>The API tidy-up is silent and done when it returns; the CLIs say "Context compacted." after.</summary>
    public static bool TidyEndsOnReturn(ProviderSession session) => session is OpenAICompatibleSession;

    /// <summary>
    /// Smart helpers (Claude only, the scout entry): the points' helper model for the main model while the setting is
    /// on and Ignis is not off for the chat (or by default when the chat is not known). Null: no change.
    /// </summary>
    public static string? HelperModel(IIgnisController? ignis, IgnisHelperQuery query)
    {
        if (ignis is null || query.Provider != ProviderKind.Claude) return null;
        var preferences = ignis.Settings.Value;
        if (!preferences.SmartHelpers) return null;
        var mode = query.ThreadID is { } thread ? ignis.Settings.ForChat(thread).Mode : preferences.DefaultMode;
        if (mode == IgnisMode.Off) return null;
        return ignis.Points.Find(query.Provider, query.MainModel)?.HelperModel is { Length: > 0 } helper ? helper : null;
    }

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
