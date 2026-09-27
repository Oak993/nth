using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B3 52bc792), helpers(of:) :396-400 in the ruling, :405-409 in this checkout.
public sealed class AppModelHelpersTests
{
    private static (Project Project, ChatThread Parent, ChatThread[] Children) Library(AppModelHarness h)
    {
        var project = AppModelSamples.Project("P", h.NewFolder("helpers")) with { IsExpanded = true };
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var parent = AppModelSamples.Thread(project) with { ExpandsHelpers = true, CreatedAt = start };
        ChatThread Child(int minute, bool panel = false, bool head = false, bool archived = false) =>
            AppModelSamples.Thread(project) with
            {
                ParentThreadID = parent.Id, CreatedAt = start.AddMinutes(minute), IsInPanel = panel,
                IsArchived = archived, Hydra = head ? AppModelSamples.Head(HydraHeadInfo.Status.Completed) : null,
            };
        var children = new[] { Child(1), Child(2), Child(3, head: true), Child(4, panel: true, head: true),
            Child(5, panel: true), Child(6, head: true, archived: true) };
        h.WriteLibrary([project], [parent, .. children]);
        return (project, parent, children);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")] // App/AppModel.swift:396-400; App/Views/Chat/TimelineRows.swift:401-403
    public void Helpers_keep_unarchived_heads_in_or_out_of_panels_drop_panel_subagents_and_sort_newest_first()
    {
        using var h = new AppModelHarness();
        var (_, parent, children) = Library(h);
        var model = h.MakeModel();
        h.Main.OnMain(() => Assert.Equal(new[] { children[3].Id, children[2].Id, children[1].Id, children[0].Id },
            model.Helpers(parent.Id).Select(thread => thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")] // App/AppModel.swift:384-400
    public void Sidebar_navigation_places_the_same_newest_first_helpers_after_the_unfolded_parent()
    {
        using var h = new AppModelHarness();
        var (_, parent, children) = Library(h);
        var model = h.MakeModel();
        h.Main.OnMain(() => Assert.Equal(new[] { parent.Id, children[3].Id, children[2].Id, children[1].Id, children[0].Id },
            model.SidebarThreads.Select(thread => thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-085")] // App/AppModel.swift:396-400, :757-772
    public void Archive_follows_helpers_including_panel_heads_but_not_the_panel_subagent()
    {
        using var h = new AppModelHarness();
        var (_, parent, children) = Library(h);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Archive(parent.Id);
            Assert.True(model.Thread(parent.Id)!.IsArchived);
            foreach (var child in children.Take(4)) Assert.True(model.Thread(child.Id)!.IsArchived);
            Assert.False(model.Thread(children[4].Id)!.IsArchived);
            Assert.True(model.Thread(children[5].Id)!.IsArchived);
        });
    }
}
