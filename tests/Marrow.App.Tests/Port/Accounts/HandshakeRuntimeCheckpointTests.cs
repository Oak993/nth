using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeRuntimeCheckpointTests
{
    [Fact]
    public void Linking_can_archive_a_running_reply_but_adoption_requires_no_running_tool_or_pending_approval()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep Café and 17 verbatim.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageDelta("answer", "Working reply"));
            rig.Session.Emit(new ProviderEvent.ToolStarted("edit", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "Config.cs" }));
            var summary = rig.Runtime.SaveHandshakeCheckpoint();
            Assert.Contains("Keep Café and 17 verbatim.", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Working reply", summary.Text, StringComparison.Ordinal);
            Assert.False(rig.Runtime.IsAccountSwitchSafe);
            Assert.Throws<InvalidOperationException>(() => rig.Runtime.SaveHandoff(""));
            var context = System.Text.Json.JsonSerializer.Deserialize<HandoffContext>(File.ReadAllText(summary.HandoffFile))!;
            Assert.False(context.IsQuiescent);
        });
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.ToolUpdated("edit", new() { Status = ToolCall.Status.Completed })));
        rig.Complete();
        rig.H.Main.OnMain(() => Assert.True(rig.Runtime.IsAccountSwitchSafe));
    }

    [Fact]
    public void Observe_attributes_real_account_limit_events_instead_of_the_providers_default_identity()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.App.UpdateThread(rig.InitialThread.Id, t => t with { ProviderAccountID = account.Id });
            rig.Runtime.Observe(rig.Session);
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
            Assert.Equal(account.Id.ToString("D"), rig.Runtime.UsageAccount!.AccountId);
        });
    }

    [Fact]
    public void Pending_permissions_questions_and_turn_settlement_block_adoption_even_with_an_idle_phase()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            Assert.True(rig.Runtime.IsAccountSwitchSafe);
            var permission = new ApprovalRequest("permission", ApprovalRequest.Kind.Tool, "Edit Config.cs?",
                new ApprovalRequest.Option[] { new("no", "Decline", ApprovalRequest.Option.Role.Decline) });
            rig.Session.Emit(new ProviderEvent.Approval(permission));
            Assert.False(rig.Runtime.IsAccountSwitchSafe);
            rig.Runtime.Resolve(permission, permission.Options[0]);
            rig.Session.Emit(new ProviderEvent.Question(new QuestionRequest("question", Marrow.Core.Port.ValueList<QuestionRequest.Question>.Empty)));
            Assert.False(rig.Runtime.IsAccountSwitchSafe);
        });
        using var settling = new ThreadRuntimeTurnRig();
        settling.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Set(settling.Runtime, "finishingTurnID", Guid.NewGuid());
            Assert.False(settling.Runtime.IsAccountSwitchSafe);
            Assert.Throws<InvalidOperationException>(() => settling.Runtime.SaveHandoff(""));
            ThreadRuntimeReflection.Set(settling.Runtime, "finishingTurnID", null);
            ThreadRuntimeReflection.Set(settling.Runtime, "closingTurnID", Guid.NewGuid());
            Assert.False(settling.Runtime.IsAccountSwitchSafe);
        });
    }
}
