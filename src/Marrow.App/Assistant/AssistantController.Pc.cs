// PORT-ONLY: ED-NEW-mission-pc-1; assistant DESIGN.md 7.4, 12.1a, 12.3-12.5. No Swift twin.
// MCP and API Tactus/Visus adapters retain their seal and wait here. DesktopGate owns entry, the mission runner's
// DextraApproval path owns classifier/taint/cards/grants, and the activity writer must accept intent before anything acts.
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Assistant;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    private IDesktopGate? missionDesktop;
    private readonly CancellationTokenSource pcClosing = new();
    private readonly CancellationToken pcClosingToken;
    private int pcClosed;

    /// <summary>The live duty host binds the single gate. Until bound, sealed calls refuse.</summary>
    public IDesktopGate? MissionDesktop { get => Volatile.Read(ref missionDesktop); set => Volatile.Write(ref missionDesktop, value); }

    internal void ClearMissionDesktop(IDesktopGate desktop) => Interlocked.CompareExchange(ref missionDesktop, null, desktop);

    private void ClosePcCalls()
    {
        if (Interlocked.Exchange(ref pcClosed, 1) != 0) return;
        pcClosing.Cancel();
        pcClosing.Dispose();
        MissionDesktop = null;
    }

    /// <summary>A mission's own nth-pc call: await its card, never resolve a provider request on the adapter's behalf.</summary>
    public async Task<bool> ApprovePc(PcRegistration registration, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref pcClosed) != 0 || registration.MissionId is not { } mission
            || !Guid.TryParse(registration.Thread, out var thread) || !PcProviderWiring.ToolNames.Contains(tool)) return false;
        var active = PcControlBridge.ForCall(registration);
        var json = arguments.ToJsonString();
        var request = new ApprovalRequest("mission-pc:" + active.CallId, ApprovalRequest.Kind.Tool, PcProviderWiring.ClaudeToolPrefix + tool,
            new ValueList<ApprovalRequest.Option>([new("allow", "Approve", ApprovalRequest.Option.Role.Approve), new("deny", "Decline", ApprovalRequest.Option.Role.Decline)]), Detail: json);
        var classification = ActionClassifier.Classify(new ActionRequest.McpTool(PcControlMcpServer.ServerName, tool, json), new ClassifierContext());
        var record = Runner.MissionOf(thread);
        if (record?.Id != mission) return false;
        if (!PcAudit(active, tool, classification, "requested", json)) return false;
        if (MissionDesktop is not { } desktop || !Runner.PcThreadActive(mission, thread))
        {
            PcAudit(active, tool, classification, "refused", json);
            return false;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Runner.PcStopToken(mission), pcClosingToken);
        var watchGate = new object();
        var watching = true;
        void Changed(MissionRecord changed)
        {
            lock (watchGate)
                if (watching && changed.Id == mission && !Runner.PcThreadActive(mission, thread)) linked.Cancel();
        }
        Runner.Changed += Changed;
        try
        {
            if (!Runner.PcThreadActive(mission, thread)) return false;
            await Settle(new AssistantChat(thread, record.Mission.AssistantId, mission, AssistantChatRole.Mission, registration.Provider, registration.Model), linked.Token).ConfigureAwait(false);
            var allowed = await desktop.ApproveMission(mission, request.Headline, !WebToolSchemas.IsWebTool(tool), token =>
            {
                if (!PcControlBridge.Attended.Current(registration.Thread)) return Task.FromResult(false);
                return Runner.ApproveLocal(mission, thread, request, token);
            }, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            var logged = PcAudit(active, tool, classification, allowed ? "approved" : "refused", json);
            return allowed && logged && Runner.PcThreadActive(mission, thread);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PcAudit(active, tool, classification, "cancelled", json);
            return false;
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        {
            PcAudit(active, tool, classification, "refused", json);
            return false;
        }
        finally
        {
            lock (watchGate) watching = false;
            Runner.Changed -= Changed;
        }
    }

    public bool PcCompleted(PcRegistration registration, string tool, JsonObject arguments, string outcome)
    {
        if (registration.MissionId is null || registration.CallId is null || !PcProviderWiring.ToolNames.Contains(tool)
            || outcome is not ("done" or "failed" or "waiting_for_user" or "unverified")) return false;
        var classification = ActionClassifier.Classify(new ActionRequest.McpTool(PcControlMcpServer.ServerName, tool, arguments.ToJsonString()), new ClassifierContext());
        return PcAudit(registration, tool, classification, outcome, null);
    }

    // 12.5: neither typed text, URLs with credentials, nor page/model strings enter the entry. The card has the exact inputs.
    private bool PcAudit(PcRegistration registration, string tool, Classification classification, string result, string? json)
    {
        if (!Guid.TryParse(registration.Thread, out var thread) || Runner.MissionOf(thread) is not { } record || record.Id != registration.MissionId) return false;
        try
        {
            o.Audit(new AuditEntry
            {
                Assistant = record.Mission.AssistantId, Mission = record.Id.ToString("D"), Agent = thread.ToString("D"),
                Tool = tool, ActionClass = classification.Class.ToString(), Target = PcControlMcpServer.ServerName + "." + tool,
                Inputs = json is null ? [AuditInput.Plain("call", registration.CallId)] : [AuditInput.Plain("call", registration.CallId), AuditInput.Typed("arguments", json)],
                Result = result,
            });
            return true;
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        {
            Runner.ReportFault("The activity log could not be written.");
            return false;
        }
    }
}
