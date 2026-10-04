using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792) :937-952 HydraMergeRow and DroppyCode/App/AppModel+HydraMerge.swift (B3) :79-187
// autoMergeHydraWork, :226-237 fail(), :244-249 and :302-304 the deferrals. PR-HYDRAMERGE-089: the merging pill vanishes when the flag goes down without an
// outcome note. Each silent path of the coordinator is driven for real (a temporary repository, the real coordinator on the manual main loop) with a real
// HydraMergeRow mounted over the lead's runtime from before the merge starts: the pill is drawn while the merge runs and is gone, with no note in its
// place, when the coordinator lets go. Only the transition was proven before (the flag lowered by hand); the paths are the coordinator's.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbMergeSilentTests(HeadlessSession headless)
{
    private static void LoadLogin() => Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult(); // the harness' bootstrap waits for it, which the UI thread cannot do

    private static HydraMergeReviewFixture Review(bool origin = true, string touched = "a.txt")
    {
        LoadLogin();
        return new HydraMergeReviewFixture(origin, touched);
    }

    // Mounts the row first (the flag is down: nothing is drawn), runs the coordinator once, lets the glide spring finish, and says what the pill did.
    // `run` starts the coordinator with an observer that sees every shell call.
    private static (bool Appeared, bool Gone) Drive(AppModelHarness h, ThreadRuntime runtime, Action<Action<ShellInvocation>> run)
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            row = new HydraMergeRow(runtime, new HydraMergePhase.Merging());
            window = Show(row);
            Assert.Empty(Mounted<HydraMergeTrack>(row)); // :941-943 no merge under way: no pill
        });
        var appeared = false;
        // The pill is put into the row's own panel when the flag goes up (:944-946): seen as it is, whatever the coordinator is doing at that moment.
        var host = Assert.IsAssignableFrom<Panel>(row.Child);
        host.Children.CollectionChanged += (_, _) => appeared |= host.Children.Count > 0;
        run(_ => appeared |= Mounted<HydraMergeTrack>(row).Any());
        h.Main.Loop.RunUntilIdle(); // the row reads the lowered flag: its removal starts
        // The pill's removal glides on the glide spring (:949-952): let it finish.
        frames.Advance(TimeSpan.Zero);
        h.Main.AdvanceMilliseconds(2000);
        frames.Advance(TimeSpan.FromMilliseconds(2000));
        var gone = false;
        h.Main.OnMain(() =>
        {
            Settle(window);
            gone = !Mounted<HydraMergeTrack>(row).Any() && Texts(row).Length == 0;
            if (!gone) Assert.Fail($"tracks {Mounted<HydraMergeTrack>(row).Count()}, texts [{string.Join("|", Texts(row))}], merging {runtime.IsHydraMerging}");
            Assert.False(runtime.IsHydraMerging);
            Close(window);
        });
        return (appeared, gone);
    }

    private static void Silent((bool Appeared, bool Gone) pill, int notesBefore, int notesAfter)
    {
        Assert.True(pill.Appeared, "the merging pill was never drawn while the coordinator ran");
        Assert.True(pill.Gone);
        Assert.Equal(notesBefore, notesAfter); // no outcome note took the pill's place
    }

    [Theory]
    [InlineData("MERGE_HEAD")]
    [InlineData("rebase-merge")]
    [Trait("Req", "PR-HYDRAMERGE-089")] // TimelineRows.swift:937-952; AppModel+HydraMerge.swift:244-249 a checkout mid-merge or mid-rebase defers the pass quietly
    public void A_checkout_in_the_middle_of_a_merge_or_a_rebase_defers_the_pass_and_the_pill_goes_with_no_note(string marker) => headless.OnUiThread(() =>
    {
        using var f = Review();
        f.Edit();
        var path = Path.Combine(f.Repo.Root, ".git", marker);
        if (marker == "MERGE_HEAD") File.WriteAllText(path, f.Repo.Git("rev-parse", "HEAD")); else Directory.CreateDirectory(path);
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(observer));
        Silent(pill, before, f.Notes.Length);
        Assert.True(f.Runtime.HasHydraMergeRetryPending); // retried in a minute, quietly
        Assert.Empty(f.Runtime.HydraMerges);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:296-304 a lead that took up work again during the capture defers the pass
    public void A_lead_that_takes_up_work_again_while_the_team_is_gathered_defers_the_pass_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review();
        f.Edit();
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(call =>
        {
            observer(call);
            if (HydraMergeReviewFixture.IsGit(call, "write-tree")) typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime, true);
        }));
        Silent(pill, before, f.Notes.Length);
        Assert.Empty(f.Runtime.HydraMerges);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:97-117 after the landing a lead that is no longer ready ends the run silently (PR-HYDRAMERGE-111)
    public void A_lead_that_stops_being_ready_while_a_head_lands_ends_the_run_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.AddHead(lead, ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var gate = new TaskCompletionSource();
        f.H.Main.OnMain(() => f.Runtime(lead).HydraLanding = TaskHandle.Detached(async () => await gate.Task));
        var before = f.Notes(lead).Length;
        var pill = Drive(f.H, f.Runtime(lead), observer =>
        {
            Task merge = null!;
            f.H.Main.OnMain(() =>
            {
                using var scope = ShellObserver.Observe(_ => { });
                merge = f.Model.AutoMergeHydraWork(lead.Id);
            });
            Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).IsHydraMerging));
            f.H.Main.OnMain(() => typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime(lead), true)); // the lead stops being ready while the landing finishes
            observer(null!); // the pill is drawn by now: the flag is up and the loop has turned
            gate.SetResult();
            f.H.Main.Loop.RunUntil(() => merge.IsCompleted, AppModelHarness.Bound);
        });
        Silent(pill, before, f.Notes(lead).Length);
        Assert.Empty(f.Runtime(lead).HydraMerges);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:229-231 the same failure again is not news: the second pass records and announces nothing
    public void The_same_failure_again_records_and_announces_nothing_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review(origin: false);
        f.Edit();
        f.Run(); // the first try: an entry and a note, the pill's place taken by the outcome
        Assert.Single(f.Notes);
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(observer));
        Silent(pill, before, f.Notes.Length);
        Assert.Single(f.Runtime.HydraMerges); // not a second record
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:232-235 a failure that clears by itself is said only once it has kept failing, never on the first try
    public void A_first_retryable_failure_is_recorded_without_a_note_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review();
        f.Edit();
        f.Forge.Reply(0, stderr: "offline create", status: "1");
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(observer));
        Silent(pill, before, f.Notes.Length);
        var record = Assert.Single(f.Runtime.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        Assert.True(record.Retryable);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:292 a checkout that is no repository fails the pass with nothing said
    public void A_checkout_that_is_not_a_repository_fails_the_pass_with_nothing_said_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review();
        f.Edit();
        var git = Path.Combine(f.Repo.Root, ".git");
        foreach (var file in Directory.EnumerateFiles(git, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(git, recursive: true);
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(observer));
        Silent(pill, before, f.Notes.Length);
        Assert.Empty(f.Runtime.HydraMerges);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:184-186, :902-906 an .alreadyOnMain run settles with a note only when nothing is left unmerged: a lead with a file still unmerged gets no settled entry and no note, and the pill goes
    public void An_already_on_the_branch_run_that_leaves_a_file_unmerged_posts_no_settlement_note_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review(); // a.txt is committed and unchanged: the team's tree is the branch's tree (:308 .alreadyOnMain)
        var before = f.Notes.Length;
        var late = false;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(call =>
        {
            observer(call);
            // A turn of the lead's that did not finish well (it failed, so the next pass has no finished work to take up: the sweep picks up only completed turns) is recorded with
            // a file of the project while the pass is capturing. It is not among the turns this pass settles (they were read at the start), so after the pass marks its own
            // turn spent one file is still unmerged and recordHydraSettlement returns before it writes anything (:903). It sits before the last turn, which stays completed.
            if (!late && HydraMergeReviewFixture.IsGit(call, "write-tree"))
            {
                late = true;
                typeof(ThreadRuntime).GetProperty("Turns")!.SetValue(f.Runtime, new ValueList<TurnRecord>(
                    [new TurnRecord { Id = Guid.NewGuid(), Index = 0, Status = TurnStatus.Failed, CompletedAt = SwiftDate.Now, TouchedPaths = new ValueList<string>(["b.txt"]) }, .. f.Runtime.Turns]));
            }
        }));
        Assert.True(late, "the pass never reached its capture");
        Silent(pill, before, f.Notes.Length);
        Assert.Empty(f.Runtime.HydraMerges); // no settled entry either
        Assert.Contains(f.Runtime.Turns, turn => turn.Id == f.Turn.Id && turn.HydraMerged); // the pass's own turn is spent
        Assert.Contains(f.Runtime.Turns, turn => turn.Index == 0 && !turn.HydraMerged); // what the pass did not take stays unmerged
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // AppModel+HydraMerge.swift:148-160 nothing to send and nothing outside a project: whatever the turns recorded is marked spent, with no word
    public void Work_that_is_already_on_the_branch_or_gone_is_marked_spent_with_no_word_and_the_pill_goes_with_no_note() => headless.OnUiThread(() =>
    {
        using var f = Review(touched: "gone.txt");
        var before = f.Notes.Length;
        var pill = Drive(f.H, f.Runtime, observer => f.Run(observer));
        Silent(pill, before, f.Notes.Length);
        Assert.Empty(f.Runtime.HydraMerges);
        Assert.True(f.Runtime.Turns.Single().HydraMerged);
    });
}
