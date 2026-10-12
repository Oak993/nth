using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

// ED-NEW-lean-5 guard: a random delta script (fixed seeds) streamed through QueueDelta, PendingFlushLength and
// FlushDeltas gives byte-identical rows and the identical length tier at every step as the old code, kept here as a
// frozen copy: pending text joined with "+", the tier as Characters(row).count + Characters(pending).count, the row
// text joined with "+" once per flush and tool output as TrimOutput(Output + text) over a list of Character starts.
public sealed class ThreadRuntimeStreamEquivalenceTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Pieces chosen to stress Character boundaries across delta and flush seams: CR LF split, combining marks, ZWJ
    // sequences, regional indicators one at a time, skin tones, Hangul jamo, an Indic conjunct, lone surrogates,
    // surrogate halves that only pair up across a seam, em dashes (rewritten for every kind but tool output).
    private static readonly string[] Pieces =
    [
        "a", "b", "word ", " ", "\r", "\n", "\r\n", "é", "e", "́", "̈", "\U0001F600", "\uD83D", "\uDE00",
        "\U0001F44D", "\U0001F3FD", "‍", "\U0001F469", "\U0001F469‍\U0001F469‍\U0001F467", "\U0001F1FA",
        "\U0001F1F8", "\U0001F1E9\U0001F1EA", "\uD83C", "\uDDF8", "ᄀ", "ᅡ", "ᆨ", "क", "्",
        "ष", "—", "…", "’", "✓", "\t", "x⃝", "️", "❤", "ำ",
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Flushed_rows_and_length_tiers_match_the_old_concatenation_for_a_random_delta_script(int seed)
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var random = new Random(seed * 7919);
            var runtime = CreateRuntime();
            var entries = new Dictionary<ThreadRuntimeStreamRow, TimelineEntry>
            {
                [ThreadRuntimeStreamRow.Message] = AddEntry(runtime, "m", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "", IsStreaming = true })),
                [ThreadRuntimeStreamRow.Reasoning] = AddEntry(runtime, "r", new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "", IsStreaming = true })),
                [ThreadRuntimeStreamRow.Plan] = AddEntry(runtime, "p", new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "", ProposedPlanState = ProposedPlan.State.Drafting })),
                [ThreadRuntimeStreamRow.Tool] = AddEntry(runtime, "t", new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run" })),
            };
            var ids = new Dictionary<ThreadRuntimeStreamRow, string> { [ThreadRuntimeStreamRow.Message] = "m", [ThreadRuntimeStreamRow.Reasoning] = "r", [ThreadRuntimeStreamRow.Plan] = "p", [ThreadRuntimeStreamRow.Tool] = "t" };
            var kinds = new Dictionary<ThreadRuntimeStreamRow, ThreadRuntime.DeltaKind>
            {
                [ThreadRuntimeStreamRow.Message] = ThreadRuntime.DeltaKind.Message,
                [ThreadRuntimeStreamRow.Reasoning] = ThreadRuntime.DeltaKind.Reasoning,
                [ThreadRuntimeStreamRow.Plan] = ThreadRuntime.DeltaKind.Plan,
                [ThreadRuntimeStreamRow.Tool] = ThreadRuntime.DeltaKind.ToolOutput,
            };
            var rows = new Dictionary<ThreadRuntimeStreamRow, string> { [ThreadRuntimeStreamRow.Message] = "", [ThreadRuntimeStreamRow.Reasoning] = "", [ThreadRuntimeStreamRow.Plan] = "", [ThreadRuntimeStreamRow.Tool] = "" };
            var pending = new Dictionary<ThreadRuntimeStreamRow, string>();
            var flushLength = Method("PendingFlushLength");
            var flush = Method("FlushDeltas");
            SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1000));

            for (var step = 0; step < 1_500; step++)
            {
                var roll = random.Next(100);
                if (roll < 80)
                {
                    var row = (ThreadRuntimeStreamRow)random.Next(4);
                    var delta = RandomDelta(random, row == ThreadRuntimeStreamRow.Tool && random.Next(8) == 0 ? 6_000 : 12);
                    Method("QueueDelta").Invoke(runtime, [ids[row], kinds[row], delta]);
                    if (delta.Length == 0) continue;
                    var cleaned = row == ThreadRuntimeStreamRow.Tool ? delta : TextCleanup.WithoutEmDashes(delta);
                    pending[row] = pending.TryGetValue(row, out var queued) ? queued + cleaned : cleaned;
                }
                else if (roll < 90)
                {
                    Assert.Equal(OldPendingFlushLength(pending, rows), (long)flushLength.Invoke(runtime, [])!);
                }
                else
                {
                    Assert.Equal(OldPendingFlushLength(pending, rows), (long)flushLength.Invoke(runtime, [])!);
                    flush.Invoke(runtime, [false]);
                    foreach (var (row, text) in pending)
                        rows[row] = row == ThreadRuntimeStreamRow.Tool ? OldTrimOutput(rows[row] + text) : rows[row] + text;
                    pending.Clear();
                    foreach (var row in rows.Keys) Assert.Equal(rows[row], RowText(entries[row]), StringComparer.Ordinal);
                }
            }
            flush.Invoke(runtime, [false]);
            foreach (var (row, text) in pending) rows[row] = row == ThreadRuntimeStreamRow.Tool ? OldTrimOutput(rows[row] + text) : rows[row] + text;
            foreach (var row in rows.Keys) Assert.Equal(rows[row], RowText(entries[row]), StringComparer.Ordinal);
            Assert.True(rows[ThreadRuntimeStreamRow.Message].Length > 1_000, "the script streamed a real reply");
            Assert.StartsWith("…", rows[ThreadRuntimeStreamRow.Tool], StringComparison.Ordinal); // the script reached the trim
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void A_row_whose_text_is_replaced_or_split_mid_character_is_counted_as_before()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var runtime = CreateRuntime();
            var entry = AddEntry(runtime, "m", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "", IsStreaming = true }));
            SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1000));
            var flushLength = Method("PendingFlushLength");
            var flush = Method("FlushDeltas");
            void Step(string delta, string expectedRow)
            {
                Method("QueueDelta").Invoke(runtime, ["m", ThreadRuntime.DeltaKind.Message, delta]);
                var oracle = OldPendingFlushLength(
                    new Dictionary<ThreadRuntimeStreamRow, string> { [ThreadRuntimeStreamRow.Message] = delta },
                    new Dictionary<ThreadRuntimeStreamRow, string> { [ThreadRuntimeStreamRow.Message] = RowText(entry) });
                Assert.Equal(oracle, (long)flushLength.Invoke(runtime, [])!);
                flush.Invoke(runtime, [false]);
                Assert.Equal(expectedRow, RowText(entry), StringComparer.Ordinal);
            }
            void Replace(string text) => entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = true }),
            };

            Step("\U0001F1FA\U0001F1F8\U0001F1FA", "\U0001F1FA\U0001F1F8\U0001F1FA");
            Step("\U0001F1F8e", "\U0001F1FA\U0001F1F8\U0001F1FA\U0001F1F8e");

            // A completion (or any other writer) swaps the text for one that does not extend the counted one.
            Replace("é́x");
            Step("́", "é́x́");
            Step("y", "é́x́y");
            Replace("é́x́z"); // same start, different end
            Step("‍\U0001F469", "é́x́z‍\U0001F469");
            Replace("\U0001F469‍"); // shorter
            Step("\U0001F469", "\U0001F469‍\U0001F469");
            Replace("\U0001F1FA\uD83C"); // ends in half a regional indicator
            Step("\uDDF8\U0001F1FA", "\U0001F1FA\U0001F1F8\U0001F1FA");
            Step("\uD83C", "\U0001F1FA\U0001F1F8\U0001F1FA\uD83C");
            Step("\uDDF8", "\U0001F1FA\U0001F1F8\U0001F1FA\U0001F1F8");
            Step("\r", "\U0001F1FA\U0001F1F8\U0001F1FA\U0001F1F8\r");
            Step("\n", "\U0001F1FA\U0001F1F8\U0001F1FA\U0001F1F8\r\n");
        });
    }

    // ED-NEW-lean-5 review fix: the per-row count memo holds no string of its own (a 100k-character reply must not
    // keep a second near-complete copy of its text for the thread's lifetime) and is handed on to the flushed row
    // text, so the next count is one reference compare plus a walk of the newest Characters, not of the whole row.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void The_count_memo_keeps_no_row_text_alive_and_resumes_on_the_flushed_text()
    {
        using var test = new TestRuntime();
        test.OnMain(() =>
        {
            var memoType = typeof(ThreadRuntime).GetNestedType("StreamedCount", BindingFlags.NonPublic);
            Assert.NotNull(memoType);
            Assert.DoesNotContain(memoType!.GetFields(InstanceMembers), field => field.FieldType == typeof(string));

            var runtime = CreateRuntime();
            var entry = AddEntry(runtime, "m", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "", IsStreaming = true }));
            SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1000));
            var streamed = StreamThenComplete(runtime, entry);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(streamed.TryGetTarget(out _), "the streamed row text outlived its row only through the count memo");
            Assert.Equal("done", RowText(entry), StringComparer.Ordinal);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<string> StreamThenComplete(ThreadRuntime runtime, TimelineEntry entry)
    {
        var flushLength = Method("PendingFlushLength");
        var flush = Method("FlushDeltas");
        var expected = "";
        for (var i = 0; i < 4; i++)
        {
            var delta = new string('a', 1_000) + "\U0001F469\u200D\U0001F469" + i;
            Method("QueueDelta").Invoke(runtime, ["m", ThreadRuntime.DeltaKind.Message, delta]);
            Assert.Equal(OldCharacters(expected).LongCount() + OldCharacters(delta).LongCount(), (long)flushLength.Invoke(runtime, [])!);
            flush.Invoke(runtime, [false]);
            expected += delta;
            Assert.Equal(expected, RowText(entry), StringComparer.Ordinal);

            // The flush handed the memo on to the new row text: it covers the text counted before the flush only.
            var memo = Memo(runtime, entry);
            Assert.True(MemoField<WeakReference<string>>(memo, "Text").TryGetTarget(out var target));
            Assert.Same(RowText(entry), target);
            Assert.Equal(expected.Length - delta.Length, MemoField<int>(memo, "Length"));
        }

        // The completion swaps in a text that does not extend the streamed one; nothing but the memo saw the old one.
        var streamed = new WeakReference<string>(RowText(entry));
        Method("QueueDelta").Invoke(runtime, ["m", ThreadRuntime.DeltaKind.Message, "z"]);
        Assert.Equal(OldCharacters(expected).LongCount() + 1, (long)flushLength.Invoke(runtime, [])!);
        Assert.Equal(expected.Length, MemoField<int>(Memo(runtime, entry), "Length"));
        flush.Invoke(runtime, [false]);
        entry.Item = entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = "done", IsStreaming = false }),
        };
        return streamed;
    }

    private static object Memo(ThreadRuntime runtime, TimelineEntry entry)
    {
        var table = typeof(ThreadRuntime).GetField("streamedCounts", InstanceMembers)!.GetValue(runtime);
        Assert.NotNull(table);
        var args = new object?[] { entry, null };
        Assert.True((bool)table!.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        return args[1]!;
    }

    private static T MemoField<T>(object memo, string name) =>
        (T)memo.GetType().GetField(name, InstanceMembers)!.GetValue(memo)!;

    private static string RandomDelta(Random random, int maxPieces)
    {
        var builder = new StringBuilder();
        var count = random.Next(maxPieces + 1);
        for (var i = 0; i < count; i++) builder.Append(Pieces[random.Next(Pieces.Length)]);
        return builder.ToString();
    }

    private static string RowText(TimelineEntry entry) => entry.Item.TimelineItemContent switch
    {
        TimelineItem.Content.Assistant assistant => assistant.Value.Text,
        TimelineItem.Content.Reasoning reasoning => reasoning.Value.Text,
        TimelineItem.Content.Plan plan => plan.Value.Markdown,
        TimelineItem.Content.Tool tool => tool.Value.Output,
        _ => throw new InvalidOperationException(),
    };

    // ---- Frozen copy of the old code (ThreadRuntime.cs:4476-4494 and Timeline.cs:196-199 before ED-NEW-lean-5) ----

    private static long OldPendingFlushLength(Dictionary<ThreadRuntimeStreamRow, string> pending, Dictionary<ThreadRuntimeStreamRow, string> rows)
    {
        long longest = 0;
        foreach (var (row, text) in pending)
        {
            if (row == ThreadRuntimeStreamRow.Tool) continue;
            longest = Math.Max(longest, OldCharacters(rows[row]).LongCount() + OldCharacters(text).LongCount());
        }
        return longest;
    }

    private static IEnumerable<string> OldCharacters(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
            yield return text.Substring(index, length);
            index += length;
        }
    }

    private static string OldTrimOutput(string output)
    {
        if (Encoding.UTF8.GetByteCount(output) <= ToolCall.OutputLimit) return output;
        var starts = new List<int>();
        for (var index = 0; index < output.Length; index += StringInfo.GetNextTextElementLength(output.AsSpan(index))) starts.Add(index);
        var keep = (int)(ToolCall.OutputLimit / 2);
        return "…" + (keep >= starts.Count ? output : output[starts[starts.Count - keep]..]);
    }

    // ---- Runtime set-up (as ThreadRuntimePart7Tests) ----

    private static MethodInfo Method(string name)
    {
        var method = typeof(ThreadRuntime).GetMethod(name, InstanceMembers);
        Assert.NotNull(method);
        return method!;
    }

    private static ThreadRuntime CreateRuntime()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        SetField(runtime, "entries", ValueList<TimelineEntry>.Empty);
        SetField(runtime, "turns", ValueList<TurnRecord>.Empty);
        SetField(runtime, "followUps", ValueList<FollowUpPrompt>.Empty);
        SetField(runtime, "skillsFollowUps", new HashSet<Guid>());
        SetField(runtime, "approvals", ValueList<ApprovalRequest>.Empty);
        SetField(runtime, "questions", ValueList<QuestionRequest>.Empty);
        SetField(runtime, "entryIndex", new Dictionary<string, TimelineEntry>(SwiftStringComparer.Instance));
        SetField(runtime, "phase", RuntimePhase.Idle);
        SetField(runtime, "saveRevision", 0L);
        SetField(runtime, "persistenceEnabled", false);
        SetField(runtime, "currentTurnID", null);
        var pendingDeltaType = typeof(ThreadRuntime).GetNestedType("PendingDelta", BindingFlags.NonPublic);
        Assert.NotNull(pendingDeltaType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), pendingDeltaType!);
        SetField(runtime, "pendingDeltas", Activator.CreateInstance(dictionaryType)!);
        return runtime;
    }

    private static TimelineEntry AddEntry(ThreadRuntime runtime, string id, TimelineItem.Content content)
    {
        var item = new TimelineItem(Guid.NewGuid(), content, id, new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Method("Append").Invoke(runtime, [item]);
        return Assert.Single(runtime.Entries, entry => entry.Id == id);
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, InstanceMembers);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
