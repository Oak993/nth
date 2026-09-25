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

// STOP App.Views.Chat.HydraProgressBar #1 (helper Claude, 2026-09-25)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/HydraProgressBar.swift:43-46,266-275,279-309,312-337,347-353
// Requirements: UI-HYDRA-027, UI-HYDRA-029 (partial: the tool-kind counts line itself is HydraProgressBar's own
// code and is correct; only rendering it for a Tool timeline entry is blocked, below)
// What is unclear or missing: Tool_events_produce_the_ordered_counts_line fails with
// "MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2242" from ToolPresentation.McpParts, called through
// HydraProgressBar's own EventsIn -> StaveCache.Resolve -> Refresh chain for every Tool timeline entry.
// App.Views.Chat.TimelineRows is an entire not-yet-started skeleton unit (every member throws MARROW_STUB); this
// packet only owns HydraProgressBar.swift, not TimelineRows.swift.
// Readings or options: (a) implement ToolPresentation.McpParts here, ahead of TimelineRows's own unit -> guesses at
// behavior that file, not this one, specifies, and risks diverging from TimelineRows's eventual real port; (b)
// leave the dependency unmet and the one test failing, documented here -> safe, visible, matches the
// App.DroppyCodeApp #1 precedent for a cross-unit MARROW_STUB blocker.
// Needed to proceed: App.Views.Chat.TimelineRows ported (owns ToolPresentation.McpParts and every other member this
// file's skeleton currently stubs).
// Done so far: HydraProgressBar.swift itself has 0 MARROW_STUB; SweepBudget, HydraWorkingTitle, HydraElapsedTime,
// HydraHeadProgress and the reply-events-excluded path are implemented and green. Only the tool-events counts-line
// test, which exercises a live Tool entry through TimelineRows's still-stubbed McpParts, is blocked.
