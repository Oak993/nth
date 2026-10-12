using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: the chat's asks for a step in the workspace, as a test answers them (unit P18, ED-NEW-pcws-5): every approval
// and guarded-action card is recorded and answered with what the test set.
public sealed class FakeWorkspaceAsks : IWorkspaceAsks
{
    public WorkspaceApproval Approval { get; set; } = new(PcApproval.Chat, null);

    public WorkspaceConfirm Confirmation { get; set; } = new(true, null);

    public List<string> Asked { get; } = [];

    /// <summary>Set to keep approvals waiting until the test answers.</summary>
    public TaskCompletionSource<WorkspaceApproval>? Hold { get; set; }

    public Task<WorkspaceApproval> Approve(PcAppIdentity app, string appName, PcCapability capability, CancellationToken cancellationToken)
    {
        lock (Asked) Asked.Add("approve " + app.ExeName + " " + WorkspaceAgentProtocol.Wire(capability));
        return Hold is { } hold ? hold.Task.WaitAsync(cancellationToken) : Task.FromResult(Approval);
    }

    public Task<WorkspaceConfirm> Confirm(string word, string elementName, string appName, CancellationToken cancellationToken)
    {
        lock (Asked) Asked.Add("confirm " + word + " " + elementName);
        return Task.FromResult(Confirmation);
    }
}
