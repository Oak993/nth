using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// ED-NEW-lean-16 (lean pass L16): the equivalence guard for thread events that touch only what changed. After every event of a
// fixed-seed run (timestamps, statuses, unread, titles, pins, archive and panel flags, no-ops, project moves, reparenting and
// MarkRead), every lookup answers from the list itself and exactly the cells whose value changed tell their observers: each
// thread's cell, each parent's child cell and Threads. Landed green on the unchanged code first.
public sealed class AppModelLeanThreadEventTests
{
    private sealed class Watch
    {
        public readonly HashSet<string> Fired = [];
    }

    private static void Observe(AppModel model, IReadOnlyList<Guid> ids, Watch watch)
    {
        watch.Fired.Clear();
        ObservationTracking.WithTracking(() => model.Threads, () => watch.Fired.Add("threads"));
        foreach (var id in ids)
        {
            var key = id;
            ObservationTracking.WithTracking(() => model.Thread(key), () => watch.Fired.Add("thread " + key));
            ObservationTracking.WithTracking(() => model.Children(key), () => watch.Fired.Add("children " + key));
        }
    }

    private static List<Guid> ChildrenIn(IReadOnlyList<ChatThread> threads, Guid parent) =>
        threads.Where(thread => thread.ParentThreadID == parent).Select(thread => thread.Id).ToList();

    private static HashSet<Guid> ShownIn(IReadOnlyList<ChatThread> threads, Guid project)
    {
        var shown = threads.Where(thread => thread.ProjectID == project && !thread.IsArchived && !thread.IsInPanel).ToList();
        var shownIDs = shown.Select(thread => thread.Id).ToHashSet();
        return shown.Where(thread => thread.ParentThreadID is not { } parent || !shownIDs.Contains(parent))
            .Select(thread => thread.Id).ToHashSet();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")]
    public void Every_thread_event_answers_from_the_list_and_tells_exactly_the_cells_that_changed()
    {
        using var h = new AppModelHarness();
        var first = AppModelSamples.Project("A", h.NewFolder("lean-a")) with { IsExpanded = true };
        var second = AppModelSamples.Project("B", h.NewFolder("lean-b")) with { IsExpanded = true };
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var seed = new ChatThread[60];
        for (var index = 0; index < seed.Length; index++)
        {
            seed[index] = AppModelSamples.Thread(index % 3 == 0 ? second : first) with
            {
                Title = "Thread " + index,
                CreatedAt = start.AddMinutes(index),
                UpdatedAt = start.AddMinutes(index),
                ParentThreadID = index % 7 == 6 ? seed[index - 2].Id : null,
                IsPinned = index % 11 == 0,
            };
        }
        h.WriteLibrary([first, second], seed);
        var model = h.MakeModel();
        var ids = seed.Select(thread => thread.Id).ToArray();
        var random = new Random(16);
        var watch = new Watch();
        h.Main.OnMain(() =>
        {
            foreach (var id in ids) model.IsSelected(id);
            for (var step = 0; step < 1500; step++)
            {
                var before = model.Threads;
                Observe(model, ids, watch);
                var target = ids[random.Next(ids.Length)];
                var kind = random.Next(11);
                switch (kind)
                {
                    case 0: model.UpdateThread(target, thread => thread with { UpdatedAt = thread.UpdatedAt.AddSeconds(1) }); break;
                    case 1: model.UpdateThread(target, thread => thread with { LastStatus = thread.LastStatus == TurnStatus.Running ? TurnStatus.Completed : TurnStatus.Running }); break;
                    case 2: model.UpdateThread(target, thread => thread with { HasUnread = !thread.HasUnread }); break;
                    case 3: model.UpdateThread(target, thread => thread with { Title = "Title " + step }); break;
                    case 4: model.UpdateThread(target, thread => thread with { }); break;
                    case 5: model.UpdateThread(target, thread => thread with { IsPinned = !thread.IsPinned }); break;
                    case 6: model.UpdateThread(target, thread => thread with { ProjectID = thread.ProjectID == first.Id ? second.Id : first.Id }); break;
                    case 7:
                        var parent = random.Next(3) == 0 ? (Guid?)null : ids[random.Next(ids.Length)];
                        model.UpdateThread(target, thread => thread with { ParentThreadID = parent == thread.Id ? null : parent });
                        break;
                    case 8: model.UpdateThread(target, thread => thread with { IsArchived = !thread.IsArchived }); break;
                    case 9: model.MarkRead([target, ids[random.Next(ids.Length)], Guid.NewGuid()]); break;
                    default: model.UpdateThread(target, thread => thread with { IsInPanel = !thread.IsInPanel }); break;
                }
                var after = model.Threads;
                Assert.Equal(before.Select(thread => thread.Id), after.Select(thread => thread.Id));
                var expected = new HashSet<string>();
                if (!before.Equals(after)) expected.Add("threads");
                for (var index = 0; index < after.Count; index++)
                {
                    var thread = after[index];
                    Assert.Equal(thread, model.Thread(thread.Id));
                    if (before[index] != thread) expected.Add("thread " + thread.Id);
                    var children = ChildrenIn(after, thread.Id);
                    Assert.Equal(children, model.Children(thread.Id).Select(child => child.Id));
                    // Children(parent) reads the parent's child cell and then each child's own cell
                    var heldChildren = ChildrenIn(before, thread.Id);
                    if (!heldChildren.SequenceEqual(children)
                        || heldChildren.Any(child => before.First(held => held.Id == child) != after.First(now => now.Id == child)))
                        expected.Add("children " + thread.Id);
                }
                Assert.True(expected.SetEquals(watch.Fired),
                    $"step {step} kind {kind}: expected [{string.Join(", ", expected.Order())}], told [{string.Join(", ", watch.Fired.Order())}]");
                foreach (var project in new[] { first, second })
                    Assert.True(ShownIn(after, project.Id).SetEquals(model.ThreadsIn(project).Select(thread => thread.Id)), $"step {step}: ThreadsIn({project.Name})");
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // AppModel.swift:203-207: the next thread event drops the child cell of a parent that does not exist
    public void A_child_cell_asked_for_a_thread_that_does_not_exist_goes_at_the_next_thread_event()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("lean-ghost")) with { IsExpanded = true };
        var a = AppModelSamples.Thread(project) with { Title = "a" };
        var b = AppModelSamples.Thread(project) with { Title = "b" };
        h.WriteLibrary([project], [a, b]);
        var model = h.MakeModel();
        var ghost = Guid.NewGuid();
        h.Main.OnMain(() =>
        {
            var fired = 0;
            Assert.Empty(ObservationTracking.WithTracking(() => model.Children(ghost), () => fired++));
            model.UpdateThread(a.Id, thread => thread with { Title = "a2" }); // an ordinary event: the ghost's cell goes
            Assert.Equal(0, fired);
            model.UpdateThread(b.Id, thread => thread with { ParentThreadID = ghost }); // the cell the observer held is gone
            Assert.Equal(0, fired);
            Assert.Equal([b.Id], model.Children(ghost).Select(thread => thread.Id));
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // the control: without an event in between, the held cell is the one that changes
    public void A_child_cell_asked_for_a_thread_that_does_not_exist_tells_its_observer_when_no_event_came_between()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("lean-ghost2")) with { IsExpanded = true };
        var b = AppModelSamples.Thread(project) with { Title = "b" };
        h.WriteLibrary([project], [b]);
        var model = h.MakeModel();
        var ghost = Guid.NewGuid();
        h.Main.OnMain(() =>
        {
            var fired = 0;
            Assert.Empty(ObservationTracking.WithTracking(() => model.Children(ghost), () => fired++));
            model.UpdateThread(b.Id, thread => thread with { ParentThreadID = ghost });
            Assert.Equal(1, fired);
            Assert.Equal([b.Id], model.Children(ghost).Select(thread => thread.Id));
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // Observation.Set: an event whose changes cancel out (a repeated id, A to B then B to A) tells no one
    public void A_thread_event_whose_changes_cancel_out_tells_no_observer()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("lean-cancel")) with { IsExpanded = true };
        var a = AppModelSamples.Thread(project) with { Title = "a" };
        var b = AppModelSamples.Thread(project) with { Title = "b" };
        var c = AppModelSamples.Thread(project) with { Title = "c" };
        h.WriteLibrary([project], [a, b, c]);
        var model = h.MakeModel();
        Guid[] peers = [a.Id, b.Id, c.Id, a.Id]; // a repeats: its order goes 0 then 3
        h.Main.OnMain(() =>
        {
            Assert.True(model.MoveInActivity(c.Id, b.Id, placeAfter: false, peers)); // a 3, c 1, b 2
            var before = model.Threads;
            var watch = new Watch();
            Observe(model, [a.Id, b.Id, c.Id], watch);
            Assert.True(model.MoveInActivity(c.Id, b.Id, placeAfter: false, peers)); // a 3 to 0 to 3, c 1, b 2: no net change
            Assert.Equal(before, model.Threads);
            Assert.Empty(watch.Fired);
        });
    }
}
