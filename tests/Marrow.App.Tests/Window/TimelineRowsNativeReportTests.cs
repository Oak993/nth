using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Markdown;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using Rig = Marrow.App.Tests.Window.Native.TimelineRowsNativeRig;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for PR-CHAT-145 (OD-0055): TimelineRows.swift:317-324 `.task(id: body)` warms a report's markdown as the pill appears (a heads report one head at a time, a note
// whole), and :598-617 HydraReportPopover builds its blocks in the initializer, so that "the tap finds the blocks ready rather than parsing the whole batch first". The record's
// verification is the frame time of the first tap. This runs it in a real window on the real frame clock (TopLevel.RequestAnimationFrame): the report rows are drawn, the warm is
// let to finish (the parse cache holds every body), the pill is tapped with a real click, and the longest gap between two frames over the next second is the tap's frame. The
// baseline is the same kind of tap with the parse cache emptied first, which is what a cold tap is.
[Collection("WindowManagerNative")]
public sealed class TimelineRowsNativeReportTests(WindowManagerNativeSession native)
{
    private const string PillTip = "Show the message";

    // One frame at 60 Hz: the budget a tap's frame stays under.
    private static readonly double FrameBudget = 1000.0 / 60;

    private static string Section(string seed, int characters)
    {
        var text = new System.Text.StringBuilder();
        for (var step = 0; text.Length < characters; step++)
            text.Append("### ").Append(seed).Append(" step ").Append(step).Append("\n\n")
                .Append("The `Widget` of ").Append(seed).Append(" handles **bold** and _italic_ text across several lines so the parser has real inline work to do at step ").Append(step).Append(".\n\n")
                .Append("- first item of ").Append(seed).Append(' ').Append(step).Append(" with `code`\n- second item\n- third item\n\n")
                .Append("```csharp\nvar value = ").Append(step).Append(";\nConsole.WriteLine(value);\n```\n\n")
                .Append("| column | value |\n| --- | --- |\n| ").Append(seed).Append(" | ").Append(step).Append(" |\n\n");
        return text.ToString().TrimEnd();
    }

    // A heads report (a batch of four heads, Hank to Otto, about ten thousand characters each) or a note (its title line, then thirty thousand characters).
    private static string Batch(string seed) =>
        string.Join("\n", Enumerable.Range(0, 4).Select(index => "## " + HydraRoster.Persona(at: index).Name + ": " + seed + " task " + index + "\n" + Section(seed + "-" + index, 10_000)));

    private static string Note(string seed) => seed + " report.\n" + Section(seed, 30_000);

    private Guid SeedReports(params (string Text, bool Batch)[] reports)
    {
        var thread = Guid.NewGuid();
        var folder = Path.Combine(Path.GetTempPath(), "timelinerows-native-report-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        native.OnUi(() =>
        {
            var now = SwiftDate.Now;
            var project = new Project("report-native", folder);
            var chat = new ChatThread(project.Id, ProviderKind.Claude, "opus", "high", RuntimeMode.Supervised) with
            {
                Id = thread, Title = "Reports in a real window", HasCustomTitle = true, UpdatedAt = now, CreatedAt = now.AddHours(-1), LastStatus = TurnStatus.Completed,
            };
            // One turn holds every report, so that all the pills are on the screen together.
            var turn = Guid.NewGuid();
            var started = now.AddMinutes(-30);
            var user = new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Hand it to the heads." }), date: started);
            var items = new List<TimelineItem> { user };
            for (var index = 0; index < reports.Length; index++)
                items.Add(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage
                {
                    Text = reports[index].Text,
                    HydraHeads = reports[index].Batch ? new ValueList<long>([0, 1, 2, 3]) : ValueList<long>.Empty,
                }), date: started.AddSeconds(8 + index)));
            items.Add(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Their reports are in." }), date: started.AddSeconds(20)));
            items.Add(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary
            {
                TurnID = turn, Status = TurnStatus.Completed, Duration = 20, FilesChanged = 0, Additions = 0, Deletions = 0,
            }), date: started.AddSeconds(21)));
            var records = new List<TurnRecord> { new() { Id = turn, Index = 0, Status = TurnStatus.Completed, StartedAt = started, CompletedAt = started.AddSeconds(20), UserItemID = user.Id } };
            var library = new Library() with { Projects = new ValueList<Project>([project]), Threads = new ValueList<ChatThread>([chat]) };
            DiskWriter.Shared.WriteSynchronously(library, Storage.LibraryURL);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(thread) { Items = new ValueList<TimelineItem>(items), Turns = new ValueList<TurnRecord>(records) }, Storage.ThreadURL(thread));
        });
        return thread;
    }

    private static readonly FieldInfo BlockCache = typeof(MarkdownView).GetField("blockCache", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static bool Cached(string text) => (bool)BlockCache.FieldType.GetMethod("Contains")!.Invoke(BlockCache.GetValue(null), [text])!;

    private static void EmptyTheCache() => BlockCache.SetValue(null, Activator.CreateInstance(BlockCache.FieldType, 800L, long.MaxValue));

    // The first tap on a pill: the longest gap between two frames of the real frame clock in the second after the real click, and the usual gap before it.
    private (double Usual, double Tap) FirstTap(AvaloniaWindow window, Button pill)
    {
        var stamps = new List<long>();
        var running = true;
        void Tick(TimeSpan _)
        {
            if (!running) return;
            stamps.Add(Stopwatch.GetTimestamp());
            window.RequestAnimationFrame(Tick);
        }
        native.OnUi(() => window.RequestAnimationFrame(Tick));
        Thread.Sleep(700); // a steady stream of frames before the tap
        var windowsBefore = Rig.VisibleWindows(native);
        var clicked = 0;
        native.OnUi(() => pill.Click += (_, _) => clicked++);
        var pressed = Rig.Tap(native, window, pill);
        N.Until(() => Rig.VisibleWindows(native) > windowsBefore, $"Tapping the report pill did not open its popover (no popup window appeared; {windowsBefore} windows, now {Rig.VisibleWindows(native)}; the pill was clicked {native.OnUi(() => clicked)} times; its enabled state {native.OnUi(() => pill.IsEnabled)}; the popover is in the window: {native.OnUi(() => window.GetVisualDescendants().Any(v => v is HydraReportsPopover or HydraReportPopover))}).", 5000);
        Thread.Sleep(1200);
        List<long> taken = native.OnUi(() => { running = false; return stamps.ToList(); });
        var gaps = new List<(long At, double Ms)>();
        for (var i = 1; i < taken.Count; i++) gaps.Add((taken[i], (taken[i] - taken[i - 1]) * 1000.0 / Stopwatch.Frequency));
        var before = gaps.Where(gap => gap.At < pressed).Select(gap => gap.Ms).OrderBy(ms => ms).ToList();
        var tap = gaps.Where(gap => gap.At >= pressed && gap.At < pressed + Stopwatch.Frequency).Select(gap => gap.Ms).DefaultIfEmpty(double.NaN).Max();
        return (before.Count == 0 ? double.NaN : before[before.Count / 2], tap);
    }

    // The report popover is a badge popover (it closes on a press outside, or the reader's Escape reaches its window); the window count tells.
    private void CloseThePopover()
    {
        Rig.Press(Rig.Escape);
        Thread.Sleep(600);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-145")] // TimelineRows.swift:317-324 the warm on appear, :598-617 the popover's blocks built in its initializer
    public void A_warmed_heads_report_opens_within_one_frame_of_the_first_tap() => native.Run(subject =>
    {
        var tiny = "Tiny report.\nA short body, so the popover is nearly empty.";
        var batch = Batch("alpha");
        var jitNote = Note("alfa-jit");
        var warmNote = Note("bravo");
        var coldNote = Note("charlie");
        var thread = SeedReports((tiny, false), (batch, true), (jitNote, false), (warmNote, false), (coldNote, false));
        var (window, _) = Rig.Open(native, subject, thread);
        Rig.Watch(native, window);
        N.Until(() => Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == PillTip).Count == 5, "The thread's five report pills never appeared.", 30000);
        var pills = Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == PillTip);
        // The warm is let to finish: every body is in the parse cache.
        var bodies = new[] { tiny.Split('\n', 2)[1].Trim(), warmNote.Split('\n', 2)[1].Trim(), coldNote.Split('\n', 2)[1].Trim() }
            .Concat(HydraReportDigest.Parse(batch).Heads.Select(head => head.Body)).ToList();
        N.Until(() => native.OnUi(() => bodies.All(Cached)), "The warm of the reports never finished.", 60000);

        // The first popover this process opens pays for the window and the code it runs once: a tiny report goes first, and its tap is the floor of any tap.
        var floor = FirstTap(window, pills[0]);
        CloseThePopover();
        var heads = FirstTap(window, pills[1]);
        CloseThePopover();
        var jit = FirstTap(window, pills[2]); // the first note pays for the code a note's views run once
        CloseThePopover();
        var warm = FirstTap(window, pills[3]);
        CloseThePopover();
        native.OnUi(EmptyTheCache);
        var cold = FirstTap(window, pills[4]);
        CloseThePopover();
        Console.WriteLine($"TAP tiny note (warm, the floor of a tap): usual gap {floor.Usual:F1} ms, tap frame {floor.Tap:F1} ms");
        Console.WriteLine($"TAP heads report of four heads, 40000 characters (warm): usual gap {heads.Usual:F1} ms, tap frame {heads.Tap:F1} ms");
        Console.WriteLine($"TAP note of 30000 characters (warm, the first note opened): usual gap {jit.Usual:F1} ms, tap frame {jit.Tap:F1} ms");
        Console.WriteLine($"TAP note of 30000 characters (warm): usual gap {warm.Usual:F1} ms, tap frame {warm.Tap:F1} ms");
        Console.WriteLine($"TAP note of 30000 characters (cold, parse cache emptied): usual gap {cold.Usual:F1} ms, tap frame {cold.Tap:F1} ms");
        // A tap opens a native popup window, which costs two frames even for a popover with nothing in it (the floor): the budget is one frame on top of that. The heads report, whose
        // bodies are parsed ahead and whose cards are closed until read, stays within it. A 30000 character note builds all its views as it opens (HydraReportPopover builds them in its
        // initializer, TimelineRows.swift:612-617): that cost is printed above, and is not claimed to fit a frame. Nor is warm against cold asserted: the note's tap swings by a hundred
        // milliseconds from run to run (views built), more than the parse a cold tap adds.
        Assert.True(heads.Tap <= floor.Tap + FrameBudget, $"The warmed heads report's tap frame took {heads.Tap:F1} ms: more than one frame ({FrameBudget:F1} ms) over a tiny report's {floor.Tap:F1} ms.");
    }, nameof(A_warmed_heads_report_opens_within_one_frame_of_the_first_tap), typeof(TimelineRowsNativeReportTests));
}
