using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// ED-NEW-lean-18 (lean unit L18a): a Droppy head's processes run in a priority group that is in the background while
// the head is not on screen and comes to the foreground when it is, or when the head finishes. On screen: the head's
// own chat is selected, or its lead's chat is selected with the Hydra panel shown. The lead itself never joins a group.
// Every model lives in an AppModelHarness (R-TEST-9); the main loop is driven, never slept on (R-TEST-5).
public sealed class AppModelHydraHeadPriorityTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_head_is_on_screen_only_while_its_own_chat_or_its_leads_shown_panel_is_selected()
    {
        using var h = new AppModelHarness();
        var (model, lead, head, other) = Make(h);
        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = other.Id;
            Assert.False(model.HydraHeadIsOnScreen(head.Id, lead.Id));
            model.SelectedThreadID = null;
            Assert.False(model.HydraHeadIsOnScreen(head.Id, lead.Id));
            model.SelectedThreadID = lead.Id;
            Assert.True(model.HydraHeadIsOnScreen(head.Id, lead.Id));
            model.Runtime(lead.Id).IsHydraPanelHidden = true;
            Assert.False(model.HydraHeadIsOnScreen(head.Id, lead.Id));
            model.SelectedThreadID = head.Id;
            Assert.True(model.HydraHeadIsOnScreen(head.Id, lead.Id));
            // A head whose lead is unknown is never lowered by doubt.
            model.SelectedThreadID = other.Id;
            Assert.True(model.HydraHeadIsOnScreen(head.Id, null));
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_heads_group_follows_the_screen_and_returns_to_the_foreground_when_the_head_finishes()
    {
        using var h = new AppModelHarness();
        var (model, lead, head, other) = Make(h);
        var group = h.Main.OnMain(() =>
        {
            model.SelectedThreadID = other.Id;
            return (ProcessPriorityGroup)typeof(AppModel).GetMethod("HydraHeadPriority", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(model, [head.Id])!;
        });
        Assert.True(group.IsBackground);

        h.Main.OnMain(() => model.SelectedThreadID = lead.Id);
        h.Main.Loop.RunUntil(() => !group.IsBackground, AppModelHarness.Bound);

        h.Main.OnMain(() => model.Runtime(lead.Id).IsHydraPanelHidden = true);
        h.Main.Loop.RunUntil(() => group.IsBackground, AppModelHarness.Bound);

        h.Main.OnMain(() => model.FinishHydraHead(head.Id, TurnStatus.Completed, "done"));
        Assert.False(group.IsBackground);
        // Finished: the group is let go, so a later selection change no longer moves it.
        h.Main.OnMain(() => model.SelectedThreadID = other.Id);
        h.Main.Loop.Run(() => Task.CompletedTask, AppModelHarness.Bound);
        Assert.False(group.IsBackground);
    }

    private static (AppModel Model, ChatThread Lead, ChatThread Head, ChatThread Other) Make(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead, head, other]);
        var model = h.MakeModel();
        // A lead with heads has its runtime (InsertHydraHead makes it), so its panel can be observed. Loading marks a
        // head saved as running stopped (AppModel.cs:216), so the head is set running again inside the model.
        h.Main.OnMain(() =>
        {
            model.Runtime(lead.Id);
            model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
        });
        return (model, lead, head, other);
    }
}
