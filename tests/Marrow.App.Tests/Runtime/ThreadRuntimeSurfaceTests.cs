using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeSurfaceTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-162")] // App/Runtime/ThreadRuntime.swift:540-555, 633-643
    public async Task Runtime_construction_loads_a_stored_document_synchronously_when_it_was_prefetched()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Prefetched runtime", h.NewFolder("prefetched-runtime"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var document = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>(
            [
                new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "prefetched" })),
            ]),
        };
        DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(thread.Id));
        var warm = DocumentPrefetch.Shared.Warm([thread.Id], urgent: true);
        Assert.NotNull(warm);
        await warm!.Completion.WaitAsync(AppModelHarness.Bound);

        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() =>
        {
            var created = model.Runtime(thread.Id);
            Assert.False(created.IsLoadingHistory);
            var loaded = Assert.IsType<TimelineItem.Content.User>(Assert.Single(created.Entries).Item.TimelineItemContent);
            Assert.Equal("prefetched", loaded.Value.Text);
            return created;
        });

        Assert.Equal(thread.Id, h.Main.OnMain(() => runtime.Thread!.Id));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-162")] // App/Runtime/ThreadRuntime.swift:540-555, 633-643
    public void Runtime_construction_starts_an_async_history_load_and_ensure_loaded_awaits_it()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Disk runtime", h.NewFolder("disk-runtime"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var document = new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>(
            [
                new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "on disk" })),
            ]),
        };
        DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(thread.Id));

        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() =>
        {
            var created = model.Runtime(thread.Id);
            Assert.True(created.IsLoadingHistory);
            Assert.Empty(created.Entries);
            return created;
        });

        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        Assert.False(runtime.IsLoadingHistory);
        var loaded = Assert.IsType<TimelineItem.Content.User>(Assert.Single(runtime.Entries).Item.TimelineItemContent);
        Assert.Equal("on disk", loaded.Value.Text);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-007")] // App/Runtime/ThreadRuntime.swift:58-63
    public void Reply_quote_excerpt_uses_the_first_nonempty_line_and_caps_at_72_characters()
    {
        Assert.Equal("first line", new ReplyQuote
        {
            Text = "\n \t  first \t line  \nsecond line",
        }.Excerpt);

        Assert.Equal(new string('x', 72) + "…",
            new ReplyQuote { Text = new string('x', 73) }.Excerpt);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-007")] // App/Runtime/ThreadRuntime.swift:71-99
    public void Reply_quote_peel_lifts_the_command_and_quote_runs_from_the_body()
    {
        var (quotes, body) = ReplyQuote.Peel("/plan\n\n> first\n>\n> third\n\n> another\n\nbody");

        Assert.Equal(["first\n\nthird", "another"], quotes.Select(quote => quote.Text));
        Assert.Equal("/plan body", body);

        const string untouched = "/plan\n\nplain text";
        var (none, unchangedBody) = ReplyQuote.Peel(untouched);
        Assert.Empty(none);
        Assert.Equal(untouched, unchangedBody);
    }

    [Fact]
    public void Has_working_helpers_tracks_only_an_existing_child_runtime_that_is_running()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Helper runtime", h.NewFolder("helper-runtime"));
        var lead = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = lead.Id };
        h.WriteLibrary([project], [lead, helper]);
        var model = h.MakeModel();
        var leadRuntime = h.Main.OnMain(() => model.Runtime(lead.Id));
        var helperRuntime = h.Main.OnMain(() => model.Runtime(helper.Id));
        h.Main.Loop.Run(() => Task.WhenAll(leadRuntime.EnsureLoaded(), helperRuntime.EnsureLoaded()), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.False(leadRuntime.HasWorkingHelpers);
            helperRuntime.RehearseTurn("helper work");
            Assert.True(leadRuntime.HasWorkingHelpers);
        });
    }

    [Fact]
    public void Has_working_native_heads_ignores_non_native_and_finished_heads()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Native head", h.NewFolder("native-head"));
        var lead = AppModelSamples.Thread(project);
        var native = NewHead(project, lead.Id, HydraHeadInfo.Kind.Native, HydraHeadInfo.Status.Running);
        var droppy = NewHead(project, lead.Id, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Status.Running);
        h.WriteLibrary([project], [lead, native, droppy]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));

        h.Main.OnMain(() =>
        {
            model.UpdateThread(native.Id, thread => thread with
            {
                Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            });
            model.UpdateThread(droppy.Id, thread => thread with
            {
                Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            });
            Assert.True(runtime.HasWorkingNativeHeads);
            model.UpdateThread(native.Id, thread => thread with
            {
                Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed },
            });
            Assert.False(runtime.HasWorkingNativeHeads);
        });
    }

    [Fact]
    public void Pending_plan_approval_returns_the_first_plan_request_and_skips_other_approval_kinds()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = CreateRuntime(h);

        var command = new ApprovalRequest("command", ApprovalRequest.Kind.Command, "Command",
            ValueList<ApprovalRequest.Option>.Empty);
        var firstPlan = new ApprovalRequest("plan-1", ApprovalRequest.Kind.Plan, "First plan",
            ValueList<ApprovalRequest.Option>.Empty);
        var secondPlan = new ApprovalRequest("plan-2", ApprovalRequest.Kind.Plan, "Second plan",
            ValueList<ApprovalRequest.Option>.Empty);

        h.Main.OnMain(() =>
        {
            Assert.Null(runtime.PendingPlanApproval);
            runtime.Rehearse(new ProviderEvent.Approval(command));
            Assert.Null(runtime.PendingPlanApproval);

            runtime.Rehearse(new ProviderEvent.Approval(firstPlan));
            runtime.Rehearse(new ProviderEvent.Approval(secondPlan));
            Assert.Same(firstPlan, runtime.PendingPlanApproval);

            runtime.Rehearse(new ProviderEvent.RequestResolved(firstPlan.Id));
            Assert.Same(secondPlan, runtime.PendingPlanApproval);
        });
    }

    [Fact]
    public void Hydra_tool_head_lookup_is_empty_until_rehearsed_and_then_tracks_the_tool_id()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = CreateRuntime(h);
        const string toolID = "agent-tool";
        var headID = Guid.NewGuid();

        h.Main.OnMain(() =>
        {
            Assert.Null(runtime.HydraHead(toolID));
            runtime.RehearseHydraHead(toolID, headID);
            Assert.Equal(headID, runtime.HydraHead(toolID));
            Assert.Null(runtime.HydraHead("another-tool"));
        });
    }

    private static (AppModel Model, ChatThread Thread, ThreadRuntime Runtime) CreateRuntime(AppModelHarness harness)
    {
        var project = AppModelSamples.Project("Runtime surface", harness.NewFolder("runtime-surface"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return (model, thread, runtime);
    }

    private static ChatThread NewHead(
        Project project,
        Guid parentID,
        HydraHeadInfo.Kind kind,
        HydraHeadInfo.Status status) =>
        new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = parentID,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(status) with
            {
                HydraHeadInfoKind = kind,
                HydraHeadInfoStatus = status,
            },
        };
}