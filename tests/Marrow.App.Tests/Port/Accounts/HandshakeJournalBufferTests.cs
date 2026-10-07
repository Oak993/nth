using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeJournalBufferTests
{
    [Fact]
    public void Multi_account_streaming_is_buffered_and_a_completed_turn_flushes_full_output_in_one_batch()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.Accounts.Add(ProviderKind.Claude));
        rig.Send("Keep the command output.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        var output = new string('x', 150_000) + "LAST-BYTE";
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Command, Title = "offline" }));
            for (var i = 0; i < 30; i++) rig.Session.Emit(new ProviderEvent.ToolOutput("tool", output.Substring(i * 5000, 5000)));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool", "LAST-BYTE"));
            Assert.False(File.Exists(HandoffJournal.PathIn(folder)));
            rig.Session.Emit(new ProviderEvent.ToolUpdated("tool", new() { Status = ToolCall.Status.Completed }));
        });
        rig.Complete();
        Assert.True(File.Exists(HandoffJournal.PathIn(folder)));
        var summary = rig.H.Main.OnMain(() => rig.Runtime.SaveHandoff(""));
        var saved = System.Text.Json.JsonSerializer.Deserialize<HandoffContext>(File.ReadAllText(summary.HandoffFile))!;
        Assert.Equal(output, Assert.IsType<TimelineItem.Content.Tool>(saved.History.Items.First(i => i.Id == "tool").TimelineItemContent).Value.Output);
    }

    [Fact]
    public void A_multi_account_journal_stays_active_when_the_second_account_is_disabled_before_completion()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.Accounts.Add(ProviderKind.Claude));
        rig.Send("Keep all of my output.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Other, Title = "offline" }));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool", new string('x', 150_000) + "RAW-TAIL"));
            rig.App.Settings.Accounts.SetEnabled(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[1].Id, false);
        });
        rig.Complete();
        var stored = HandoffJournal.Restore(rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence()), HandoffJournal.Read(folder));
        Assert.Equal(new string('x', 150_000) + "RAW-TAIL", Assert.IsType<TimelineItem.Content.Tool>(Assert.Single(stored.Items, item => item.Id == "tool").TimelineItemContent).Value.Output);
    }

    [Fact]
    public void Shutdown_flushes_raw_output_that_has_not_reached_the_batch_threshold()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.Accounts.Add(ProviderKind.Claude));
        rig.Send("Keep all of my output through restart.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Other, Title = "offline" }));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool", new string('x', 150_000) + "RESTART-TAIL"));
            rig.App.SaveBeforeQuit();
        });
        var stored = HandoffJournal.Restore(rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence()), HandoffJournal.Read(folder));
        Assert.Equal(new string('x', 150_000) + "RESTART-TAIL", Assert.IsType<TimelineItem.Content.Tool>(Assert.Single(stored.Items, item => item.Id == "tool").TimelineItemContent).Value.Output);
    }

    [Fact]
    public void A_single_account_preserves_clipped_raw_output_when_it_shuts_down_before_a_second_account_is_connected()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Preserve my original command output.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Other, Title = "offline" }));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool", new string('x', 150_000) + "BEFORE-SECOND-TAIL"));
            rig.App.SaveBeforeQuit();
        });
        var stored = HandoffJournal.Restore(rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence()), HandoffJournal.Read(folder));
        Assert.Equal(new string('x', 150_000) + "BEFORE-SECOND-TAIL", Assert.IsType<TimelineItem.Content.Tool>(Assert.Single(stored.Items, item => item.Id == "tool").TimelineItemContent).Value.Output);
    }

    [Fact]
    public void A_small_raw_assistant_reply_survives_display_cleanup_and_shutdown_word_for_word()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep the exact reply.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("reply", "Café—17"));
            rig.App.SaveBeforeQuit();
        });
        var stored = HandoffJournal.Restore(rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence()), HandoffJournal.Read(folder));
        Assert.Equal("Café—17", Assert.IsType<TimelineItem.Content.Assistant>(Assert.Single(stored.Items, item => item.Id == "reply").TimelineItemContent).Value.Text);
    }

    [Fact]
    public void A_small_resolved_approval_and_its_scope_survive_shutdown_for_a_future_handoff()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep the approval scope.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        rig.H.Main.OnMain(() =>
        {
            var option = new ApprovalRequest.Option("once", "Approve once", ApprovalRequest.Option.Role.Approve);
            var request = new ApprovalRequest("approve-17", ApprovalRequest.Kind.Tool, "Only Config.cs", new([option]), Detail: "Scope 17");
            rig.Session.Emit(new ProviderEvent.Approval(request));
            rig.Runtime.Resolve(request, option);
            Assert.Empty(rig.Runtime.Approvals);
            rig.App.SaveBeforeQuit();
        });
        Assert.Contains(HandoffJournal.Read(folder), line => line.Contains("Resolution", StringComparison.Ordinal)
            && line.Contains("Scope 17", StringComparison.Ordinal) && line.Contains("Approve once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_running_message_boundary_checkpoint_does_not_claim_that_the_source_session_is_quiescent()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Do two things.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("reply", "First thing done."));
            var summary = rig.Runtime.SaveHandoff("diff");
            var saved = System.Text.Json.JsonSerializer.Deserialize<HandoffContext>(File.ReadAllText(summary.HandoffFile))!;
            Assert.False(saved.IsQuiescent);
            Assert.False(rig.Runtime.IsAccountSwitchSafe);
            rig.Session.Emit(new ProviderEvent.ToolStarted("edit", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "Config.cs" }));
            Assert.Throws<InvalidOperationException>(() => rig.Runtime.SaveHandoff("diff"));
        });
    }
}
