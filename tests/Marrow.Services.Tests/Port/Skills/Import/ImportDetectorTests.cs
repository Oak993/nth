using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118 addendum): Marrow finds the apps that keep skills by itself, without a picked folder, when the page opens and when those folders change. It only reports; it never imports.
public sealed class ImportDetectorTests
{
    private sealed class FakeWatch : IFolderWatch
    {
        public List<IReadOnlyList<WatchTarget>> Started { get; } = [];
        public int Disposed { get; private set; }
        public Action? Changed { get; private set; }

        public IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed)
        {
            Started.Add(targets);
            Changed = changed;
            return new Gone(this);
        }

        private sealed class Gone(FakeWatch owner) : IDisposable
        {
            public void Dispose() => owner.Disposed++;
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig(string? state = null)
        {
            Home = new ForeignHome();
            Time = new ManualTimeProvider();
            Library = new ImportedSkillsLibrary(Home.SkillsRoot, Time);
            Watch = new FakeWatch();
            State = state ?? Path.Combine(Home.Root, "marrow-data", "skills", "imported", "dismissed.json");
            Detector = Make();
        }

        public ForeignHome Home { get; }
        public ManualTimeProvider Time { get; }
        public ImportedSkillsLibrary Library { get; }
        public FakeWatch Watch { get; }
        public string State { get; }
        public ImportDetector Detector { get; private set; }
        public int Scans { get; private set; }

        public ImportDetector Make() => Detector = new ImportDetector(() => { Scans++; return new ForeignSkillScanner(Home.Home).Scan(); }, Library, Home.Home, Watch, State, Time);

        public void Fill()
        {
            Home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
            Home.Skill(".claude/skills", "beta", "beta", "Beta skill.");
            Home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
        }

        public void Dispose()
        {
            Detector.Dispose();
            Home.Dispose();
        }
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Refresh_finds_the_skills_of_installed_apps_on_its_own_with_no_picked_folder()
    {
        using var rig = new Rig();
        Assert.False(rig.Detector.Current.Visible);
        rig.Fill();
        rig.Detector.Refresh();
        var found = rig.Detector.Current;
        Assert.True(found.Visible);
        Assert.Equal(3, found.Count);
        Assert.Equal([ForeignApp.ClaudeCode, ForeignApp.Codex], found.Apps);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Nothing_found_means_nothing_to_say()
    {
        using var rig = new Rig();
        rig.Detector.Refresh();
        Assert.False(rig.Detector.Current.Visible);
        Assert.Equal(0, rig.Detector.Current.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Nothing_is_ever_imported_or_written_in_the_other_apps_by_detecting()
    {
        using var rig = new Rig();
        rig.Fill();
        var before = ForeignHome.Fingerprint(rig.Home.Home);
        rig.Detector.Refresh();
        rig.Detector.Start();
        rig.Watch.Changed!();
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(rig.Library.List());
        Assert.Equal(before, ForeignHome.Fingerprint(rig.Home.Home));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_what_is_not_yet_imported_counts_and_the_line_goes_when_everything_is_in()
    {
        using var rig = new Rig();
        rig.Fill();
        rig.Detector.Refresh();
        var skills = new ForeignSkillScanner(rig.Home.Home).Scan().Skills;
        rig.Library.Import(skills.Where(s => s.Name == "alpha"), ImportMode.Copy, _ => DuplicateChoice.Skip);
        Assert.Equal(2, rig.Detector.Current.Count);
        Assert.Equal([ForeignApp.ClaudeCode, ForeignApp.Codex], rig.Detector.Current.Apps);
        rig.Library.Import(skills.Where(s => s.Name == "gamma"), ImportMode.Link, _ => DuplicateChoice.Skip);
        Assert.Equal([ForeignApp.ClaudeCode], rig.Detector.Current.Apps);
        rig.Library.Import(skills.Where(s => s.Name == "beta"), ImportMode.Copy, _ => DuplicateChoice.Skip);
        Assert.False(rig.Detector.Current.Visible);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Dismiss_hides_the_line_keeps_it_hidden_after_a_restart_and_a_new_skill_brings_it_back()
    {
        using var rig = new Rig();
        rig.Fill();
        rig.Detector.Refresh();
        rig.Detector.Dismiss();
        Assert.False(rig.Detector.Current.Visible);
        rig.Detector.Refresh();
        Assert.False(rig.Detector.Current.Visible);
        var again = rig.Make();
        again.Refresh();
        Assert.False(again.Current.Visible);
        rig.Home.Skill(".cursor/skills", "fresh", "fresh", "Fresh skill.");
        again.Refresh();
        Assert.Equal((1, [ForeignApp.Cursor]), (again.Current.Count, again.Current.Apps.ToArray()));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unreadable_dismissed_file_is_ignored_and_a_missing_state_folder_is_made_on_dismiss()
    {
        using var rig = new Rig();
        rig.Fill();
        Directory.CreateDirectory(Path.GetDirectoryName(rig.State)!);
        File.WriteAllText(rig.State, "{ not json");
        var detector = rig.Make();
        detector.Refresh();
        Assert.True(detector.Current.Visible);
        File.Delete(rig.State);
        Directory.Delete(Path.GetDirectoryName(rig.State)!, true);
        detector.Dismiss();
        Assert.True(File.Exists(rig.State));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Changed_is_raised_when_what_was_found_changes_and_not_when_it_is_the_same()
    {
        using var rig = new Rig();
        var raised = 0;
        rig.Detector.Changed += () => raised++;
        rig.Detector.Refresh();
        Assert.Equal(0, raised);
        rig.Fill();
        rig.Detector.Refresh();
        Assert.Equal(1, raised);
        rig.Detector.Refresh();
        Assert.Equal(1, raised);
        rig.Detector.Dismiss();
        Assert.Equal(2, raised);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_scan_that_fails_keeps_what_was_known_and_does_not_throw()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
        var fail = false;
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        using var detector = new ImportDetector(() => fail ? throw new IOException("gone") : new ForeignSkillScanner(home.Home).Scan(), library, home.Home);
        detector.Refresh();
        fail = true;
        detector.Refresh();
        Assert.Equal(1, detector.Current.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task RefreshAsync_scans_off_the_calling_thread_and_reports_the_same()
    {
        using var rig = new Rig();
        rig.Fill();
        await rig.Detector.RefreshAsync();
        Assert.Equal(3, rig.Detector.Current.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_change_in_the_watched_folders_scans_again_once_after_things_settle()
    {
        using var rig = new Rig();
        rig.Detector.Start();
        Assert.True(rig.Detector.Watching);
        Assert.Single(rig.Watch.Started);
        rig.Detector.Refresh();
        var scans = rig.Scans;
        rig.Fill();
        rig.Watch.Changed!();
        rig.Time.Advance(TimeSpan.FromMilliseconds(300));
        rig.Watch.Changed!();
        rig.Watch.Changed!();
        rig.Time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(scans, rig.Scans); // each change starts the wait again
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(scans + 1, rig.Scans);
        Assert.Equal(3, rig.Detector.Current.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void After_a_change_the_watch_is_made_again_so_a_folder_that_has_just_appeared_is_watched()
    {
        using var rig = new Rig();
        rig.Detector.Start();
        rig.Fill();
        rig.Watch.Changed!();
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, rig.Watch.Started.Count);
        Assert.Equal(1, rig.Watch.Disposed);
        Assert.Contains(rig.Watch.Started[1], t => t.Deep && t.Folder == Path.Combine(rig.Home.Home, ".claude", "skills"));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Stop_ends_the_watch_and_later_changes_do_nothing()
    {
        using var rig = new Rig();
        rig.Detector.Start();
        var changed = rig.Watch.Changed!;
        rig.Detector.Stop();
        Assert.False(rig.Detector.Watching);
        Assert.Equal(1, rig.Watch.Disposed);
        var scans = rig.Scans;
        changed();
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(scans, rig.Scans);
        rig.Detector.Start();
        Assert.Equal(2, rig.Watch.Started.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Watch_targets_follow_what_exists_the_skills_folder_itself_or_the_nearest_folder_that_exists()
    {
        using var home = new ForeignHome();
        Directory.CreateDirectory(Path.Combine(home.Home, ".claude", "skills"));
        Directory.CreateDirectory(Path.Combine(home.Home, ".codex"));
        var targets = ForeignWatchTargets.For(home.Home);
        Assert.Contains(targets, t => t.Deep && t.Folder == Path.Combine(home.Home, ".claude", "skills"));
        Assert.Contains(targets, t => !t.Deep && t.Folder == Path.Combine(home.Home, ".codex") && t.Names!.Contains("skills"));
        Assert.Contains(targets, t => !t.Deep && t.Folder == home.Home && t.Names!.Contains(".cursor") && t.Names.Contains(".gemini"));
        Assert.DoesNotContain(targets, t => t.Deep && t.Folder == home.Home);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_system_watch_reports_a_new_skill_file_in_a_real_folder_and_nothing_after_it_is_disposed()
    {
        using var home = new ForeignHome();
        var skills = Path.Combine(home.Home, ".claude", "skills");
        Directory.CreateDirectory(skills);
        var hits = 0;
        var watch = new SystemFolderWatch().Watch(ForeignWatchTargets.For(home.Home), () => Interlocked.Increment(ref hits));
        home.Skill(".claude/skills", "one", "one", "One.");
        for (var i = 0; i < 300 && Volatile.Read(ref hits) == 0; i++) await Task.Delay(20);
        Assert.True(Volatile.Read(ref hits) > 0);
        watch.Dispose();
        await Task.Delay(200);
        var after = Volatile.Read(ref hits);
        home.Skill(".claude/skills", "two", "two", "Two.");
        await Task.Delay(300);
        Assert.Equal(after, Volatile.Read(ref hits));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_quiet_line_names_the_count_and_the_apps_in_plain_words()
    {
        Assert.Equal("Found 12 skills in Claude Code and Codex", ImportWords.Found(12, [ImportWords.ClaudeCode, ImportWords.Codex]));
        Assert.Equal("Found 1 skill in Cursor", ImportWords.Found(1, [ImportWords.Cursor]));
        Assert.Equal("Found 5 skills in Claude Code, Codex and Cursor", ImportWords.Found(5, [ImportWords.ClaudeCode, ImportWords.Codex, ImportWords.Cursor]));
    }
}
