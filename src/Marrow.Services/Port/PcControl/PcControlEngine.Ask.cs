// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.9). pc_ask: the model's
// short question, queued in the ask broker for the pill's card and the chat. The call waits under the 25 s rule with the
// reason "question" and pc_wait until "step" waits on. It answers ok with the answer; declined_by_user with "expired"
// (30 minutes), "moved_on" (the user wrote in the chat first) or "dismissed"; stopped_by_user when a stop covers the chat
// or the app. The question outlives its call: when the call stops waiting it stays in the pill, and its answer goes into
// the chat as the next message (the chat mirror, unit P28). A question that asks for a secret is shown only in the chat.
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    private async Task<PcResult> Ask(JsonObject args, PcCall call)
    {
        var request = PcAskRequest.Parse(args);
        string? app = null;
        string? appName = null;
        if (request.Window is { } text)
        {
            // The window only names the app on the pill's line; a window that is gone or nth's own names none.
            var handle = Window(text);
            if (parts.Catalog.Window(handle) is { } info && !parts.Own.Owns(handle))
            {
                app = info.App.Key;
                appName = AppName(info);
                call.Audit.App = info.ExeName;
                call.Audit.Window = info.Title;
            }
        }
        var ask = parts.Asks.Add(new PcAskDraft(PcAskKind.Question, caller.Thread, request)
        {
            App = app,
            AppName = appName,
            Head = caller.Head,
            Model = caller.Model,
        });
        using var stopped = new CancellationTokenSource();
        void OnStopped(PcScope scope, PcStopSource source)
        {
            if (source != PcStopSource.Sleep && scope.Covers(caller.Thread, app ?? "")) stopped.Cancel();
        }
        parts.Control.Stopped += OnStopped;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Token, stopped.Token);
            PcAskOutcome outcome;
            using (call.Wait.Waiting(PcWaitReason.Question)) outcome = await ask.Outcome.WaitAsync(linked.Token).ConfigureAwait(false);
            return outcome switch
            {
                { Status: PcAskStatus.Answered, Answer: PcAnswer answer } => PcResult.Success(body: answer.Render()),
                { Reason: PcWithdrawal.Expired } => PcResult.Fail(PcErrorCode.DeclinedByUser, PcDeclineReason.Expired),
                { Reason: PcWithdrawal.MovedOn } => PcResult.Fail(PcErrorCode.DeclinedByUser, PcDeclineReason.MovedOn),
                { Reason: PcWithdrawal.Dismissed } => PcResult.Fail(PcErrorCode.DeclinedByUser, PcDeclineReason.Dismissed),
                _ => PcResult.Fail(PcErrorCode.StoppedByUser, "the question was withdrawn"),
            };
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus; the question stays in the pill and its answer goes to the chat");
        }
        finally
        {
            parts.Control.Stopped -= OnStopped;
        }
    }
}
