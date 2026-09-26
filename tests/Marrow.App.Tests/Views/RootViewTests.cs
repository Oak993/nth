using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.App.Views;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :1-450.
// Covers the 6 types that do not depend on SidebarView.cs or ChatView.cs (both blocked): DetailView's empty-state
// branch, WelcomeView, ProviderChecklist, ProviderStatusRow, OpenProvidersSettingsButton and NoThreadView; and
// RootView.HandleDropCore (PR-WIN-064, PR-WIN-065), the whole body of the private handleDrop(_:) (:94-110), which
// needs no visual tree and so is not blocked by SidebarView the way RootView's own top-level body is. CopyCommandButton
// is gone at B3 (B3_PLAN.md section 4); its test is removed with it. RootView's top-level body, ThreadColumns and
// DetailView's "a thread is selected" branch stay MARROW_STUB (see the STOP note at the end of this file) and are not
// exercised here.
// AppModelHarness's TestRuntime makes MainActorModel writes (Track callbacks fired during AttachedToVisualTree)
// require an active Pump, so both construction and interaction happen inside harness.Main.OnMain(...), matching the
// pattern this session already established for App.Views.Terminal.TerminalPanel and App.Views.Settings.HydraSettingsPage.
[Collection("Headless")]
public sealed class RootViewTests(HeadlessSession headless)
{
    private static string AllText(Control control) =>
        string.Join("\n", LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? ""));

    /// <summary>A dropped path as HandleDropCore's SwiftURL parameter type expects it. SOURCE FACT (empirically
    /// confirmed here, matching the documented gap CodexSession.cs's DirectoryFileUrl and ACPSession.cs already
    /// work around, both cited on SwiftURL.cs's own file:// POSIX-root note): a "file://" SwiftURL's .Path
    /// unconditionally reproduces macOS's POSIX url.path and corrupts a Windows drive-letter path ("C:\Users\x" ->
    /// "/C:/Users/x", which Directory.Exists/File.Exists then correctly report as absent). FileURLWithPath
    /// (SwiftURL.cs:88) is no better: it traps on any non-POSIX-rooted path outright. This builds the same
    /// non-"file"-scheme URL those two units use instead, whose .Path takes the plain percent-decode branch
    /// (SwiftURL.cs's non-file Path branch) with no POSIX-root handling, round-tripping correctly.</summary>
    private static SwiftURL DroppedUrl(string path)
    {
        var normalized = path.Replace('\\', '/');
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return SwiftURL.Create("marrow-dropped-path:" + builder) ?? throw new InvalidOperationException("bad test path");
    }

    private static Control Build(AppModel model, Func<Control> makeView)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = makeView();
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return host;
    }

    [Fact]
    [Trait("Req", "PR-WIN-077")] // RootView.swift:155-165
    public void DetailView_shows_the_welcome_page_when_there_are_no_projects_and_no_selected_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new DetailView());
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<WelcomeView>(host));
                Assert.Empty(LogicalQuery.SelfAndDescendants<NoThreadView>(host));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-077")] // RootView.swift:155-165
    public void DetailView_shows_the_no_thread_page_once_a_project_exists_and_no_thread_is_selected()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("Project", directory).Url);
                var host = Build(model, () => new DetailView());
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<NoThreadView>(host));
                Assert.Empty(LogicalQuery.SelfAndDescendants<WelcomeView>(host));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-079")] // RootView.swift:304-315, OD-0002
    public void WelcomeView_shows_the_Marrow_identity_and_tagline()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new WelcomeView());
                var text = AllText(host);
                Assert.Contains("Marrow", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Droppy Code", text, StringComparison.Ordinal);
                Assert.Contains("A calm, native home for your coding agents.", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-080")] // RootView.swift:316-328
    public void WelcomeView_offers_add_a_project_over_the_drop_hint()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new WelcomeView());
                var text = AllText(host);
                Assert.Contains("Add a project", text, StringComparison.Ordinal);
                Assert.Contains("Or drop a folder anywhere in this window.", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-080")] // RootView.swift:317-319. ChooseProjectFolder no-ops when the headless lifetime
    // carries no IClassicDesktopStyleApplicationLifetime main window (AppModel.swift:526-529), so this only proves
    // the click reaches the model without throwing, not that a folder picker opens.
    public void Clicking_add_a_project_does_not_throw()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new WelcomeView());
                var add = LogicalQuery.SelfAndDescendants<Button>(host)
                    .First(button => AllText(button).Contains("Add a project", StringComparison.Ordinal));
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
        });
    }

    // WelcomeView_activity_list_row_detail_follows_the_selected_style: each case below builds its own host from a
    // style already set before construction, rather than mutating ActivityThreadStyle after the page exists.
    // Mutating it afterwards posts WelcomeView's Track callback (a full-page Rebuild) for a LATER turn (Tracked<T>'s
    // reactive update is posted, not inline); if that turn is never itself pumped by the test, the dispatcher still
    // flushes it during headless teardown (Dispatcher.ResetForUnitTests), where Avalonia's font pipeline
    // reproducibly throws "Could not create glyphTypeface" (the same pre-existing headless limitation documented in
    // TerminalPanelTests.cs/followups.md).
    [Theory]
    [Trait("Req", "PR-WIN-081")] // RootView.swift:329-338
    [InlineData(ActivityThreadStyle.Icon)]
    [InlineData(ActivityThreadStyle.ProjectName)]
    public void WelcomeView_activity_list_row_detail_matches_the_style_set_before_construction(ActivityThreadStyle style)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.ActivityThreadStyle = style;
                var host = Build(model, () => new WelcomeView());
                Assert.Contains("Project mark", AllText(host), StringComparison.Ordinal);
                Assert.Contains(style.Detail, AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-082")] // RootView.swift:339-342, :352-363
    public void WelcomeView_lists_a_Providers_section()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new WelcomeView());
                Assert.Contains("Providers", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-082")] // RootView.swift:352-363
    public void ProviderChecklist_renders_one_row_per_provider_with_n_minus_one_dividers()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new ProviderChecklist());
                var kinds = SwiftEnum<ProviderKind>.AllCases;
                var rows = LogicalQuery.SelfAndDescendants<ProviderStatusRow>(host).ToArray();
                var dividers = LogicalQuery.SelfAndDescendants<ChromeRowDivider>(host).ToArray();
                Assert.Equal(kinds.Count, rows.Length);
                Assert.Equal(kinds.Count - 1, dividers.Length);
                foreach (var kind in kinds) Assert.Contains(kind.DisplayName, AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-084")] // RootView.swift:386-393. Every provider starts "not installed" (no real
    // executable is on the harness's binary paths, R-TEST-10), so this exercises the not-installed branch for a
    // CLI provider without needing to fake ProviderRegistry's internal status cache.
    public void A_not_installed_CLI_provider_offers_Install()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var cli = SwiftEnum<ProviderKind>.AllCases.First(kind => !kind.IsAPIKeyBased);
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new ProviderStatusRow(cli));
                Assert.Contains("Install", AllText(host), StringComparison.Ordinal);
                Assert.DoesNotContain("Add key", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-084")] // RootView.swift:386-394
    public void A_not_installed_API_key_provider_offers_Add_key()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var apiKey = SwiftEnum<ProviderKind>.AllCases.First(kind => kind.IsAPIKeyBased);
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new ProviderStatusRow(apiKey));
                Assert.Contains("Add key", AllText(host), StringComparison.Ordinal);
                var button = LogicalQuery.SelfAndDescendants<Button>(host).Single();
                Assert.Equal("Open Settings > Providers", ToolTip.GetTip(button) as string);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-088")] // RootView.swift:424-449
    public void NoThreadView_offers_a_new_thread_button_with_its_chord_hint()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new NoThreadView());
                var text = AllText(host);
                Assert.Contains("Pick a thread or start a new one", text, StringComparison.Ordinal);
                Assert.Contains("New thread", text, StringComparison.Ordinal);
                Assert.Contains("Ctrl+N", text, StringComparison.Ordinal); // OD-0008 B7: Windows notation, not macOS "⌘N"
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-088")] // RootView.swift:434-435
    public void Clicking_new_thread_creates_a_thread_in_the_current_project()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("Project", directory).Url);
                var host = Build(model, () => new NoThreadView());
                var before = model.Threads.Count;

                var newThread = LogicalQuery.SelfAndDescendants<Button>(host)
                    .First(button => AllText(button).Contains("New thread", StringComparison.Ordinal));
                newThread.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(before + 1, model.Threads.Count);
                Assert.NotNull(model.SelectedThreadID);
            });
        });
    }

    // HandleDropCore (RootView.swift:94-110): the whole body of private func handleDrop(_:), reached through the
    // internal static extraction RootView.HandleDropCore documented on that method. Every case below drives it
    // directly with real files and folders under harness.Scratch (R-TEST-9); none of it needs RootView's own body
    // (still MARROW_STUB, see the STOP note below), so these are not blocked by SidebarView.

    [Fact]
    [Trait("Req", "PR-WIN-064")] // RootView.swift:96-105
    public void Dropping_folders_adds_each_as_a_project_and_starts_a_thread_in_the_last_one()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var first = harness.NewFolder("first");
            var second = harness.NewFolder("second");
            harness.Main.OnMain(() =>
            {
                var accepted = RootView.HandleDropCore(model, [DroppedUrl(first), DroppedUrl(second)]);

                Assert.True(accepted); // :109 !folders.isEmpty
                Assert.Equal(2, model.Projects.Count); // :104, both dropped folders added, in order
                // AddProject stores url.Path (AppModel.swift:516-524), which round-trips with '/' separators
                // (SwiftPath.StandardizedFileURLPath): compare normalized, not the raw '\' harness.NewFolder path.
                Assert.Equal(first.Replace('\\', '/'), model.Projects[0].Path);
                Assert.Equal(second.Replace('\\', '/'), model.Projects[1].Path);
                // :105 model.newThread(in: lastProject): the SECOND (last) folder gets the new thread, not the first.
                Assert.NotNull(model.SelectedThreadID);
                var thread = model.Thread(model.SelectedThreadID!.Value)!;
                Assert.Equal(model.Projects[1].Id, thread.ProjectID);
                // :103-105 `for folder in folders { lastProject = ... }` only reassigns lastProject; newThread(in:)
                // is called exactly ONCE, after the loop, not once per folder. Guards against a regression that
                // moves the newThread call inside the loop, which would still leave SelectedThreadID pointing at a
                // Projects[1] thread (newThread always overwrites it) and so would still pass the assertions above.
                Assert.Single(model.Threads);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-064")] // RootView.swift:98-101
    public void Dropping_a_path_that_does_not_exist_is_skipped_and_reports_no_acceptance()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var missing = Path.Combine(harness.Scratch, "does-not-exist-" + Guid.NewGuid().ToString("N"));
            harness.Main.OnMain(() =>
            {
                var accepted = RootView.HandleDropCore(model, [DroppedUrl(missing)]);

                Assert.False(accepted); // :100 guard fileExists else { continue }; :109 both lists stay empty
                Assert.Empty(model.Projects);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:106-108
    public void Dropping_a_file_with_a_thread_selected_attaches_it_to_that_threads_draft()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            var filePath = Path.Combine(harness.Scratch, "dropped.txt");
            File.WriteAllText(filePath, "hello");
            Guid threadID = default;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(DroppedUrl(directory));
                threadID = model.NewThread(project)!.Id; // selects it (AppModel.swift:623)
            });

            harness.Main.OnMain(() =>
            {
                var accepted = RootView.HandleDropCore(model, [DroppedUrl(filePath)]);
                Assert.True(accepted); // :109 !files.isEmpty
            });
            // Storage.Attach imports on a detached task (Services.Store.Storage.swift:161-174) then hops back to
            // main to update the draft; pump until that lands, as StorageTests.cs's own Attach tests do.
            harness.Main.Loop.RunUntil(() => model.Runtime(threadID).Draft.Attachments.Count > 0);

            harness.Main.OnMain(() =>
            {
                Assert.Single(model.Runtime(threadID).Draft.Attachments);
                Assert.Equal("dropped.txt", model.Runtime(threadID).Draft.Attachments[0].Name);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:106-108, "failure_cases": "No thread selected"
    public void Dropping_a_file_with_no_thread_selected_is_silently_discarded()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var filePath = Path.Combine(harness.Scratch, "dropped.txt");
            File.WriteAllText(filePath, "hello");
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SelectedThreadID); // no project, no thread, nothing selected

                var accepted = RootView.HandleDropCore(model, [DroppedUrl(filePath)]);

                // :106 `let threadID = model.selectedThreadID` fails: no Storage.attach call, no exception, and
                // still true because :109 counts an accepted file regardless of whether it could be attached.
                Assert.True(accepted);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:103-108
    // SOURCE FACT, verified directly against the pinned reference (AppModel.swift:614/623), overriding this
    // record's own windows_path prose ("a thread created by the folder half is NOT the thread the files attach
    // to"): newThread(in:) sets selectedThreadID = thread.id (AppModel.swift:623) BEFORE handleDrop's own read of
    // model.selectedThreadID at RootView.swift:106, which runs after the folder loop. So when a folder and a file
    // are dropped together with no thread selected beforehand, the file DOES land on the thread the folder half
    // just created and selected: the packet's expected_result prose does not match the traced control flow, and is
    // not followed here (R-TEST-4: expectations come from the pinned Swift, not from a mismatched gloss on it).
    public void A_mixed_drop_with_no_prior_selection_attaches_the_file_to_the_thread_the_folder_half_just_created()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var folder = harness.NewFolder("project");
            var filePath = Path.Combine(harness.Scratch, "dropped.txt");
            File.WriteAllText(filePath, "hello");
            Guid createdThreadID = default;
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SelectedThreadID);
                RootView.HandleDropCore(model, [DroppedUrl(folder), DroppedUrl(filePath)]);
                Assert.NotNull(model.SelectedThreadID); // :105 newThread selected it
                createdThreadID = model.SelectedThreadID!.Value;
            });
            harness.Main.Loop.RunUntil(() => model.Runtime(createdThreadID).Draft.Attachments.Count > 0);
            harness.Main.OnMain(() =>
            {
                Assert.Single(model.Runtime(createdThreadID).Draft.Attachments);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:103-108; AppModel.swift newThread(in:) sets selectedThreadID = thread.id
    // A thread already selected before the drop, in a project unrelated to the folder half, does NOT keep its
    // selection when the drop ALSO contains a folder: newThread(in: lastProject) (called once, after the folder
    // loop, :105) overwrites model.selectedThreadID with the thread it just created in the newly dropped project,
    // and HandleDropCore's file-attach step (:106-108) reads that overwritten value, not the pre-existing one.
    public void A_mixed_drop_overrides_an_unrelated_pre_existing_selection_and_attaches_the_file_to_the_new_threads_draft()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var existingDirectory = harness.NewFolder("existing");
            var droppedFolder = harness.NewFolder("dropped");
            var filePath = Path.Combine(harness.Scratch, "dropped.txt");
            File.WriteAllText(filePath, "hello");

            Guid existingThreadID = default;
            harness.Main.OnMain(() =>
            {
                var existingProject = model.AddProject(DroppedUrl(existingDirectory));
                existingThreadID = model.NewThread(existingProject)!.Id; // selects it, unrelated to the drop below
                Assert.Equal(existingThreadID, model.SelectedThreadID);
            });

            Guid newThreadID = default;
            harness.Main.OnMain(() =>
            {
                var accepted = RootView.HandleDropCore(model, [DroppedUrl(droppedFolder), DroppedUrl(filePath)]);
                Assert.True(accepted);
                // :105 newThread(in: lastProject) overwrote the pre-existing, unrelated selection.
                Assert.NotEqual(existingThreadID, model.SelectedThreadID);
                newThreadID = model.SelectedThreadID!.Value;
            });
            harness.Main.Loop.RunUntil(() => model.Runtime(newThreadID).Draft.Attachments.Count > 0);

            harness.Main.OnMain(() =>
            {
                Assert.Single(model.Runtime(newThreadID).Draft.Attachments);
                // The file attaches to the newly created/selected thread, never to the old, unrelated selection.
                Assert.Empty(model.Runtime(existingThreadID).Draft.Attachments);
            });
        });
    }
}

// STOP App.Views.RootView #2 (B3 unit agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/RootView.swift:8-92 (RootView body, B3 52bc792), :177-297 (ThreadColumns), :129-133
// (DetailView's "a thread is selected" branch)
// Requirements: PR-WIN-062, PR-WIN-063, PR-WIN-066, PR-WIN-067 (RootView's own body: window clip, backdrop and
// chrome-configurator backgrounds, the model-alert dialog, the genie/row-glide/command-palette overlay stack) and
// the "a thread is selected" half of PR-WIN-077 stay MARROW_STUB. PR-WIN-064 and PR-WIN-065 (drop handling) are now
// implemented and tested above through HandleDropCore, which needed no visual tree and so was not blocked by the
// dependency below.
// What is unclear or missing: nothing is unclear; this narrows STOP #1 (helper Claude, 2026-09-26), whose own
// blockers were checked again directly against the files named, not inferred:
//   - GenieLayer, RowGlideLayer (App.Views.Sidebar.RowGlide.cs), WindowChromeConfigurator/WindowChromeProbe and
//     WallpaperStore (src/Marrow.App/Theme/Wallpaper.cs; ED-195 supersedes ED-189 to move it to Marrow.UI.Theme in a
//     later lane, so its current App.Theme namespace is not this unit's to change) are ALL now real, 0 MARROW_STUB
//     each: STOP #1 listed them as blockers and that is no longer true.
//   - SidebarView (App.Views.Sidebar.SidebarView.cs) is still 53 MARROW_STUB; its constructor
//     (`public SidebarView(bool inPopover = false, Action? dismiss = null) => throw new
//     NotImplementedException(...)`, SidebarView.cs's own current text) throws unconditionally. RootView's body
//     builds `SidebarView()` as the first child of its HStack every time (RootView.swift:17), eagerly per R-VIEW-1
//     ("body is built once in the constructor"), so no `new RootView()` can succeed while this throws, for any
//     input, in production or in a test. This is the one unconditional blocker for PR-WIN-062, 063, 066 and 067.
//   - CommandPalette (App.Views.Palette.CommandPalette.cs) is also still MARROW_STUB (constructor throws), but only
//     reached when model.IsCommandPalettePresented is true (RootView.swift:72-76); it does not by itself block
//     PR-WIN-062, 063 or 066, only the true branch of PR-WIN-067, and only once SidebarView unblocks the rest.
//   - ThreadColumns.updateNSView calls into ChatView (App.Views.Chat.ChatView.cs, still 23 MARROW_STUB) and needs a
//     WindowLiveResize read from the environment; HostedEnvironment (App.Window.WindowManager.cs) now attaches one
//     for real, so ChatView is the only remaining block here. DetailView's "a thread is selected" branch constructs
//     ThreadColumns and so inherits this one block.
// Readings or options: wait for App.Views.Sidebar.SidebarView and, for the command-palette-true branch of
// PR-WIN-067, App.Views.Palette.CommandPalette; separately, wait for App.Views.Chat.ChatView for ThreadColumns and
// DetailView's thread-selected branch. RootView's own top-level body and ThreadColumns do not share a blocker any
// more (SidebarView versus ChatView), so they could be picked up in separate later passes if that helps sequencing,
// unlike STOP #1's assessment.
// Needed to proceed: unit App.Views.Sidebar.SidebarView (and, only for PR-WIN-067's true branch,
// App.Views.Palette.CommandPalette) for RootView's own body; unit App.Views.Chat.ChatView for ThreadColumns and
// DetailView's thread-selected branch.
// Done so far: every requirement in both work packets (part1.md, part2.md) not listed above is implemented and
// tested: DetailView's empty-state branch, WelcomeView, ProviderChecklist, ProviderStatusRow,
// OpenProvidersSettingsButton, NoThreadView (CopyCommandButton is gone at B3, B3_PLAN.md section 4; its test is
// removed, not left failing) and HandleDropCore (PR-WIN-064, PR-WIN-065), all 0 MARROW_STUB, all green.
