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
            var items = new List<TimelineItem>();
            var records = new List<TurnRecord>();
            for (var index = 0; index < reports.Length; index++)
            {
                var turn = Guid.NewGuid();
                var started = now.AddMinutes(-30 + index * 5);
                var user = new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Hand it to the heads." }), date: started);
                items.Add(user);
                items.Add(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage
                {
                    Text = reports[index].Text,
                    HydraHeads = reports[index].Batch ? new ValueList<long>([0, 1, 2, 3]) : ValueList<long>.Empty,
                }), date: started.AddSeconds(8)));
                items.Add(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Their report is in." }), date: started.AddSeconds(10)));
                items.Add(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary
                {
                    TurnID = turn, Status = TurnStatus.Completed, Duration = 10, FilesChanged = 0, Additions = 0, Deletions = 0,
                }), date: started.AddSeconds(11)));
                records.Add(new TurnRecord { Id = turn, Index = index, Status = TurnStatus.Completed, StartedAt = started, CompletedAt = started.AddSeconds(10), UserItemID = user.Id });
            }
            var library = new Library() with { Projects = new ValueList<Project>([project]), Threads = new ValueList<ChatThread>([chat]) };
            DiskWriter.Shared.WriteSynchronously(library, Storage.LibraryURL);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(thread) { Items = new ValueList<TimelineItem>(items), Turns = new ValueList<TurnRecord>(records) }, Storage.ThreadURL(thread));
        });
        return thread;
    }

    private static readonly FieldInfo BlockCache = typeof(MarkdownView).GetField("blockCache", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static bool Cached(string text) => (bool)BlockCache.FieldType.GetMethod("Contains")!.Invoke(BlockCache.GetValue(null), [text])!;

    private static void EmptyTheCache() => BlockCache.SetValue(null, Activator.CreateInstance(BlockCache.FieldType, 800));

    // The first tap on a pill: the longest gap between two frames of the real frame clock in the second after the real click, and the usual gap before it.
    private (double Usual, double Tap) FirstTap(AvaloniaWindow window, Button pill, Popup popup)
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
        var pressed = Rig.Tap(native, window, pill);
        N.Until(() => native.OnUi(() => popup.IsOpen), "Tapping the report pill did not open its popover.", 5000);
        Thread.Sleep(1200);
        List<long> taken = native.OnUi(() => { running = false; return stamps.ToList(); });
        var gaps = new List<(long At, double Ms)>();
        for (var i = 1; i < taken.Count; i++) gaps.Add((taken[i], (taken[i] - taken[i - 1]) * 1000.0 / Stopwatch.Frequency));
        var before = gaps.Where(gap => gap.At < pressed).Select(gap => gap.Ms).OrderBy(ms => ms).ToList();
        var tap = gaps.Where(gap => gap.At >= pressed && gap.At < pressed + Stopwatch.Frequency).Select(gap => gap.Ms).DefaultIfEmpty(double.NaN).Max();
        return (before.Count == 0 ? double.NaN : before[before.Count / 2], tap);
    }

    private static void CloseThePopover()
    {
        Rig.Press(Rig.Escape);
        Thread.Sleep(300);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-145")] // TimelineRows.swift:317-324 the warm on appear, :598-617 the popover's blocks built in its initializer
    public void A_warmed_heads_report_opens_within_one_frame_of_the_first_tap() => native.Run(subject =>
    {
        var batch = Batch("alpha");
        var warmNote = Note("bravo");
        var coldNote = Note("charlie");
        var thread = SeedReports((batch, true), (warmNote, false), (coldNote, false));
        var (window, _) = Rig.Open(native, subject, thread);
        Rig.Watch(native, window);
        N.Until(() => Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == PillTip).Count == 3, "The thread's three report pills never appeared.", 30000);
        var pills = Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == PillTip);
        var popups = native.OnUi(() => pills.Select(pill => ((Panel)pill.Parent!).Children.OfType<Popup>().First()).ToList());
        // The warm is let to finish: every body is in the parse cache.
        var bodies = new[] { warmNote.Split('\n', 2)[1].Trim(), coldNote.Split('\n', 2)[1].Trim() }
            .Concat(HydraReportDigest.Parse(batch).Heads.Select(head => head.Body)).ToList();
        N.Until(() => native.OnUi(() => bodies.All(Cached)), "The warm of the three reports never finished.", 60000);

        var heads = FirstTap(window, pills[0], popups[0]);
        CloseThePopover();
        var warm = FirstTap(window, pills[1], popups[1]);
        CloseThePopover();
        native.OnUi(EmptyTheCache);
        var cold = FirstTap(window, pills[2], popups[2]);
        CloseThePopover();
        Console.WriteLine($"TAP heads report (warm): usual gap {heads.Usual:F1} ms, tap frame {heads.Tap:F1} ms");
        Console.WriteLine($"TAP note (warm): usual gap {warm.Usual:F1} ms, tap frame {warm.Tap:F1} ms");
        Console.WriteLine($"TAP note (cold, parse cache emptied): usual gap {cold.Usual:F1} ms, tap frame {cold.Tap:F1} ms");
        Assert.True(heads.Tap - heads.Usual <= FrameBudget, $"The warmed heads report's tap frame took {heads.Tap:F1} ms ({heads.Usual:F1} ms between frames before it): over one frame's budget of {FrameBudget:F1} ms.");
        Assert.True(warm.Tap - warm.Usual <= FrameBudget, $"The warmed note's tap frame took {warm.Tap:F1} ms ({warm.Usual:F1} ms between frames before it): over one frame's budget of {FrameBudget:F1} ms.");
        Assert.True(cold.Tap > warm.Tap, $"The cold tap ({cold.Tap:F1} ms) was not slower than the warmed one ({warm.Tap:F1} ms): the baseline does not tell a warmed tap from a cold one.");
    });
}
