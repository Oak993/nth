using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Runtime;

[Trait("Feature", "MA-4")]
[Trait("Rule", "R-TEST-4")]
public sealed class ThreadRuntimeHandoffTests
{
    [Fact]
    public void Runtime_archives_full_tool_output_and_approval_choices_before_timeline_condensation()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep 17 and Café 🙂 verbatim.");
        var output = "COMMAND-HEAD" + new string('x', 70_000) + "buried number 456789" + new string('y', 70_000) + "COMMAND-TAIL";
        var approval = new ApprovalRequest("approval-17", ApprovalRequest.Kind.Command, "Run test 17?", new ApprovalRequest.Option[] { new("yes", "Only this time", ApprovalRequest.Option.Role.Approve) });
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool-17", new() { ToolCallKind = ToolCall.Kind.Command, Title = "dotnet test" }));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool-17", output));
            rig.Session.Emit(new ProviderEvent.Approval(approval));
            rig.Runtime.Resolve(approval, approval.Options[0]);
            rig.Session.Emit(new ProviderEvent.ToolUpdated("tool-17", new() { Status = ToolCall.Status.Completed, ExitCode = 0 }));
            rig.Session.Emit(new ProviderEvent.MessageCompleted("assistant-17", "Choice—17 stays word for word."));
        });
        rig.Complete();
        var result = rig.H.Main.OnMain(() => rig.Runtime.SaveHandoff("diff --git a/Config.cs b/Config.cs\n+17"));
        Assert.Contains("Keep 17 and Café 🙂 verbatim.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Only this time", result.Text, StringComparison.Ordinal);
        Assert.Contains("Choice—17 stays word for word.", result.Text, StringComparison.Ordinal);
        Assert.Contains("buried number 456789", File.ReadAllText(result.HandoffFile), StringComparison.Ordinal);
        Assert.Contains("diff --git", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_refuses_handoff_while_a_turn_or_approval_is_pending()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Do not repeat running tools.");
        rig.H.Main.OnMain(() => Assert.Throws<InvalidOperationException>(() => rig.Runtime.SaveHandoff("diff")));
    }

    [Fact]
    public void Resolved_approvals_survive_runtime_reload_and_the_archive_remains_readable()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep permission scoped to this thread.");
        var request = new ApprovalRequest("permission-42", ApprovalRequest.Kind.Tool, "Edit Config.cs?",
            new ApprovalRequest.Option[] { new("no", "Decline for this turn", ApprovalRequest.Option.Role.Decline) });
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.Approval(request));
            rig.Runtime.Resolve(request, request.Options[0]);
        });
        rig.Complete();
        var first = rig.H.Main.OnMain(() => rig.Runtime.SaveHandoff(""));
        rig.H.Main.OnMain(() => DiskWriter.Shared.WriteSynchronously(rig.Runtime.SnapshotForPersistence(), Storage.ThreadURL(rig.InitialThread.Id)));
        var restored = rig.H.Main.OnMain(() => new ThreadRuntime(rig.InitialThread.Id, rig.App));
        rig.H.Main.Loop.Run(() => restored.EnsureLoaded(), AppModelHarness.Bound);
        var again = rig.H.Main.OnMain(() => restored.SaveHandoff(""));
        Assert.Contains("Decline for this turn", again.Text, StringComparison.Ordinal);
        Assert.NotEqual(first.HandoffFile, again.HandoffFile);
        Assert.True(File.Exists(first.HandoffFile));
    }
}
