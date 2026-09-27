using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.App.Views.Chat;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrDerivationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-105")] // App/Views/Chat/TimelineRows.swift:2547-2585
    public void Derivation_partitions_all_kinds_and_keeps_the_last_words_when_the_final_tool_failed()
    {
        headless.OnUiThread(() =>
        {
            using var actor = new TestRuntime();
            actor.OnMain(() =>
            {
                var blank = Reply(" \n\t\r");
                var narration = Reply("narration");
                var first = Reply("first answer");
                var last = Reply("last words");
                var plan = Plan();
                var info = MakeNotice(Notice.Level.Info);
                var warning = MakeNotice(Notice.Level.Warning);
                var error = MakeNotice(Notice.Level.Error);
                var head = Tool(ToolCall.Kind.Agent);
                var failed = Tool(status: ToolCall.Status.Failed,
                    edits: new ValueList<FileEdit>([new FileEdit("file.swift")]));
                var todos = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Todos(ValueList<TodoStep>.Empty)));
                TimelineEntry[] content = [blank, narration, head, first, plan, warning, info, todos, last, error, failed];
                var folded = Derive(content, false);
                // :2553-2559 skip whitespace, preserve all nonblank replies in arrival order.
                Assert.Equal([narration, first, last], Property<ValueList<TimelineEntry>>(folded, "AssistantEntries"));
                // :2566, :2578 clear on EVERY tool, then keep the last assistant when the failed tool ends the turn.
                Assert.Equal([last], Property<ValueList<TimelineEntry>>(folded, "AnswerEntries"));
                Assert.Equal([plan], Property<ValueList<TimelineEntry>>(folded, "CollapsedPlans"));
                Assert.Equal([warning, error], Property<ValueList<TimelineEntry>>(folded, "CollapsedNotices"));
                Assert.Equal([head], Property<ValueList<TimelineEntry>>(folded, "HeadEntries"));
                Assert.True(Property<bool>(folded, "HasEdits"));
                Assert.True(Property<bool>(folded, "HasResponse"));
                Assert.Empty(Property<ValueList<TimelineGroup>>(folded, "DetailGroups"));
                var expanded = Derive(content, true);
                var groups = Property<ValueList<TimelineGroup>>(expanded, "DetailGroups");
                Assert.NotEmpty(groups);
                Assert.DoesNotContain(groups, group => group is TimelineGroup.Heads);
                Assert.Equal(TimelineGroup.Build(content, showReasoning: false).Where(group => group is not TimelineGroup.Heads), groups);

                // :2560-2565 and default :2574: plans, notices and checklists never interrupt the answer.
                var uninterrupted = Derive([narration, head, first, plan, info, warning, todos, last, error], false);
                Assert.Equal([first, last], Property<ValueList<TimelineEntry>>(uninterrupted, "AnswerEntries"));
                Assert.False(Property<bool>(uninterrupted, "HasEdits"));
                // :2555 character predicate, not a trimmed copy of every reply.
                var production = File.ReadAllText(SourcePath());
                Assert.Contains("SwiftString.Characters(assistant.Value.Text).Any(character => !SwiftCharacter.IsWhitespace(character))", production, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-105")] // App/Views/Chat/TimelineRows.swift:2553-2559, :2578-2579
    public void Only_readable_replies_plans_and_non_info_notices_make_a_response()
    {
        headless.OnUiThread(() =>
        {
            using var actor = new TestRuntime();
            actor.OnMain(() =>
            {
                foreach (var content in new TimelineEntry[][] { [], [Reply(" \n\t")], [MakeNotice(Notice.Level.Info)], [Tool()] })
                {
                    var value = Derive(content, false);
                    Assert.False(Property<bool>(value, "HasResponse"));
                    Assert.False(Property<bool>(value, "HasEdits"));
                    Assert.Empty(Property<ValueList<TimelineEntry>>(value, "AnswerEntries"));
                }
                foreach (var entry in new[] { Plan(), MakeNotice(Notice.Level.Warning), MakeNotice(Notice.Level.Error), Reply("x") })
                    Assert.True(Property<bool>(Derive([entry], false), "HasResponse"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-106")] // App/Views/Chat/TimelineRows.swift:2511-2521, :2590-2591, :2703-2706
    public void Derivations_are_counted_and_the_render_read_never_writes_the_cache()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            h.Main.OnMain(() =>
            {
                var turn = Guid.NewGuid();
                var entries = new TimelineRowsPrCountedEntries([Reply("```\nanswer\n```")]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    entries.AsValueList(), null, false, false);
                Assert.Null(Field(block, "derived"));
                var cold = Invoke(block, "DerivedFor", false);
                Assert.Equal(1, entries.Derivations);
                // :2513-2514 "Written after the body ... never in it"; :2704-2706 onChange stores it.
                Assert.Null(Field(block, "derived"));
                var window = Show(block);
                Assert.NotNull(Field(block, "derived"));
                Assert.Equal(2, entries.Derivations);
                var cached = Invoke(block, "DerivedFor", false);
                Assert.NotSame(cold, cached);
                Assert.Same(cached, Invoke(block, "DerivedFor", false));
                Assert.Equal(2, entries.Derivations);
                // A changed fold invalidates the key, but asking for it during render still cannot store state.
                Invoke(block, "DerivedFor", true);
                Assert.Equal(3, entries.Derivations);
                Assert.Same(cached, Invoke(block, "DerivedFor", false));
                Assert.Equal(3, entries.Derivations);
                // The actual change handler records the derivation of the body that has just run.
                var header = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Button>(block)
                    .Single(button => Equals(Avalonia.Controls.ToolTip.GetTip(button), "Show this turn's steps"));
                Click(window, header);
                h.Main.RunUntilIdle(); Settle(window);
                Assert.Equal(4, entries.Derivations);
                var expanded = Invoke(block, "DerivedFor", true);
                Assert.Same(expanded, Invoke(block, "DerivedFor", true));
                Assert.Equal(4, entries.Derivations);
                // Equal text in a different entry is a DIFFERENT identity (DerivedKey.ids).
                entries[0] = Reply("```\nanswer\n```");
                Assert.NotSame(expanded, Invoke(block, "DerivedFor", true));
                Assert.Equal(5, entries.Derivations);
                Close(window);
            });
        });
    }

    private static object Derive(IReadOnlyList<TimelineEntry> entries, bool expanded) =>
        typeof(TurnFinishedBlock).GetNestedType("Derived", BindingFlags.NonPublic)!
            .GetConstructor([typeof(IReadOnlyList<TimelineEntry>), typeof(bool)])!.Invoke([entries, expanded]);
    private static string SourcePath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../../../src/Marrow.App/Views/Chat/TimelineRows.cs"));
}
