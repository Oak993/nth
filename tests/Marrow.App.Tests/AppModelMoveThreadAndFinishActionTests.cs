using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

public sealed class AppModelMoveThreadAndFinishActionTests
{
    [Fact]
    [Trait("Req", "PR-CHAT-139")] // NewThreadPrompt.swift:595-601
    public void Moving_an_empty_thread_to_a_project_changes_only_its_project_id_and_persists()
    {
        using var h = new AppModelHarness();
        var source = AppModelSamples.Project("Source", h.NewFolder("source"));
        var target = AppModelSamples.Project("Target", h.NewFolder("target"));
        var thread = AppModelSamples.Thread(source) with
        {
            Title = "Untitled draft",
            HasCustomTitle = true,
            IsPinned = true,
            SortOrder = 4,
        };
        var expected = thread with { ProjectID = target.Id };
        h.WriteLibrary([source, target], [thread]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.MoveThread(thread.Id, target));

        Assert.Equal(expected, h.Main.OnMain(() => model.Thread(thread.Id)));
        h.Main.Advance(TimeSpan.FromSeconds(1));
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(expected, AppModelHarness.LoadedLibrary().Threads.Single(value => value.Id == thread.Id));
    }

    [Fact]
    [Trait("Req", "PR-WIN-172")] // AppModel.swift:846-854
    [Trait("Req", "UI-WIN-024")] // AppCommands.swift:100-102
    public void Finish_action_title_and_symbol_follow_the_setting_and_settled_state()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("finish-action"));
        var open = AppModelSamples.Thread(project);
        var settled = AppModelSamples.Thread(project) with { IsSettled = true };
        h.WriteLibrary([project], [open, settled]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.SelectedThreadID = open.Id;
            AssertFinish(model, "Settle thread", "checkmark.circle");
            model.SelectedThreadID = settled.Id;
            AssertFinish(model, "Reopen thread", "arrow.uturn.backward");

            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            model.SelectedThreadID = open.Id;
            AssertFinish(model, "Archive thread", "archivebox");
            model.SelectedThreadID = settled.Id;
            AssertFinish(model, "Archive thread", "archivebox");
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-172")] // AppModel.swift:846-873
    public void Finish_action_archives_a_helper_even_when_settle_is_the_action()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("finish-helper"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsSettled = true };
        h.WriteLibrary([project], [parent, helper]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.SelectedThreadID = helper.Id;
            // AppModel.swift:855-863 has no helper case in the title or the symbol: a settled helper reads as any settled thread.
            AssertFinish(model, "Reopen thread", "arrow.uturn.backward");

            model.FinishSelectedThread();
            Assert.True(model.Thread(helper.Id)!.IsArchived);
        });
    }

    private static void AssertFinish(AppModel model, string title, string symbol)
    {
        Assert.Equal(title, model.FinishActionTitle);
        Assert.Equal(symbol, model.FinishActionSymbol);
    }
}
