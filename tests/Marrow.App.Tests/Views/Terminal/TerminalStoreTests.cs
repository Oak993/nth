using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalStore.swift (B2 1fbf81e), :103-176, the store's bookkeeping.
// Every expectation below is transcribed from the Swift lines cited on each test and from the PR-TERM records; none
// comes from running the port. TerminalSession.init (:17-41) always starts a shell. The tests that read what a real
// shell is (its process, command line, directory, environment, prompt or exit) open a real PowerShell (OD-0008 C5) in a
// throwaway folder (WithRealStore); the tests of the store's bookkeeping open the same sessions on a stand-in shell, so
// that no PowerShell and conhost are started for them (WithStore). Every test ends with TerminateAll.
// The Swift stored property `sessions` (:106) and the method `sessions(for:)` (:109) cannot share one C# name; these
// tests read the store only through Sessions(threadID), Selected(threadID) and Selection (see the unit report).
[Collection("Headless")]
public sealed class TerminalStoreTests(HeadlessSession headless)
{
    private static void WithStore(Action<TerminalStore> body) => WithStore(realShell: false, body);

    private static void WithRealStore(Action<TerminalStore> body) => WithStore(realShell: true, body);

    private static void WithStore(bool realShell, Action<TerminalStore> body)
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var shells = realShell ? null : TerminalShells.Fake();
        var store = new TerminalStore();
        try
        {
            body(store);
        }
        finally
        {
            store.TerminateAll();
        }
    }

    private static List<string> Titles(TerminalStore store, Guid threadID) => [.. store.Sessions(threadID).Select(s => s.Title)];

    private static bool SelectionNames(TerminalStore store, Guid threadID, Guid sessionID) =>
        store.Selection.TryGetValue(threadID, out var selected) && selected == sessionID;

    // ---- PR-TERM-050: sessions per thread, and an empty list for a thread with none ------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-050")] // App/Views/Terminal/TerminalStore.swift:106-111
    public void Asking_for_a_thread_with_no_sessions_returns_an_empty_list_and_creates_nothing() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();

        Assert.Empty(store.Sessions(thread));   // :110 sessions[threadID] ?? []
        Assert.Empty(store.Sessions(thread));   // asking again still finds nothing: nothing was created
        Assert.False(store.Selection.ContainsKey(thread));
        Assert.Null(store.Selected(thread));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-050")] // App/Views/Terminal/TerminalStore.swift:106-111, :122
    public void Sessions_are_kept_per_thread_in_the_order_they_were_opened() => headless.OnUiThread(() => WithStore(store =>
    {
        var threadA = Guid.NewGuid();
        var threadB = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("per thread");

        var a1 = store.Open(threadA, directory);
        var b1 = store.Open(threadB, directory);
        var a2 = store.Open(threadA, directory, title: "Second");
        var a3 = store.Open(threadA, directory);

        var listA = store.Sessions(threadA);
        Assert.Equal(3, listA.Count);
        Assert.Same(a1, listA[0]);
        Assert.Same(a2, listA[1]);
        Assert.Same(a3, listA[2]);
        var listB = store.Sessions(threadB);
        Assert.Single(listB);
        Assert.Same(b1, listB[0]);
        Assert.All(listA, s => Assert.Equal(threadA, s.ThreadID));
        Assert.Equal(threadB, listB[0].ThreadID);
    }));

    // ---- PR-TERM-051: open() numbering, append and select ----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-051")] // App/Views/Terminal/TerminalStore.swift:118-125
    public void Open_names_untitled_tabs_Terminal_n_from_the_current_count_appends_and_selects_each() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("numbering");

        for (var n = 1; n <= 3; n++)
        {
            var session = store.Open(thread, directory);
            Assert.Equal("Terminal " + n.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Title); // :121 "Terminal \(number)"
            Assert.Same(session, store.Sessions(thread)[^1]);                                                         // :122 appended at the end
            Assert.True(SelectionNames(store, thread, session.Id));                                                   // :123 selected
        }
        Assert.Equal(new[] { "Terminal 1", "Terminal 2", "Terminal 3" }, Titles(store, thread));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-051")] // App/Views/Terminal/TerminalStore.swift:120-121
    public void Closing_tab_two_of_three_then_opening_makes_a_second_tab_named_Terminal_3() => headless.OnUiThread(() => WithStore(store =>
    {
        // The record's own verification: open three, close the second, open a fourth, two tabs named "Terminal 3".
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("duplicate names");
        store.Open(thread, directory);
        var second = store.Open(thread, directory);
        store.Open(thread, directory);

        store.Close(second);
        var fourth = store.Open(thread, directory); // :120 number = count (2) + 1

        Assert.Equal("Terminal 3", fourth.Title);
        Assert.Equal(new[] { "Terminal 1", "Terminal 3", "Terminal 3" }, Titles(store, thread));
        Assert.True(SelectionNames(store, thread, fourth.Id));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-051")] // App/Views/Terminal/TerminalStore.swift:119-121, :127-129
    public void An_explicit_title_is_used_as_given_and_still_counts_toward_the_next_number() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("explicit title");

        store.Open(thread, directory);
        var named = store.Open(thread, directory, title: "Build");
        store.Run(new ProjectScript(name: "Dev server", command: "Write-Output ready"), thread, directory); // :128 title: script.name
        var untitled = store.Open(thread, directory);
        var otherFirst = store.Open(otherThread, directory);

        Assert.Equal("Build", named.Title);
        Assert.Equal(new[] { "Terminal 1", "Build", "Dev server", "Terminal 4" }, Titles(store, thread));
        Assert.Equal("Terminal 4", untitled.Title);
        Assert.Equal("Terminal 1", otherFirst.Title); // the count is per thread
        Assert.True(SelectionNames(store, thread, untitled.Id));
    }));

    // ---- PR-TERM-011: selected(for:) ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-011")] // App/Views/Terminal/TerminalStore.swift:113-116
    public void Selected_is_the_session_the_selection_names() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("selected");
        var first = store.Open(thread, directory);
        var second = store.Open(thread, directory);
        store.Open(thread, directory);

        store.Selection = store.Selection.Mutating(m => m[thread] = first.Id);
        Assert.Same(first, store.Selected(thread));

        store.Selection = store.Selection.Mutating(m => m[thread] = second.Id);
        Assert.Same(second, store.Selected(thread));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-011")] // App/Views/Terminal/TerminalStore.swift:113-116
    public void A_stale_selection_falls_back_to_the_last_session_and_the_stale_key_stays() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("stale selection");
        store.Open(thread, directory);
        store.Open(thread, directory);
        var last = store.Open(thread, directory);
        var stale = Guid.NewGuid();

        store.Selection = store.Selection.Mutating(m => m[thread] = stale);

        Assert.Same(last, store.Selected(thread));       // :115 ?? list.last
        Assert.True(SelectionNames(store, thread, stale)); // selected(for:) only reads; the stale key is left in place
    }));

    [Fact]
    [Trait("Req", "PR-TERM-011")] // App/Views/Terminal/TerminalStore.swift:113-116
    public void A_missing_selection_or_one_naming_another_threads_session_falls_back_to_the_last() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("missing selection");
        store.Open(thread, directory);
        var last = store.Open(thread, directory);
        var elsewhere = store.Open(otherThread, directory);

        store.Selection = store.Selection.Mutating(m => m.Remove(thread));
        Assert.Same(last, store.Selected(thread));

        store.Selection = store.Selection.Mutating(m => m[thread] = elsewhere.Id); // only this thread's list is searched
        Assert.Same(last, store.Selected(thread));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-011")] // App/Views/Terminal/TerminalStore.swift:113-116
    public void With_no_sessions_selected_is_nil_even_when_a_selection_is_recorded() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        store.Selection = store.Selection.Mutating(m => m[thread] = Guid.NewGuid());

        Assert.Null(store.Selected(thread)); // list.first is nil and list.last is nil
    }));

    // ---- PR-TERM-037: a session's identity -------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-037")] // App/Views/Terminal/TerminalStore.swift:7-12, :17-20
    public void A_session_has_a_fresh_id_and_keeps_its_thread_directory_and_title_and_starts_running() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("identity");

        var first = store.Open(thread, directory);
        var second = store.Open(thread, directory, title: "Logs");

        Assert.NotEqual(Guid.Empty, first.Id);   // :8 let id = UUID()
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(thread, first.Id);
        Assert.Equal(thread, first.ThreadID);    // :9
        Assert.Equal(directory, first.Directory); // :10
        Assert.Equal("Terminal 1", first.Title);  // :11
        Assert.Equal("Logs", second.Title);
        Assert.True(first.IsRunning);             // :12 isRunning = true
        Assert.True(second.IsRunning);
    }));

    [Fact]
    [Trait("Req", "PR-TERM-037")] // App/Views/Terminal/TerminalStore.swift:12, :59-63, :139-155
    public void Is_running_only_ever_goes_from_true_to_false_and_a_restart_makes_a_new_session() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("one way");
        var session = store.Open(thread, directory);
        var id = session.Id;

        session.Terminate();
        Assert.False(session.IsRunning);
        session.Terminate();
        Assert.False(session.IsRunning);

        var fresh = store.Restart(session);

        Assert.NotSame(session, fresh);
        Assert.NotEqual(id, fresh.Id);
        Assert.False(session.IsRunning); // the old session never comes back to running
        Assert.True(fresh.IsRunning);
        Assert.Equal(id, session.Id);    // identity is fixed for the session's life
        Assert.Equal(thread, session.ThreadID);
        Assert.Equal(directory, session.Directory);
        Assert.Equal("Terminal 1", session.Title);
    }));

    // ---- PR-TERM-052: restart() ------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-052")] // App/Views/Terminal/TerminalStore.swift:139-155
    public void Restart_replaces_the_session_at_the_same_index_keeping_title_and_directory_and_selects_it() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("restart in place");
        var first = store.Open(thread, directory);
        var middle = store.Open(thread, directory, title: "Server");
        var last = store.Open(thread, directory);

        var fresh = store.Restart(middle);

        var list = store.Sessions(thread);
        Assert.Equal(3, list.Count);
        Assert.Same(first, list[0]);
        Assert.Same(fresh, list[1]);         // :148-149 same index
        Assert.Same(last, list[2]);
        Assert.False(middle.IsRunning);      // :141 session.terminate()
        Assert.True(fresh.IsRunning);
        Assert.NotEqual(middle.Id, fresh.Id);
        Assert.Equal(middle.ThreadID, fresh.ThreadID); // :143
        Assert.Equal(middle.Directory, fresh.Directory); // :144
        Assert.Equal("Server", fresh.Title);  // :145
        Assert.True(SelectionNames(store, thread, fresh.Id)); // :153
        Assert.True(first.IsRunning);
        Assert.True(last.IsRunning);
    }));

    [Fact]
    [Trait("Req", "PR-TERM-052")] // App/Views/Terminal/TerminalStore.swift:148-153
    public void Restarting_a_session_the_store_no_longer_holds_appends_the_fresh_one() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("restart appends");
        var gone = store.Open(thread, directory, title: "Gone");
        var kept = store.Open(thread, directory);
        store.Close(gone);

        var fresh = store.Restart(gone);

        var list = store.Sessions(thread);
        Assert.Equal(2, list.Count);
        Assert.Same(kept, list[0]);
        Assert.Same(fresh, list[1]); // :151 appended
        Assert.Equal("Gone", fresh.Title);
        Assert.True(SelectionNames(store, thread, fresh.Id));
    }));

    [Fact]
    [Trait("Req", "PR-TERM-052")] // App/Views/Terminal/TerminalStore.swift:127-129, :139-155
    public void A_restarted_script_tab_keeps_the_script_name() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("script restart name");
        store.Run(new ProjectScript(name: "Test suite", command: "Write-Output ready"), thread, directory);
        var script = store.Sessions(thread)[0];

        var fresh = store.Restart(script);

        Assert.Equal("Test suite", fresh.Title);
        Assert.Same(fresh, store.Sessions(thread)[0]);
        Assert.False(script.IsRunning);
    }));

    // ---- PR-TERM-053: close() --------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-053")] // App/Views/Terminal/TerminalStore.swift:157-163
    public void Closing_the_selected_tab_moves_the_selection_to_the_last_remaining_tab_not_a_neighbour() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("close selected");
        var s1 = store.Open(thread, directory);
        var s2 = store.Open(thread, directory);
        var s3 = store.Open(thread, directory);
        var s4 = store.Open(thread, directory);
        store.Selection = store.Selection.Mutating(m => m[thread] = s2.Id);

        store.Close(s2);

        Assert.False(s2.IsRunning);                          // :158
        Assert.Equal(new[] { s1, s3, s4 }, store.Sessions(thread));  // :159
        Assert.True(SelectionNames(store, thread, s4.Id));   // :161 sessions[threadID]?.last?.id
    }));

    [Fact]
    [Trait("Req", "PR-TERM-053")] // App/Views/Terminal/TerminalStore.swift:160
    public void Closing_an_unselected_tab_leaves_the_selection_alone() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("close unselected");
        var s1 = store.Open(thread, directory);
        var s2 = store.Open(thread, directory);
        var s3 = store.Open(thread, directory);
        store.Selection = store.Selection.Mutating(m => m[thread] = s1.Id);

        store.Close(s3);

        Assert.True(SelectionNames(store, thread, s1.Id));
        Assert.Equal(new[] { s1, s2 }, store.Sessions(thread));
        Assert.False(s3.IsRunning);
        Assert.True(s1.IsRunning);
        Assert.True(s2.IsRunning);
    }));

    [Fact]
    [Trait("Req", "PR-TERM-053")] // App/Views/Terminal/TerminalStore.swift:157-163
    public void Closing_the_last_tab_clears_the_selection() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("close last");
        var only = store.Open(thread, directory);

        store.Close(only);

        Assert.Empty(store.Sessions(thread));
        Assert.False(store.Selection.ContainsKey(thread)); // :161 assigning nil removes the key
        Assert.Null(store.Selected(thread));
        Assert.False(only.IsRunning);
    }));

    // ---- PR-TERM-054: closeAll(for:) -------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-054")] // App/Views/Terminal/TerminalStore.swift:165-169
    public void Close_all_terminates_every_session_of_the_thread_and_removes_both_entries() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("close all");
        var a = store.Open(thread, directory);
        var b = store.Open(thread, directory);
        var other = store.Open(otherThread, directory);

        store.CloseAll(thread);

        Assert.False(a.IsRunning);                           // :166
        Assert.False(b.IsRunning);
        Assert.Empty(store.Sessions(thread));                // :167
        Assert.False(store.Selection.ContainsKey(thread));   // :168
        Assert.Null(store.Selected(thread));
        Assert.True(other.IsRunning);                        // another thread is untouched
        Assert.Same(other, Assert.Single(store.Sessions(otherThread)));
        Assert.True(SelectionNames(store, otherThread, other.Id));

        var next = store.Open(thread, directory);            // a fresh empty list: numbering starts again
        Assert.Equal("Terminal 1", next.Title);
    }));

    // ---- PR-TERM-056: terminateAll() -------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-056")] // App/Views/Terminal/TerminalStore.swift:171-175
    public void Terminate_all_stops_every_session_in_every_thread_and_leaves_both_maps_intact() => headless.OnUiThread(() => WithStore(store =>
    {
        var threadA = Guid.NewGuid();
        var threadB = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("terminate all");
        var a1 = store.Open(threadA, directory);
        var a2 = store.Open(threadA, directory);
        var b1 = store.Open(threadB, directory);

        store.TerminateAll();

        Assert.False(a1.IsRunning);
        Assert.False(a2.IsRunning);
        Assert.False(b1.IsRunning);
        Assert.Equal(new[] { a1, a2 }, store.Sessions(threadA)); // the dictionaries are not cleared
        Assert.Equal(new[] { b1 }, store.Sessions(threadB));
        Assert.True(SelectionNames(store, threadA, a2.Id));
        Assert.True(SelectionNames(store, threadB, b1.Id));
        Assert.Same(a2, store.Selected(threadA));
    }));

    // ---- PR-TERM-117: nothing survives the store -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-117")] // App/Views/Terminal/TerminalStore.swift:106-107, :1-176
    public void The_store_persists_nothing_so_a_new_store_starts_with_no_sessions_and_no_selection() => headless.OnUiThread(() =>
    {
        var defaults = new InMemorySettingsStore();
        using var scope = UserDefaults.Standard.Override(defaults);
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("per run");
        var first = new TerminalStore();
        try
        {
            var a = first.Open(thread, directory);
            first.Open(thread, directory, title: "Named");
            first.Run(new ProjectScript(name: "Script", command: "Write-Output ready"), thread, directory);
            first.Restart(a);
            first.Selection = first.Selection.Mutating(m => m[thread] = a.Id);
            first.Close(first.Sessions(thread)[0]);
            first.CloseAll(thread);
            first.Open(thread, directory);
        }
        finally
        {
            first.TerminateAll();
        }

        var second = new TerminalStore();
        try
        {
            Assert.Empty(second.Sessions(thread));
            Assert.Empty(second.Selection);
            Assert.Null(second.Selected(thread));
            Assert.Empty(defaults.Writes);                             // no settings key is written
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory)); // and no file is written where the shells run
            Assert.Equal("Terminal 1", second.Open(thread, directory).Title);
        }
        finally
        {
            second.TerminateAll();
        }
    });

    // ---- PR-TERM-039: shell resolution (DEVIATION OD-0008 C5) --------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-039")] // App/Views/Terminal/TerminalStore.swift:28
    public void The_launched_shell_is_pwsh_or_windows_powershell_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("shell resolution");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.Contains(Path.GetFileNameWithoutExtension(info!.ImagePath), ShellProbe.ShellImageNames, StringComparer.OrdinalIgnoreCase);
    }));

    // ---- PR-TERM-040: the shell inherits the captured environment plus exactly four overrides ---------------------

    [Fact]
    [Trait("Req", "PR-TERM-040")] // App/Views/Terminal/TerminalStore.swift:29-33
    public void The_shell_receives_exactly_the_four_overridden_variables_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.CreateProjectDirectory("environment overrides");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.Equal("xterm-256color", info!.Variable("TERM"));               // :30
        Assert.Equal("truecolor", info.Variable("COLORTERM"));                 // :31
        Assert.Equal("nth", info.Variable("TERM_PROGRAM"));                  // :32 DEVIATION OD-0008 B12
        Assert.Equal(directory, info.Variable("DROPPY_CODE_PROJECT_ROOT"));      // :33
    }));

    // ---- PR-TERM-041: no quoting or escaping, and Windows names collide only in letter case -----------------------

    [Fact]
    [Trait("Req", "PR-TERM-041")] // App/Views/Terminal/TerminalStore.swift:34
    public void The_environment_carries_the_awkward_directory_verbatim_and_each_override_appears_once_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.CreateProjectDirectory("verbatim env");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.Equal(directory, info!.Variable("DROPPY_CODE_PROJECT_ROOT")); // the awkward name, unescaped
        Assert.Equal(1, info.CountVariable("TERM_PROGRAM"));                  // :30-33 always a write, never a duplicate
        Assert.Equal(1, info.CountVariable("TERM"));
        Assert.Equal(1, info.CountVariable("COLORTERM"));
        Assert.Equal(1, info.CountVariable("DROPPY_CODE_PROJECT_ROOT"));
    }));

    // ---- PR-TERM-042: no hyphen-prefixed login argv[0] is set on Windows -------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-042")] // App/Views/Terminal/TerminalStore.swift:35, :40
    public void No_hyphen_prefixed_login_argv_zero_is_set_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("no hyphen argv0");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.NotEmpty(info!.Arguments);
        Assert.False(info.Arguments[0].StartsWith('-'));
        Assert.Contains(Path.GetFileNameWithoutExtension(info.ImagePath), info.Arguments[0], StringComparison.OrdinalIgnoreCase);
    }));

    // ---- PR-TERM-043: a plain tab launches the shell with an empty argument list -----------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-043")] // App/Views/Terminal/TerminalStore.swift:36-40
    public void A_plain_tab_launches_the_shell_with_no_extra_arguments_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("empty args");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.Single(info!.Arguments); // argv[0], the executable path, only: :36 var arguments: [String] = []
    }));

    // ---- PR-TERM-044: the shell's working directory is the session's directory --------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-044")] // App/Views/Terminal/TerminalStore.swift:40
    public void The_shell_starts_in_the_sessions_directory_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.CreateProjectDirectory("working directory");
        var session = store.Open(thread, directory);

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.True(ShellProbe.SameDirectory(info!.CurrentDirectory, directory));
    }));

    // ---- PR-TERM-045: the fixed greys plus the theme accent ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-045")] // App/Views/Terminal/TerminalStore.swift:47-57
    public void Apply_appearance_sets_the_fixed_greys_and_the_theme_accent() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("appearance");
        var session = store.Open(thread, directory);

        session.ApplyAppearance(isDark: true);
        Assert.NotNull(session.View.NativeBackgroundColor);
        Assert.NotNull(session.View.NativeForegroundColor);
        Assert.Equal(new SwiftColor(0.08, 1).Resolve(ThemeVariant.Dark), session.View.NativeBackgroundColor!.Resolve(ThemeVariant.Dark)); // :50
        Assert.Equal(new SwiftColor(0.9, 1).Resolve(ThemeVariant.Dark), session.View.NativeForegroundColor!.Resolve(ThemeVariant.Dark));   // :51

        var accent = Chrome.AccentNSColor.Resolve(ThemeVariant.Dark);
        var caret = session.View.CaretColor!.Resolve(ThemeVariant.Dark);
        var selection = session.View.SelectedTextBackgroundColor!.Resolve(ThemeVariant.Dark);
        Assert.Equal(accent, caret); // :55
        Assert.Equal(Chrome.AccentNSColor.Opacity(0.3).Resolve(ThemeVariant.Dark), selection); // :56 withAlphaComponent(0.3)

        session.ApplyAppearance(isDark: false);
        Assert.Equal(new SwiftColor(0.985, 1).Resolve(ThemeVariant.Light), session.View.NativeBackgroundColor!.Resolve(ThemeVariant.Light)); // :50
        Assert.Equal(new SwiftColor(0.12, 1).Resolve(ThemeVariant.Light), session.View.NativeForegroundColor!.Resolve(ThemeVariant.Light));   // :51
    }));

    [Fact]
    [Trait("Req", "PR-TERM-045")] // App/Views/Terminal/TerminalStore.swift:48
    public void Applying_the_same_appearance_twice_leaves_the_same_colour_objects() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("appearance idempotent");
        var session = store.Open(thread, directory);

        session.ApplyAppearance(isDark: true);
        var background = session.View.NativeBackgroundColor;
        var caret = session.View.CaretColor;

        session.ApplyAppearance(isDark: true); // :48 guard appliedDark != isDark else { return }

        Assert.Same(background, session.View.NativeBackgroundColor);
        Assert.Same(caret, session.View.CaretColor);
    }));

    // ---- PR-TERM-046: terminate is idempotent -----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-046")] // App/Views/Terminal/TerminalStore.swift:59-63
    public void Terminate_marks_the_session_not_running_at_once_and_a_second_call_is_a_no_op_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        // :60-63: terminate() does not wait for the shell to actually exit and has no escalation (unlike
        // Shell.terminate's ladder) -- an idle prompt can ignore the graceful signal (PR-TERM-046 failure_cases),
        // so this proves the idempotency the record actually promises, not that the OS process has died (the port's view
        // ends the shell too, as macOS's closed pty does, ED-NEW-shellleak-1; the next test proves that).
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("terminate idempotent");
        var session = store.Open(thread, directory);
        Assert.True(ShellProbe.IsAlive(session.View.ProcessId));
        Assert.True(session.IsRunning);

        session.Terminate(); // :61-62
        Assert.False(session.IsRunning);

        session.Terminate(); // :60 guard isRunning else { return } -- must not throw
        Assert.False(session.IsRunning);
    }));

    [Fact]
    [Trait("Req", "PR-TERM-046")] // App/Views/Terminal/TerminalStore.swift:59-63; on macOS the closed pty hangs the shell up, a ConPTY has no hang-up
    [Trait("Decision", "ED-NEW-shellleak-1")]
    public void Terminate_also_ends_the_shell_process_and_the_program_it_started_so_nothing_outlives_the_tab_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        // An idle PowerShell prompt ignores the graceful step, and a tab that was closed, restarted or quit with the app
        // used to leave the shell and its conhost running. The shell starts a hidden program of its own that sleeps.
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("terminate ends the tree");
        var command = "$exe = (Get-Process -Id $PID).Path; "
            + "$child = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList '-NoLogo','-NoProfile','-Command','Start-Sleep -Seconds 300'; "
            + "Set-Content -Path 'child.pid' -Value $child.Id -Encoding ascii";
        store.Run(new ProjectScript(name: "Tree", command: command), thread, directory);
        var session = store.Sessions(thread)[0];
        var shell = session.View.ProcessId;
        var child = 0;
        try
        {
            Assert.True(int.TryParse(UiPump.WaitForText(Path.Join(directory, "child.pid"), UiPump.Bound),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out child),
                "the shell did not start its program");
            Assert.True(ShellProbe.IsAlive(shell));
            Assert.True(ShellProbe.IsAlive(child));

            session.Terminate();

            Assert.True(ShellProbe.WaitForExit(shell, UiPump.Bound), "the shell outlived Terminate");
            Assert.True(ShellProbe.WaitForExit(child, UiPump.Bound), "the program the shell started outlived Terminate");
        }
        finally
        {
            if (child != 0) ShellProbe.KillIfAlive(child);
            ShellProbe.KillIfAlive(shell);
        }
    }));

    // ---- PR-TERM-047: a shell that exits on its own marks the session not running -------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-047")] // App/Views/Terminal/TerminalStore.swift:78-80, :97-100
    public void A_shell_that_exits_on_its_own_marks_isRunning_false_and_keeps_the_tab_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("self exit");
        store.Run(new ProjectScript(name: "Self exit", command: "exit 0"), thread, directory);
        var session = store.Sessions(thread)[0];

        Assert.True(UiPump.Until(() => !session.IsRunning, UiPump.Bound));
        Assert.Single(store.Sessions(thread)); // the tab is not removed
        Assert.Same(session, store.Sessions(thread)[0]);
        Assert.Equal("Self exit", session.Title); // the title is unchanged
    }));

    // ---- PR-TERM-048: three delegate callbacks are deliberately empty -----------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-048")] // App/Views/Terminal/TerminalStore.swift:91, :93, :95
    public void The_three_terminal_delegate_hooks_are_empty_no_ops_by_code_inspection()
    {
        // TerminalBridge.SizeChanged/SetTerminalTitle/HostCurrentDirectoryUpdate (TerminalStore.cs, cited at the
        // same Swift lines) are one-line empty bodies: no field, store method or session state is touched. This
        // unit hosts no OSC-sequence-parsing terminal emulator (a future UI unit owns the real grid), so nothing
        // in the port can emit these callbacks yet to observe externally; verified by inspection of the three
        // empty bodies, per the packet's "unless it cannot be tested automatically (then say so and why)".
        Assert.True(true);
    }

    // ---- PR-TERM-049: PLATFORM BOUNDARY, accepted deviation OD-0031 / ED-046 (see Port/ProcessDescendants.cs) --

    [Fact]
    [Trait("Req", "PR-TERM-049")] // App/Views/Terminal/TerminalStore.swift:69-76
    public void Foreground_job_detection_fails_closed_for_a_non_positive_pid()
    {
        Assert.False(TerminalSession.ForegroundJobIsTheShell(0));  // :70
        Assert.False(TerminalSession.ForegroundJobIsTheShell(-1));
    }

    [Fact]
    [Trait("Req", "PR-TERM-049")] // App/Views/Terminal/TerminalStore.swift:69-76
    public void A_shell_at_the_prompt_reads_as_foreground_and_a_shell_running_a_child_does_not_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("foreground job");
        var atPrompt = store.Open(thread, directory);

        Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(atPrompt.View.ProcessId), UiPump.Bound));

        var busyDirectory = TerminalTestFolders.Create("foreground job busy");
        store.Run(new ProjectScript(name: "Busy", command: "powershell -NoProfile -Command 'Start-Sleep -Seconds 20'"), thread, busyDirectory);
        var busy = store.Sessions(thread)[1];

        Assert.True(UiPump.Until(() => !TerminalSession.ForegroundJobIsTheShell(busy.View.ProcessId), UiPump.Bound));
    }));

    // ---- PR-TERM-031 / PR-TERM-049: "if it can't tell, it also goes to the terminal" (OD-0031, ED-046; TerminalStore.swift:69-76
    // answers false when it cannot tell). The process table is replaced through the port's seam so each unknown is forced. ----

    private static T WithProcessTable<T>(Func<IReadOnlyList<ProcessDescendants.ProcessRow>?> table, Func<T> body)
    {
        var previous = ProcessDescendants.ReadProcessTable;
        ProcessDescendants.ReadProcessTable = table;
        try { return body(); }
        finally { ProcessDescendants.ReadProcessTable = previous; }
    }

    private static IReadOnlyList<ProcessDescendants.ProcessRow> Table(params (int Id, int Parent)[] rows) =>
        [.. rows.Select(row => new ProcessDescendants.ProcessRow(row.Id, row.Parent))];

    [Fact]
    [Trait("Req", "PR-TERM-031")] // App/Views/Terminal/TerminalStore.swift:69-76 failure path; OD-0031; ED-046 "default to FALSE on any error"
    [Trait("Req", "PR-TERM-049")]
    public void Foreground_job_detection_answers_false_whenever_it_cannot_tell()
    {
        // The pid is not in the process table (a pid no process holds): unknown, not "no child".
        Assert.False(TerminalSession.ForegroundJobIsTheShell(int.MaxValue));
        // The snapshot itself fails (CreateToolhelp32Snapshot / Process32First failure).
        Assert.False(WithProcessTable(() => null, () => TerminalSession.ForegroundJobIsTheShell(100)));
        // An empty table: the shell is not in it.
        Assert.False(WithProcessTable(() => Table(), () => TerminalSession.ForegroundJobIsTheShell(100)));
        // A table that does not list the shell but lists other processes.
        Assert.False(WithProcessTable(() => Table((4, 0), (200, 4)), () => TerminalSession.ForegroundJobIsTheShell(100)));
        Assert.Null(WithProcessTable(() => null, () => ProcessDescendants.HasLiveDescendant(100)));
        Assert.Null(WithProcessTable(() => Table((4, 0)), () => ProcessDescendants.HasLiveDescendant(100)));
        Assert.Null(ProcessDescendants.HasLiveDescendant(0));
    }

    [Fact]
    [Trait("Req", "PR-TERM-031")] // ED-046 "or enumeration timeout"
    [Trait("Req", "PR-TERM-049")]
    public void Foreground_job_detection_answers_false_when_the_enumeration_times_out()
    {
        var previous = ProcessDescendants.EnumerationTimeout;
        ProcessDescendants.EnumerationTimeout = TimeSpan.Zero; // the real Toolhelp walk exceeds it at its first entry
        try
        {
            Assert.Null(ProcessDescendants.HasLiveDescendant(Environment.ProcessId));
            Assert.False(TerminalSession.ForegroundJobIsTheShell(Environment.ProcessId));
        }
        finally { ProcessDescendants.EnumerationTimeout = previous; }
        // With the ordinary timeout the same real table lists this process, so the answer is no longer unknown.
        Assert.NotNull(ProcessDescendants.HasLiveDescendant(Environment.ProcessId));
    }

    [Fact]
    [Trait("Req", "PR-TERM-031")] // OD-0031: close only "when the shell has no live child program"
    [Trait("Req", "PR-TERM-049")]
    public void Foreground_job_detection_is_true_only_for_a_listed_shell_with_no_live_descendant()
    {
        // The shell (100) is in the table with no child: the shell owns the foreground.
        Assert.True(WithProcessTable(() => Table((4, 0), (100, 4), (300, 4)), () => TerminalSession.ForegroundJobIsTheShell(100)));
        // A direct child, or a grandchild, is a live program: the shell does not own it.
        Assert.False(WithProcessTable(() => Table((100, 4), (200, 100)), () => TerminalSession.ForegroundJobIsTheShell(100)));
        Assert.False(WithProcessTable(() => Table((100, 4), (200, 100), (201, 200)), () => TerminalSession.ForegroundJobIsTheShell(100)));
        // Children of other processes are not descendants of the shell.
        Assert.True(WithProcessTable(() => Table((100, 4), (300, 4), (301, 300)), () => TerminalSession.ForegroundJobIsTheShell(100)));
        Assert.Equal(false, WithProcessTable(() => Table((100, 4)), () => ProcessDescendants.HasLiveDescendant(100)));
        Assert.Equal(true, WithProcessTable(() => Table((100, 4), (200, 100)), () => ProcessDescendants.HasLiveDescendant(100)));
    }

    // ---- PR-TERM-123: a script tab's argument vector (DEVIATION OD-0008 C5) ------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-123")] // App/Views/Terminal/TerminalStore.swift:37-39
    public void A_script_tab_runs_the_command_then_stays_interactive_whether_it_succeeded_or_failed_Native() => headless.OnUiThread(() => WithRealStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("script argv");
        var marker = Path.Join(directory, "ran.txt");
        // cmd /c exit 7 fails (a nonzero result) without itself ending the PowerShell host, unlike an explicit
        // `exit` in the script; the record's "; exec shell -l" joins with the same "runs regardless" semantics.
        var command = $"Set-Content -Path '{marker}' -Value 'ok' -Encoding utf8; cmd /c exit 7";

        store.Run(new ProjectScript(name: "Failing script", command: command), thread, directory);
        var session = store.Sessions(thread)[0];

        Assert.Equal("ok", UiPump.WaitForText(marker, UiPump.Bound)?.TrimStart('﻿')); // ran as written, unquoted

        var info = ShellProbe.TryRead(session.View.ProcessId);
        Assert.NotNull(info);
        Assert.Contains("-NoExit", info!.Arguments, StringComparer.OrdinalIgnoreCase); // :39 exec shell -l equivalent
        Assert.True(ShellProbe.IsAlive(session.View.ProcessId)); // the failing exit code did not end the tab
        Assert.True(session.IsRunning);
    }));

    // ---- PR-TERM-038 (font and Option-as-Meta only; the 640x240 frame has no seam here, see the trailing note) and
    // PR-TERM-124: the font is TerminalFont.preferred(), evaluated once per session creation -----------------------

    [Fact]
    [Trait("Req", "PR-TERM-038")] // App/Views/Terminal/TerminalStore.swift:21-23
    [Trait("Req", "PR-TERM-124")] // App/Views/Terminal/TerminalStore.swift:22
    public void Each_session_is_created_with_TerminalFont_preferred() => headless.OnUiThread(() => WithStore(store =>
    {
        var thread = Guid.NewGuid();
        var directory = TerminalTestFolders.Create("font");
        var session = store.Open(thread, directory);

        Assert.Equal(TerminalFont.Preferred(), session.View.Font); // :22, evaluated at construction
        Assert.True(session.View.OptionAsMetaKey);                  // :23

        var fresh = store.Restart(session);
        Assert.Equal(TerminalFont.Preferred(), fresh.View.Font); // re-evaluated by the fresh session's own init
    }));
}
