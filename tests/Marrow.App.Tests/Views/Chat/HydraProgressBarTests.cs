using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraProgressBarTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-119")] // App/Views/Chat/HydraProgressBar.swift:486-539
    [Trait("Req", "PR-HYDRA-191")] // App/Views/Chat/HydraProgressBar.swift:486-539
    [Trait("Req", "UI-HYDRA-030")] // App/Views/Chat/HydraProgressBar.swift:486-539
    public void Working_title_displays_the_supplied_task_text()
    {
        const string title = "Update the working title";
        headless.OnUiThread(() =>
        {
            var view = new HydraWorkingTitle(title, isRunning: false);

            Assert.Contains(title, Texts(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-120")] // App/Views/Chat/HydraProgressBar.swift:543-562
    [Trait("Req", "PR-HYDRA-192")] // App/Views/Chat/HydraProgressBar.swift:543-562
    [Trait("Req", "UI-HYDRA-031")] // App/Views/Chat/HydraProgressBar.swift:543-562
    public void Finished_elapsed_time_uses_the_finish_instant()
    {
        var startedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(125);
        var expected = RelativeTime.Duration(finishedAt - startedAt);

        headless.OnUiThread(() =>
        {
            var view = new HydraElapsedTime(startedAt, finishedAt, isRunning: false);

            Assert.Contains(expected, Texts(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-182")] // App/Views/Chat/HydraProgressBar.swift:43-46, :266-275, :312-337, :347-353
    [Trait("Req", "PR-HYDRA-187")] // App/Views/Chat/HydraProgressBar.swift:279-309
    [Trait("Req", "UI-HYDRA-027")] // App/Views/Chat/HydraProgressBar.swift:43-46, :266-275, :347-353, :391-392
    [Trait("Req", "UI-HYDRA-029")] // App/Views/Chat/HydraProgressBar.swift:178-183, :279-309
    public void Tool_events_produce_the_ordered_counts_line()
    {
        headless.OnUiThread(() =>
        {
            var entries = new[]
            {
                ToolEntry(ToolCall.Kind.Read, "Read a file"),
                ToolEntry(ToolCall.Kind.Command, "Run a command"),
                ToolEntry(ToolCall.Kind.Edit, "Edit a file"),
            };
            var bar = new HydraProgressBar(
                EmptyRuntime(),
                DateTimeOffset.UnixEpoch,
                finishedAt: null,
                HydraHeadInfo.Status.Running,
                SwiftColor.White,
                entries);

            Assert.Contains("1 edit · 1 command · 1 read", Texts(bar));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-182")] // App/Views/Chat/HydraProgressBar.swift:312-337
    [Trait("Req", "PR-HYDRA-187")] // App/Views/Chat/HydraProgressBar.swift:279-309
    public void Reply_events_do_not_add_to_the_counts_line()
    {
        headless.OnUiThread(() =>
        {
            var reply = new TimelineEntry(new TimelineItem(
                turnID: null,
                content: new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Done" }),
                date: DateTimeOffset.UnixEpoch));
            var bar = new HydraProgressBar(
                EmptyRuntime(),
                DateTimeOffset.UnixEpoch,
                finishedAt: null,
                HydraHeadInfo.Status.Running,
                SwiftColor.White,
                [reply]);

            Assert.DoesNotContain("0 edits", Texts(bar));
            Assert.DoesNotContain("0 commands", Texts(bar));
            Assert.DoesNotContain("0 reads", Texts(bar));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-185")] // App/Views/Chat/HydraProgressBar.swift:53-59, :199-206, :581-603
    public void Sweep_budget_grants_three_slots_in_join_order_and_promotes_the_next_waiter()
    {
        headless.OnUiThread(() =>
        {
            var budget = new SweepBudget();
            var first = new object();
            var second = new object();
            var third = new object();
            var fourth = new object();

            budget.Join(first);
            budget.Join(second);
            budget.Join(third);
            budget.Join(fourth);

            Assert.True(budget.Sweeps(first));
            Assert.True(budget.Sweeps(second));
            Assert.True(budget.Sweeps(third));
            Assert.False(budget.Sweeps(fourth));

            budget.Leave(second);

            Assert.True(budget.Sweeps(first));
            Assert.False(budget.Sweeps(second));
            Assert.True(budget.Sweeps(third));
            Assert.True(budget.Sweeps(fourth));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-190")] // App/Views/Chat/HydraProgressBar.swift:452-481
    [Trait("Req", "UI-HYDRA-074")] // App/Views/Chat/HydraProgressBar.swift:452-481
    public void Head_progress_uses_the_thread_title_when_its_task_is_empty()
    {
        headless.OnUiThread(() =>
        {
            var head = Uninitialized<ChatThread>();
            Set(head, nameof(ChatThread.Title), "Thread title");
            Set(head, nameof(ChatThread.CreatedAt), DateTimeOffset.UnixEpoch);
            Set(head, nameof(ChatThread.Hydra), new HydraHeadInfo(
                0,
                "",
                HydraHeadInfo.Kind.Droppy,
                HydraHeadInfo.Origin.Delegated));

            var view = new HydraHeadProgress(head, EmptyRuntime());

            Assert.Contains("Thread title", Texts(view));
        });
    }

    private static IReadOnlyList<string> Texts(Control root) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(root)
            .Select(textBlock => textBlock.Text ?? "")
            .ToArray();

    private static TimelineEntry ToolEntry(ToolCall.Kind kind, string title) =>
        new(new TimelineItem(
            turnID: null,
            content: new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = kind,
                Title = title,
                StartedAt = DateTimeOffset.UnixEpoch,
            }),
            date: DateTimeOffset.UnixEpoch));

    private static ThreadRuntime EmptyRuntime()
    {
        var runtime = Uninitialized<ThreadRuntime>();
        typeof(ThreadRuntime)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, ValueList<TimelineEntry>.Empty);
        return runtime;
    }

    private static T Uninitialized<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Set<T>(T instance, string property, object value) where T : class =>
        typeof(T).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(instance, value);
}

// RESOLVED App.Views.Chat.HydraProgressBar #1 (was: STOP #1, helper Claude, 2026-09-25; resolved B3, Claude Sonnet 5,
// 2026-09-26)
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:2189-2233,2242-2250 (unchanged B2 1fbf81e -> B3 52bc7926).
// ENGINEERING DECISION: Tool_events_produce_the_ordered_counts_line's blocker was
// ToolPresentation.McpParts/Label/Verb/Symbol(ToolCall.Kind) in App.Views.Chat.TimelineRows, all still
// MARROW_STUB. Of TimelineRows's skeleton, only these four are pure, self-contained functions of ToolCall's
// already-ported Kind/Status/Title plus SwiftString and the already-ported MCPCatalog.EntryMatching, with no
// dependency on any other still-stubbed TimelineRows member or type; every other member either builds a View or
// depends on one that does. Ported just those four in TimelineRows.cs (:2189-2233 label/verb/symbol(for kind),
// :2242-2250 mcpParts), each transcribed from the pinned Swift and cited by line, rather than guessed. The rest of
// App.Views.Chat.TimelineRows remains an unread, unstarted skeleton; only its unit may port anything more of it.
