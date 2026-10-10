using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.App.Views;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :1-450.
// Covers the detail empty states and first-launch selected conversation: DetailView's empty-state branch, WelcomeView, ProviderChecklist, ProviderStatusRow,
// OpenProvidersSettingsButton and NoThreadView; RootView.HandleDropCore (PR-WIN-064, PR-WIN-065), the whole body of
// the private handleDrop(_:) (:94-110); and, from this review pass, RootView's own top-level body (PR-WIN-062,
// PR-WIN-063, PR-WIN-066, PR-WIN-067): the window's own clip, the detail pane's clip and (inert) insets, the model
// alert, and the genie/row-glide/command-palette overlay stack. CopyCommandButton is gone at B3 (B3_PLAN.md section
// 4); its test is removed with it. RootViewTestsColumns covers the selected thread's cache and prebuild lifecycle.
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

    // Headless keeps its virtual native client fixed; deliver the resize notification the real platform sends.
    private static void ResizeWindow(AvaloniaWindow window, Size size)
    {
        var resized = (Delegate)typeof(Avalonia.Platform.ITopLevelImpl)
            .GetProperty("Resized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window.PlatformImpl)!;
        resized.DynamicInvoke(size, WindowResizeReason.User);
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
    [Trait("Req", "PR-WIN-077")] // App/Views/RootView.swift:155-165
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
    [Trait("Req", "PR-WIN-077")] // App/Views/RootView.swift:155-165
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
    [Trait("Req", "PR-WIN-079")] // App/Views/RootView.swift:304-315, OD-0002
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
                Assert.Contains("nth", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Droppy Code", text, StringComparison.Ordinal);
                Assert.Contains("A calm, native home for your coding agents.", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-080")] // App/Views/RootView.swift:316-328
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
    [Trait("Req", "PR-WIN-080")] // App/Views/RootView.swift:317-319. ChooseProjectFolder no-ops when the headless lifetime
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
    [Trait("Req", "PR-WIN-081")] // App/Views/RootView.swift:329-338
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
    [Trait("Req", "PR-WIN-082")] // App/Views/RootView.swift:339-342, :352-363
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
    [Trait("Req", "PR-WIN-082")] // App/Views/RootView.swift:352-363
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
    [Trait("Req", "PR-WIN-084")] // App/Views/RootView.swift:386-393. Every provider starts "not installed" (no real
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
    [Trait("Req", "PR-WIN-084")] // App/Views/RootView.swift:386-394
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
    [Trait("Req", "PR-WIN-088")] // App/Views/RootView.swift:424-449
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
    [Trait("Req", "PR-WIN-088")] // App/Views/RootView.swift:434-435
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
    // so these exercise the drop body independently of the hosted views.

    [Fact]
    [Trait("Req", "PR-WIN-064")] // App/Views/RootView.swift:96-105
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
    [Trait("Req", "PR-WIN-064")] // App/Views/RootView.swift:98-101
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
    [Trait("Req", "PR-WIN-065")] // App/Views/RootView.swift:106-108
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
    [Trait("Req", "PR-WIN-065")] // App/Views/RootView.swift:106-108, "failure_cases": "No thread selected"
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
    [Trait("Req", "PR-WIN-065")] // App/Views/RootView.swift:103-108
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
    [Trait("Req", "PR-WIN-065")] // App/Views/RootView.swift:103-108; AppModel.swift newThread(in:) sets selectedThreadID = thread.id
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

    // ---- RootView's own top-level body (review pass; SidebarView and CommandPalette are both real now) ----

    // Review pass item (5): the only other RootView tests build it directly (Build(model, ...) above); this one
    // instead goes through WindowManager.MainContent/MakeMainWindow (WindowManager.cs:72,160-180), the actual
    // production path (WindowManagerTests.cs's own ShowMain helper proves real windows open fine under this
    // project's headless session; RecordingAlertPresenter exists only because a SECOND, ad-hoc modal window does
    // not, unrelated to this one). WindowManager() makes its own AppModel internally (:86), so PrepareBootstrap runs
    // on THAT model, the freshest one this path can produce, rather than a separate model WindowManager would never
    // actually use.
    // PrepareBootstrap's own LoginEnvironment.Load().Wait() captures the Avalonia dispatcher's context if awaited on
    // that thread and deadlocks (FollowUpEditorTests.cs's own identical RuntimeHarness() note). Completed once here,
    // on a pool thread with no synchronization context, the process-wide cached load (Shell.cs's Loader) then makes
    // every later PrepareBootstrap's own Wait() return at once; a private helper, not the [Fact] body itself, per
    // xUnit1031 (FollowUpEditorTests.cs's RuntimeHarness() is the same shape for the same reason).
    private static void LoadLoginEnvironmentOffMain() =>
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();

    [Fact]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:8-92, via the real WindowManager path
    [Trait("Req", "PR-WIN-077")] // App/Views/RootView.swift:155-165, the sidebar and the no-thread DetailView
    public void ShowMain_builds_a_real_RootView_whose_sidebar_and_no_thread_DetailView_render_without_throwing()
    {
        LoadLoginEnvironmentOffMain();
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var manager = harness.Main.OnMain(() => new WindowManager());
            harness.PrepareBootstrap(manager.Model);
            harness.Main.OnMain(manager.ShowMain);
            harness.Main.OnMain(() =>
            {
                var window = manager.MainWindow ?? throw new InvalidOperationException("ShowMain built no window");
                window.UpdateLayout();
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<SidebarView>(window));
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<WelcomeView>(window)); // no projects yet: :157-159
                Assert.Empty(LogicalQuery.SelfAndDescendants<NoThreadView>(window));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:64-68
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:33-43; UI/Chrome/Chrome.swift:9-11
    public void The_window_content_clips_to_the_windows_rectangle_and_the_detail_pane_clips_its_own_bounds()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new RootView());
                var root = Assert.IsType<RootView>(((Border)host).Child);

                // PR-WIN-063 verification_method: "unit: the constant is 0 and the clip is present".
                Assert.Equal(0, Chrome.SheetInset);
                Assert.True(root.DetailClip.ClipToBounds); // :40 .clipped()

                // PR-WIN-062: clipped after the backdrop (:64-65 paint behind windowClip; RootView's own constructor builds them in that order) is painted. OD-0075 and
                // the owner's intent of 2026-10-05 (ED-338): the clip is the window's rectangle, not the 26-point continuous rounded rectangle, so the backdrop
                // and the detail sheet reach the window's edge, and the backdrop's hairline is the rectangle's inner stroke (nothing of the window is drawn in the 26-point shape).
                Assert.True(root.WindowClip.ClipToBounds);
                Assert.Equal(default, root.WindowClip.CornerRadius);
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<WindowBackdrop>(root.WindowClip));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:8-92; App/Window/WindowManager.swift:62-72,86-110
    [Trait("Req", "PR-WIN-077")] // App/Views/RootView.swift:155-165; App/AppModel.swift:239-287
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:33-43,130-133,266-283
    public void A_fresh_empty_bootstrap_renders_the_real_WindowManager_RootView_sidebar_and_welcome_detail()
    {
        LoadLoginEnvironmentOffMain();
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness();
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            // The chat's Hydra button opens its intro popover on first appearance (HydraButton.swift:93); a headless window
            // has no popup platform, so popovers are recorded (as ChatHeadlessFixtures does).
            using var popovers = Presentation.Presenter.Override(new RecordingPresenter());
            var manager = harness.Main.OnMain(() => new WindowManager());
            using var shared = WindowManager.OverrideShared(manager);
            harness.Main.OnMain(() =>
            {
                Assert.Empty(manager.Model.Projects);
                Assert.Empty(manager.Model.Threads);
                Assert.Null(manager.Model.SelectedThreadID);
            });
            harness.PrepareBootstrap(manager.Model); // absent CLI paths + in-memory secrets; no real provider process or key
            harness.Main.Loop.Run(() => manager.Model.Bootstrap(), AppModelHarness.Bound); // real bootstrap completes
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            try
            {
                harness.Main.OnMain(manager.ShowMain); // MainContent remains its production () => new RootView()
                harness.Main.OnMain(() =>
                {
                    Assert.Empty(manager.Model.Projects);
                    Assert.Empty(manager.Model.Threads);
                    Assert.Null(manager.Model.SelectedThreadID);
                    var window = manager.MainWindow ?? throw new InvalidOperationException("ShowMain built no window");
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var root = Assert.Single(LogicalQuery.SelfAndDescendants<RootView>(window));
                    Assert.Same(manager.Model, AppEnvironment.GetAppModel(root)); // real HostedRoot environment
                    Assert.NotNull(AppEnvironment.GetWindowLiveResize(root));
                    var sidebar = Assert.Single(LogicalQuery.SelfAndDescendants<SidebarView>(root));
                    Assert.True(sidebar.Bounds.Width > 0);
                    Assert.Contains("Search threads", LogicalQuery.SelfAndDescendants<TextBox>(sidebar).Select(box => box.PlaceholderText));
                    var detail = Assert.Single(LogicalQuery.SelfAndDescendants<DetailView>(root));
                    Assert.Single(LogicalQuery.SelfAndDescendants<WelcomeView>(detail)); // :157-159 no projects/no selection
                    Assert.Empty(LogicalQuery.SelfAndDescendants<NoThreadView>(detail)); // :161-163 requires a project
                    Assert.True(detail.Bounds.Width > 0);
                    Assert.True(detail.Bounds.Height > 0);
                    using var image = window.CaptureRenderedFrame(); // draw the real hosted tree, not a synthetic child
                    Assert.NotNull(image);
                    Assert.True(image.PixelSize.Width > 0);
                    Assert.True(image.PixelSize.Height > 0);
                });
                // :130-133 the same real RootView changes to the selected thread after first launch.
                harness.Main.OnMain(() =>
                {
                    var project = manager.Model.AddProject(new Project("First project", harness.NewFolder("first-project")).Url);
                    var thread = manager.Model.NewThread(project)!;
                    Assert.Equal(thread.Id, manager.Model.SelectedThreadID);
                });
                harness.Main.OnMain(() =>
                {
                    var window = manager.MainWindow!;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var root = Assert.Single(LogicalQuery.SelfAndDescendants<RootView>(window));
                    Assert.Single(LogicalQuery.SelfAndDescendants<SidebarView>(root));
                    var detail = Assert.Single(LogicalQuery.SelfAndDescendants<DetailView>(root));
                    Assert.Empty(LogicalQuery.SelfAndDescendants<WelcomeView>(detail));
                    var chat = Assert.Single(LogicalQuery.SelfAndDescendants<ChatView>(detail));
                    Assert.Same(manager.Model, AppEnvironment.GetAppModel(chat));
                    Assert.Same(AppEnvironment.GetWindowLiveResize(root), AppEnvironment.GetWindowLiveResize(chat));
                    Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(chat));
                    Assert.Single(LogicalQuery.SelfAndDescendants<ComposerView>(chat));
                    Assert.True(chat.Bounds.Width > 0);
                    Assert.True(chat.Bounds.Height > 0);
                    using var image = window.CaptureRenderedFrame();
                    Assert.NotNull(image);
                });
            }
            finally { harness.Main.OnMain(() => { manager.MainWindow?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-066")] // App/Views/RootView.swift:80-91
    [Trait("Req", "UI-WIN-073")] // App/Views/RootView.swift:80-91 the one cancel-role OK button; dismissing clears model.alert
    public void A_model_alert_shows_through_the_presenter_with_one_OK_button_and_dismissing_clears_it()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var presenter = new RecordingAlertPresenter { AutoDismiss = false };
            using var alertSeam = AlertPresentation.Presenter.Override(presenter);
            harness.Main.OnMain(() =>
            {
                model.Alert = new AppAlert { Title = "Trouble", Message = "Something went wrong." }; // set before
                // attach, so the Track's own first (synchronous) evaluation at AttachedToVisualTree already sees it.
                Build(model, () => new RootView());

                var shown = Assert.Single(presenter.Shown);
                Assert.Equal("Trouble", shown.Title); // :81 model.alert?.title ?? ""
                Assert.Equal("Something went wrong.", shown.InformativeText); // :90 Text(alert.message)
                Assert.Equal(AlertStyle.Warning, shown.Style);
                var button = Assert.Single(shown.Buttons); // :87-88 one Button("OK", role: .cancel)
                Assert.Equal("OK", button.Title);
                Assert.NotNull(model.Alert); // still showing: dismissing, not raising it, is what clears it (:83-84)

                presenter.Dismiss(); // simulates the OK click (or, per PR-WIN-066's windows_path, Escape or Enter)
            });
            // ShowAlert's continuation after `await Show(...)` (where it sets model.Alert = null) is posted, not
            // inline (RecordingAlertPresenter's own TaskCompletionSource uses RunContinuationsAsynchronously, the
            // same shape AppUpdaterFailureAlertTests.cs's identical Dismiss-then-pump already needs).
            harness.Main.Loop.RunUntilIdle();
            harness.Main.OnMain(() => Assert.Null(model.Alert)); // :83-84 set: if !$0 { model.alert = nil }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-067")] // App/Views/RootView.swift:69-76
    public void The_command_palette_appears_above_the_genie_and_row_glide_layers_and_leaves_entirely_when_dismissed()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            Control host = null!;
            harness.Main.OnMain(() =>
            {
                host = Build(model, () => new RootView());
                Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(host)); // :73 not presented yet
                model.IsCommandPalettePresented = true; // :73
            });
            harness.Main.Loop.RunUntilIdle(); // Tracked<T>'s reactive update (after the first, synchronous one) is posted
            harness.Main.OnMain(() =>
            {
                var order = LogicalQuery.SelfAndDescendants<Control>(host).ToList();
                var genieIndex = order.FindIndex(control => control is GenieLayer);
                var rowGlideIndex = order.FindIndex(control => control is RowGlideLayer);
                var paletteIndex = order.FindIndex(control => control is CommandPalette);
                Assert.True(genieIndex >= 0, "GenieLayer is missing"); // :70
                Assert.True(rowGlideIndex >= 0, "RowGlideLayer is missing"); // :71
                Assert.True(paletteIndex >= 0, "CommandPalette is missing while presented"); // :72-74
                // :69-76 chained in this order: the later .overlay draws over the earlier ones.
                Assert.True(paletteIndex > genieIndex, "the palette must draw above the genie layer");
                Assert.True(paletteIndex > rowGlideIndex, "the palette must draw above the row-glide layer");

                model.IsCommandPalettePresented = false; // :73
            });
            harness.Main.Loop.RunUntilIdle();
            harness.Main.OnMain(() =>
                Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(host))); // :74 "holds no state between openings"
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:64-68
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:33-43; UI/Chrome/Chrome.swift:9-11
    public void The_real_body_keeps_the_backdrop_inside_its_window_clip_and_the_detail_inside_zero_insets()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Assert.IsType<Border>(Build(model, () => new RootView()));
                var root = Assert.IsType<RootView>(host.Child);
                var window = Assert.IsAssignableFrom<AvaloniaWindow>(TopLevel.GetTopLevel(root));
                try
                {
                    window.Width = 900;
                    window.Height = 600;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(root.Bounds.Size, root.WindowClip.Bounds.Size); // :68 ignoresSafeArea
                    Assert.NotNull(root.WindowClip.Clip);
                    Assert.Equal(root.WindowClip.Bounds.Size, root.WindowClip.Clip.Bounds.Size); // backdrop shares the window's clip
                    Assert.Single(LogicalQuery.SelfAndDescendants<WindowBackdrop>(root.WindowClip)); // :64 before clipShape
                    Assert.Single(LogicalQuery.SelfAndDescendants<DetailView>(root.DetailClip)); // :40 clips the detail itself
                    var padding = Assert.IsType<Border>(root.DetailClip.GetVisualParent());
                    Assert.Equal(new Thickness(0), padding.Padding); // :41-43 Chrome.sheetInset == 0
                    Assert.True(root.DetailClip.ClipToBounds);
                    Assert.True(root.DetailClip.Bounds.Width > 0);
                    using var image = window.CaptureRenderedFrame();
                    Assert.NotNull(image); // forces the composited tree to draw as well as lay out
                    window.Width = 760;
                    window.UpdateLayout();
                    Assert.Equal(root.WindowClip.Bounds.Size, root.WindowClip.Clip.Bounds.Size); // :66 the clip follows the resized window
                }
                finally { window.Hide(); Dispatcher.UIThread.RunJobs(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-067")] // App/Views/RootView.swift:69-76
    public void The_three_overlay_layers_share_the_whole_window_origin_and_palette_state_is_fresh_on_reopen()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            Border host = null!;
            RootView root = null!;
            AvaloniaWindow window = null!;
            CommandPalette first = null!;
            try
            {
                harness.Main.OnMain(() =>
                {
                    host = Assert.IsType<Border>(Build(model, () => new RootView()));
                    root = Assert.IsType<RootView>(host.Child);
                    window = Assert.IsAssignableFrom<AvaloniaWindow>(TopLevel.GetTopLevel(root));
                    model.IsCommandPalettePresented = true;
                });
                harness.Main.OnMain(() =>
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var layers = Assert.IsType<Panel>(root.Child);
                    Assert.Equal(4, layers.Children.Count); // clipped body, genie, row glide, palette host
                    Assert.Same(root.WindowClip, layers.Children[0]);
                    Assert.IsType<GenieLayer>(layers.Children[1]);
                    Assert.IsType<RowGlideLayer>(layers.Children[2]);
                    var paletteHost = Assert.IsType<Panel>(layers.Children[3]);
                    first = Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(paletteHost));
                    foreach (var layer in layers.Children)
                    {
                        Assert.Equal(root.Bounds.Size, layer.Bounds.Size); // covers sidebar as well as detail
                        Assert.Equal(new Point(0, 0), layer.TranslatePoint(default, window)); // one shared window origin
                    }
                    model.IsCommandPalettePresented = false;
                });
                harness.Main.OnMain(() =>
                {
                    Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
                    model.IsCommandPalettePresented = true;
                });
                harness.Main.OnMain(() =>
                {
                    Assert.NotSame(first, Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(root))); // :73 no state held between openings
                    model.IsCommandPalettePresented = false;
                });
            }
            finally { harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-WIN-066")] // App/Views/RootView.swift:80-91
    public void A_model_alert_is_owned_by_the_rendering_window_and_clearing_the_model_closes_it(bool beforeAttach)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            // R-VIEW-9: record the owned ShowDialog request; headless opens the same real dialog nonmodally.
            var property = typeof(RootView).GetProperty("PresentAlertDialog", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);
            var presentation = Assert.IsType<Ambient<Func<AvaloniaWindow, AvaloniaWindow, System.Threading.Tasks.Task>>>(property.GetValue(null));
            AvaloniaWindow? dialog = null;
            AvaloniaWindow? recordedOwner = null;
            using var presenter = presentation.Override((alertWindow, owner) =>
            {
                dialog = alertWindow;
                recordedOwner = owner;
                Assert.True(owner.IsVisible); // an owned ShowDialog requires an already presented owner
                var completion = new System.Threading.Tasks.TaskCompletionSource();
                alertWindow.Closed += (_, _) => completion.TrySetResult();
                alertWindow.Show();
                return completion.Task;
            });
            AvaloniaWindow? window = null;
            try
            {
                harness.Main.OnMain(() =>
                {
                    var alert = new AppAlert { Title = "", Message = "Owned message" }; // :81 empty title is preserved
                    if (beforeAttach) model.Alert = alert;
                    var host = Build(model, () => new RootView());
                    window = Assert.IsAssignableFrom<AvaloniaWindow>(TopLevel.GetTopLevel(host));
                    if (!beforeAttach) model.Alert = alert;
                });
                harness.Main.OnMain(() =>
                {
                    Assert.NotNull(dialog);
                    Assert.Same(window, recordedOwner); // window-modal, not an independent Window.Show()
                    Assert.True(dialog.IsVisible);
                    Assert.Equal("", dialog.Title);
                    Assert.Contains("Owned message", AllText(dialog), StringComparison.Ordinal);
                    var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(dialog));
                    Assert.Equal("OK", Assert.IsType<TextBlock>(button.Content).Text); // ED-216 shared button label; same source title
                    Assert.True(button.IsDefault); // Enter
                    Assert.True(button.IsCancel); // Escape, :88 cancel role
                    model.Alert = null; // :83-84 the binding must also close the presented alert
                });
                harness.Main.OnMain(() => Assert.False(dialog!.IsVisible));
            }
            finally { harness.Main.OnMain(() => { dialog?.Close(); window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:14-43 overlay at 22-31 takes no layout space
    public void The_sidebar_resize_overlay_does_not_reserve_detail_width(bool visible, bool onlyFloating) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the floating panel slides when the window resizes (FloatingSidebarPanel.swift:104)
        using var launch = new LaunchHarness();
        var model = launch.Manager.Model;
        AvaloniaWindow window = null!;
        RootView root = null!;
        try
        {
            launch.Harness.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = onlyFloating;
                model.Settings.SidebarOnlyFloats = onlyFloating;
                if (model.Sidebar.IsVisible != visible) model.Sidebar.Toggle();
                var host = Assert.IsType<Border>(Build(model, () => new RootView()));
                root = Assert.IsType<RootView>(host.Child);
                window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(root));
                window.Width = 900;
                window.Height = 600;
            });
            launch.Harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var expectedX = visible && !onlyFloating ? model.Sidebar.RenderedWidth : 0;
                Assert.Equal(expectedX, root.DetailClip.TranslatePoint(default, root)!.Value.X);
                Assert.Equal(root.Bounds.Width - expectedX, root.DetailClip.Bounds.Width);
                Assert.Equal(window.ClientSize.Width, root.Bounds.Width);
            });
        }
        finally { launch.Harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:44-51; App/Views/Sidebar/FloatingSidebarPanel.swift:23-35
    public void Resizing_the_detail_keeps_the_floating_panel_sidebar_and_search_state() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var model = launch.Manager.Model;
        AvaloniaWindow window = null!;
        RootView root = null!;
        FloatingSidebarPanel panel = null!;
        SidebarView sidebar = null!;
        TextBox search = null!;
        try
        {
            launch.Harness.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = true;
                model.Settings.SidebarOnlyFloats = true;
                var host = Assert.IsType<Border>(Build(model, () => new RootView()));
                root = Assert.IsType<RootView>(host.Child);
                window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(root));
                window.Width = 900;
                window.Height = 700;
                ResizeWindow(window, new Size(900, 700));
                window.UpdateLayout();
            });
            launch.Harness.Main.OnMain(() =>
            {
                panel = Assert.Single(LogicalQuery.SelfAndDescendants<FloatingSidebarPanel>(root));
                sidebar = Assert.Single(LogicalQuery.SelfAndDescendants<SidebarView>(panel));
                search = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sidebar));
                search.Text = "kept query";
                window.Width = 1000;
                window.Height = 740;
                ResizeWindow(window, new Size(1000, 740));
                window.UpdateLayout();
            });
            launch.Harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Same(panel, Assert.Single(LogicalQuery.SelfAndDescendants<FloatingSidebarPanel>(root)));
                Assert.Equal(1000, root.DetailClip.Bounds.Width); // exact requested geometry, within the headless screen
                Assert.Same(sidebar, Assert.Single(LogicalQuery.SelfAndDescendants<SidebarView>(panel)));
                Assert.Same(search, Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sidebar)));
                Assert.Equal("kept query", search.Text);
                Assert.Equal(root.DetailClip.Bounds.Size, panel.Bounds.Size); // new area, same instance
            });
        }
        finally { launch.Harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Theory]
    [InlineData("escape")]
    [InlineData("ok")]
    [InlineData("return")]
    [Trait("Req", "PR-WIN-066")] // App/Views/RootView.swift:80-91; ED-216 lone cancel-role OK still takes Return
    [Trait("Req", "UI-WIN-073")] // App/Views/RootView.swift:80-91 the one cancel-role OK button; dismissing clears model.alert
    public void A_replaced_alert_updates_the_owned_dialog_and_production_dismissal_clears_the_model(string dismissal) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var property = typeof(RootView).GetProperty("PresentAlertDialog", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var presentation = Assert.IsType<Ambient<Func<AvaloniaWindow, AvaloniaWindow, Task>>>(property.GetValue(null));
        AvaloniaWindow dialog = null!;
        AvaloniaWindow window = null!;
        var presentations = 0;
        using var seam = presentation.Override((shown, owner) =>
        {
            presentations++;
            dialog = shown;
            var completion = new TaskCompletionSource();
            shown.Closed += (_, _) => completion.TrySetResult();
            shown.Show();
            return completion.Task;
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new RootView());
                window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(host));
                model.Alert = new AppAlert { Title = "First title", Message = "First message" };
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("First title", dialog.Title);
                Assert.Contains("First message", AllText(dialog), StringComparison.Ordinal);
                var titleLine = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(dialog.Content).Children[0]);
                Assert.Equal("First title", titleLine.Text); // :81 and ED-216: bold first line as well as window title
                Assert.Equal(Avalonia.Media.FontWeight.Bold, titleLine.FontWeight);
                model.Alert = new AppAlert { Title = "Replacement title", Message = "Replacement message" };
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1, presentations); // update the existing owned dialog
                Assert.Equal("Replacement title", dialog.Title);
                var titleLine = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(dialog.Content).Children[0]);
                Assert.Equal("Replacement title", titleLine.Text);
                Assert.Equal(Avalonia.Media.FontWeight.Bold, titleLine.FontWeight);
                Assert.Contains("Replacement message", AllText(dialog), StringComparison.Ordinal);
                Assert.DoesNotContain("First message", AllText(dialog), StringComparison.Ordinal);
                var ok = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(dialog));
                Assert.True(ok.IsDefault);
                Assert.True(ok.IsCancel);
                if (dismissal == "ok") ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else if (dismissal == "escape") dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
                else dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            });
            harness.Main.OnMain(() =>
            {
                Assert.False(dialog.IsVisible);
                Assert.Null(model.Alert); // false presentation binding, :83-84
            });
        }
        finally { harness.Main.OnMain(() => { dialog?.Close(); window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-066")] // App/Views/RootView.swift:80-91; ED-216 extended request, owner and cancel-role OK
    [Trait("Req", "UI-WIN-073")] // App/Views/RootView.swift:80-91 the one cancel-role OK button; dismissing clears model.alert
    public void The_model_alert_uses_the_extended_shared_request_with_its_rendering_owner_and_cancel_role_OK() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var recorder = new RecordingConfirmationPresenter();
        using var presenter = AlertPresentation.Presenter.Override(recorder);
        AvaloniaWindow window = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new RootView());
                window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(host));
                model.Alert = new AppAlert { Title = "Request title", Message = "Request message" };
            });
            harness.Main.OnMain(() =>
            {
                var request = Assert.Single(recorder.Shown);
                Assert.Equal("Request title", request.Title);
                Assert.Equal("Request message", request.Message);
                Assert.Same(window, request.Owner);
                Assert.Equal(AlertKind.Alert, request.Kind);
                Assert.Null(request.TextFields);
                var ok = Assert.Single(request.Buttons);
                Assert.Equal("OK", ok.Title); // :88
                Assert.Equal(AlertButtonRole.Cancel, ok.Role);
                Assert.Same(ok, request.ReturnDefault); // ED-216 lone cancel button is also the Return default
                Assert.NotNull(model.Alert);
                recorder.Cancel();
            });
            harness.Main.OnMain(() => Assert.Null(model.Alert)); // :83-84 false presentation binding
        }
        finally { harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-064")] // App/Views/RootView.swift:77-79 dropDestination(for: URL.self)
    public void Drag_over_accepts_local_file_URLs_and_rejects_text_only_drags() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var host = Assert.IsType<Border>(Build(model, () => new RootView()));
            var root = Assert.IsType<RootView>(host.Child);
            var window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(root));
            try
            {
                using var textData = new DataTransfer();
                textData.Add(DataTransferItem.CreateText("not a file URL"));
                var textDrag = new DragEventArgs(DragDrop.DragOverEvent, textData, root, default, KeyModifiers.None);
                root.RaiseEvent(textDrag);
                Assert.Equal(DragDropEffects.None, textDrag.DragEffects);
                var path = Path.Combine(harness.Scratch, "drag-over.txt");
                File.WriteAllText(path, "file");
                var file = window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)).GetAwaiter().GetResult()!;
                using var files = new DataTransfer();
                files.Add(DataTransferItem.CreateFile(file));
                var fileDrag = new DragEventArgs(DragDrop.DragOverEvent, files, root, default, KeyModifiers.None);
                root.RaiseEvent(fileDrag);
                Assert.Equal(DragDropEffects.Copy, fileDrag.DragEffects);
            }
            finally { window.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    // The no-selection failure case travels through the real drag-and-drop pipeline. The folder and selected-file
    // shapes are covered independently through HandleDropCore above.
    [Fact]
    [Trait("Req", "PR-WIN-064")] // App/Views/RootView.swift:77-79, wired onto the already-tested HandleDropCore
    [Trait("Req", "PR-WIN-065")] // App/Views/RootView.swift:106-108's own failure case
    public void Dropping_a_real_file_with_nothing_selected_reaches_HandleDrop_through_the_real_pipeline_and_discards_it()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var filePath = Path.Combine(harness.Scratch, "dropped.txt");
            File.WriteAllText(filePath, "hello");
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new RootView());
                var root = Assert.IsType<RootView>(((Border)host).Child);
                var topLevel = TopLevel.GetTopLevel(host) ?? throw new InvalidOperationException("no TopLevel");
                var storageFile = topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(filePath)).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("no storage file for " + filePath);
                var dt = new DataTransfer();
                dt.Add(DataTransferItem.CreateFile(storageFile));

                Assert.Null(model.SelectedThreadID);
                root.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, dt, root, new Point(10, 10), KeyModifiers.None));

                Assert.Null(model.SelectedThreadID); // :106 `if !files.isEmpty, let threadID = model.selectedThreadID`
                Assert.Empty(model.Projects); // a file never adds a project (:101 isDirectory.boolValue's else branch)
            });
        });
    }
    [Fact]
    [Trait("Req", "PR-WIN-067")] // App/Views/RootView.swift:69-76; App/Views/Sidebar/RowGlide.swift:224-236
    public void Opening_the_palette_during_a_live_glide_preserves_the_ghost_origin_and_layer_order() => headless.OnUiThread(() =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        using var departure = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(200, 24));
        using var arrival = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(200, 24));
        var model = harness.MakeModel();
        AvaloniaWindow window = null!;
        RootView root = null!;
        RowGlideView ghost = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                root = Assert.IsType<RootView>(Assert.IsType<Border>(Build(model, () => new RootView())).Child);
                window = Assert.IsAssignableFrom<AvaloniaWindow>(TopLevel.GetTopLevel(root));
                animator.Launch(Guid.NewGuid(), RowGlideAnimator.Glide.Direction.Settle,
                    new Rect(20, 60, 200, 24), new Rect(20, 260, 200, 24), new Rect(root.Bounds.Size),
                    departure, arrival, 1, 1);
            });
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                ghost = Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(root));
            });
            harness.Main.AdvanceMilliseconds(100); // live flight, before the source's arrival/finish deadlines
            harness.Main.OnMain(() =>
            {
                frames.Advance(TimeSpan.FromMilliseconds(100));
                var progress = (double)typeof(RowGlideView).GetField("progress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ghost)!;
                Assert.InRange(progress, double.Epsilon, 1);
                Assert.False(Assert.Single(animator.Glides).HasArrived);
                model.IsCommandPalettePresented = true;
            });
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var layers = Assert.IsType<Panel>(root.Child);
                Assert.IsType<GenieLayer>(layers.Children[1]);
                var glideLayer = Assert.IsType<RowGlideLayer>(layers.Children[2]);
                var paletteLayer = Assert.IsType<Panel>(layers.Children[3]);
                Assert.Same(ghost, Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(glideLayer)));
                Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(paletteLayer));
                Assert.Single(animator.Glides); // opening the palette must not finish or replace the active ghost
                foreach (var layer in new Control[] { layers.Children[1], glideLayer, paletteLayer })
                {
                    Assert.Equal(root.Bounds.Size, layer.Bounds.Size);
                    Assert.Equal(new Point(0, 0), layer.TranslatePoint(default, window));
                }
                model.IsCommandPalettePresented = false;
            });
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
                Assert.Same(ghost, Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(root)));
                Assert.False(Assert.Single(animator.Glides).HasArrived);
            });
        }
        finally { harness.Main.OnMain(() => { window?.Close(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-081")] // App/Views/RootView.swift:301-349 the ScrollView keeps its identity and offset while the activity style changes
    public void Changing_the_activity_style_keeps_the_welcome_page_scrolled()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            model.Settings.ActivityThreadStyle = ActivityThreadStyle.Icon;
            Control host = null!;
            ScrollViewer before = null!;
            harness.Main.OnMain(() =>
            {
                host = Build(model, () => new WelcomeView());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                before = LogicalQuery.SelfAndDescendants<ScrollViewer>(host).First();
                model.Settings.ActivityThreadStyle = ActivityThreadStyle.ProjectName;
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Same(before, LogicalQuery.SelfAndDescendants<ScrollViewer>(host).First());
                Assert.Contains(ActivityThreadStyle.ProjectName.Detail, AllText(host), StringComparison.Ordinal);
                ((AvaloniaWindow)TopLevel.GetTopLevel(host)!).Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-084")] // App/Views/RootView.swift:367-408 a row whose branch does not change keeps its trailing control (focus, spinner) when another provider's status changes
    public void A_provider_row_keeps_its_trailing_control_when_its_own_branch_is_unchanged()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var cli = SwiftEnum<ProviderKind>.AllCases.First(kind => !kind.IsAPIKeyBased);
            harness.Main.OnMain(() =>
            {
                var host = Build(model, () => new ProviderStatusRow(cli));
                var row = LogicalQuery.SelfAndDescendants<ProviderStatusRow>(host).Single();
                var trailing = (ContentControl)typeof(ProviderStatusRow).GetField("trailing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(row)!;
                var before = trailing.Content;
                Assert.NotNull(before);
                typeof(ProviderStatusRow).GetMethod("Rebuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
                Assert.Same(before, trailing.Content);
                ((AvaloniaWindow)TopLevel.GetTopLevel(host)!).Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-088")] // App/Views/RootView.swift:441-446 the body reads the ShortcutStore label, so a remap shows at once
    public void The_new_thread_chord_follows_a_shortcut_remap()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var store = ShortcutStore.Shared.Override(harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var model = harness.MakeModel();
            Control host = null!;
            harness.Main.OnMain(() =>
            {
                host = Build(model, () => new NoThreadView());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains("Ctrl+N", AllText(host), StringComparison.Ordinal);
                ShortcutStore.Shared.Current.Set(null, AppShortcut.NewThread);
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("Ctrl+N", AllText(host), StringComparison.Ordinal);
                Assert.Contains("New thread", AllText(host), StringComparison.Ordinal);
                ((AvaloniaWindow)TopLevel.GetTopLevel(host)!).Close();
            });
        });
    }
}

// Resolved STOP #3: the AlertPresenter owner's ED-216 request/window/session landed during this review pass.
// RootView now supplies its rendering owner and cancel-role OK through AlertPresentation.Show(AlertRequest),
// and uses the shared window/session for the real dialog. The source's replacement/model-clear binding is
// retained by a thin adapter; bold title, message, owned presentation and Escape/Return/OK are proven above.
// Swift: App/Views/RootView.swift:80-91 (B3 52bc792). Record: PR-WIN-066.

// STOP App.Views.RootView #4 (Window review fixes)
// Kind: platform/capture gap.
// Swift: App/Views/RootView.swift:64-68 (B3 52bc792). Record: PR-WIN-062 (partial composited-clip proof only).
// Missing (as of that review): window region or DWM corner preference so the native shadow follows the curve, and a four-corner capture.
// Needed: the native window-chrome owner supplies that region/preference and a four-corner capture verifies it.
// This review explicitly deferred implementing PR-WIN-062's remaining native/capture portion; the four-corner capture exists since (below).
// PARTLY RESOLVED (defects-priority): the owner ruled the other way for the corners (OD-0075, and his intent of 2026-10-05: the glass is extended to the window's
// edge, no window region), and that is built and proved on pixels headless (RootViewCornerWedgeTests) and on the REAL window's screen pixels
// (MainWindowCornerWedgeNativeTests, ED-338).
