using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The notices the runtime appends (App/Runtime/ThreadRuntime.swift: compact :1754, startTurn's catch :1379, takeBackRunningTurn
// :716, and ensureSession :1593, B3): the exact text and level of each row.
public sealed class ThreadRuntimeNoticeRowsTests
{
    private static Notice[] NoticesNow(ThreadRuntimeTurnRig rig) => rig.Runtime.Entries
        .Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Notice>().Select(notice => notice.Value).ToArray();

    private static Notice[] Notices(ThreadRuntimeTurnRig rig) => rig.H.Main.OnMain(() => NoticesNow(rig));

    [Fact]
    [Trait("Req", "UI-RUNTIME-032")] // :1754 a compact that failed: the error's localized description, a warning, no wrapper text
    public void A_failed_compact_appends_the_errors_description_as_a_warning()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        rig.Session.CompactScript = () => Task.FromException(new ProviderError(new ProviderError.Case.Failed("The provider could not compact right now.")));

        rig.H.Main.OnMain(() => rig.Runtime.Compact());
        rig.H.Main.Loop.RunUntilIdle();

        var notices = Notices(rig);
        Assert.Equal(2, notices.Length);
        Assert.Equal((Notice.Level.Info, "Compacting context."), (notices[0].NoticeLevel, notices[0].Message));
        Assert.Equal((Notice.Level.Warning, "The provider could not compact right now."), (notices[1].NoticeLevel, notices[1].Message));
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-034")] // :1379 a ProviderError that threw while the turn was current: its description, as an error
    public void A_provider_error_that_ends_a_turn_appends_its_description_as_an_error()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Session.SendFailure = new ProviderError(new ProviderError.Case.Failed("The provider refused this request."));

        rig.Send("Fix the parser");

        var notice = Assert.Single(Notices(rig));
        Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
        Assert.Equal("The provider refused this request.", notice.Message);
        Assert.Equal(TurnStatus.Failed, rig.Runtime.Turns.Last().Status); // then the turn is finished as failed
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-037")] // :716 the revert that should have removed a stopped message failed: "The stopped message was kept: " + the error
    public void A_stopped_message_whose_rewind_fails_is_kept_with_an_error_notice()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("First message");
        rig.Complete();
        rig.Send("Second message"); // runs; the provider has no resume point before it (no session id, no anchor)
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);

        rig.H.Main.OnMain(() => rig.Runtime.TakeBackRunningTurn());
        rig.Complete(TurnStatus.Interrupted);
        rig.H.Main.Loop.RunUntil(() => NoticesNow(rig).Any(notice => notice.NoticeLevel == Notice.Level.Error), AppModelHarness.Bound);

        var notice = Assert.Single(Notices(rig));
        Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
        Assert.Equal("The stopped message was kept: Claude has no resume point before this message. The conversation was kept.", notice.Message);
        Assert.Equal(2, rig.H.Main.OnMain(() => rig.Runtime.Turns.Count)); // the stopped turn stays in the thread
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-031")] // :1593 a lost conversation during a turn: a warning with the provider's display name
    public void A_provider_that_lost_the_conversation_during_a_turn_warns_with_its_display_name()
    {
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider =>
            provider == ProviderKind.Commandcode ? SwiftURL.Create("file:///factory-test/cli") : null);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Commandcode, thread => thread with
        {
            ProviderSessionID = "conversation-the-cli-no-longer-has",
            ProviderResumeAt = "anchor-1",
        }, install: false);
        ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", Guid.NewGuid());
        try
        {
            Task task = null!;
            rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
            rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
            Assert.True(task.IsCompletedSuccessfully);

            var notice = Assert.Single(Notices(rig));
            Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
            Assert.Equal("Command Code no longer has this conversation. It starts a new one here, without the messages above.", notice.Message);

            // R-TEST-9: the new session installed its mod under the harness's scratch folder, not the real %LOCALAPPDATA%.
            var appData = CommandCodeSession.LocalApplicationDataForTesting.Current;
            Assert.StartsWith(Path.GetFullPath(Marrow.Testing.TestDataDirectory.Read()!), Path.GetFullPath(appData), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(appData, Marrow.Core.Support.AppInfo.DataFolderName, "CommandCode", "marrow-approvals.ts")));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        }
    }
}
