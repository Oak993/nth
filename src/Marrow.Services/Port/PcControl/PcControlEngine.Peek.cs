// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-46; ED-NEW-pcctl-32 item 9; DESIGN.md 2.10 "Windows behind or minimized" and
// the quiet peek, critique C18; 10.4 the "Show window" card). A step that needs a minimized window on screen (a screenshot,
// a point or a drag, the real mouse or keyboard) asks first on the pill, "Show Outlook for a moment?" with why, which
// DESIGN makes the default for every peek until NP-12 proves one unseen for an exe (none is proven, so every peek asks).
// On Show the window is restored without activation at the bottom of the Z-order for that step, and minimized again in its
// exact placement when the step ends, whatever ends it; on Not now the step does without it. Steps that work from the tree
// (R1 patterns, R2 messages to stock controls) never ask: a minimized window answers them as it is.
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    /// <summary>Why a screenshot of a minimized window asks to show it, as the card says it.</summary>
    internal const string ScreenshotReason = "a screenshot needs it on screen";

    /// <summary>Why the real mouse or keyboard in a minimized window asks to show it.</summary>
    internal const string RealInputReason = "the real mouse or keyboard needs it on screen";

    /// <summary>What the model is told after Not now.</summary>
    internal const string NotShown = "the window is minimized and the user chose Not now; the tree still works (use element numbers)";

    /// <summary>
    /// Why a step needs the minimized window on screen, as the card says it; null when the tree is enough, or when the step
    /// names no screenshot of this window it can use (it is refused as it was, without asking).
    /// </summary>
    private string? PeekReason(string tool, PcWindowInfo info, JsonObject args)
    {
        var why = tool switch
        {
            PcToolSchemas.Drag => "a drag needs it on screen",
            PcToolSchemas.Click when args["point"] is not null => "a click at a point needs it on screen",
            PcToolSchemas.Scroll when args["point"] is not null => "a scroll at a point needs it on screen",
            _ => null,
        };
        if (why is null) return null;
        var observation = Observation(PcArgs.Str(args, "obs"));
        return observation is { Shot: not null } && observation.Window.Handle == info.Handle && LatestShot(info.Handle)?.Number == observation.Number ? why : null;
    }

    // Asks to show the minimized window for this step and, on Show, shows it: the peek is the call's until the step ends.
    // Returns the window as it is now shown, or the answer to give the model.
    private async Task<(PcWindowInfo? Shown, PcResult? Refused)> Peek(PcWindowInfo info, string why, PcCall call)
    {
        PcShowAnswer answer;
        using (call.Wait.Waiting(PcWaitReason.ConfirmCard))
            answer = await parts.Confirmer.Show(Source(info), AppName(info), info.Title, why, call.Token).ConfigureAwait(false);
        call.Timings.Mark("confirm");
        if (answer != PcShowAnswer.Show) return (null, PcResult.Fail(PcErrorCode.DeclinedByUser, NotShown));
        if (call.GrantLive?.Invoke() == false)
            return (null, PcResult.Fail(PcErrorCode.NotApproved, "the app was removed from Tactus's apps while the step waited"));
        call.Token.ThrowIfCancellationRequested();
        // paused while the card was up: the step holds before the window is shown
        await Boundary(info, call).ConfigureAwait(false);
        EndPeek(call);
        if (parts.Catalog.Peek(info.Handle) is not { } peek)
            return (null, PcResult.Fail(PcErrorCode.OffScreen, "the window is minimized and Windows did not show it"));
        call.Peek = peek;
        call.Timings.Mark("peek");
        var live = parts.Catalog.Window(info.Handle);
        if (live is null || !info.SameWindow(live)) return (null, PcResult.Fail(PcErrorCode.WindowGone, "the window is gone"));
        if (live.Minimized) return (null, PcResult.Fail(PcErrorCode.OffScreen, "the window is minimized and Windows did not show it"));
        return (live, null);
    }

    // The peek ends with its step: the window minimized again in its exact placement.
    private static void EndPeek(PcCall call)
    {
        var peek = call.Peek;
        call.Peek = null;
        peek?.Dispose();
    }
}
