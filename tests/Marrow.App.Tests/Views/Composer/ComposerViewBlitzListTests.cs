using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzListTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzRingTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzUsageTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :190-273, :311-330, :426-501 and the slash and @ list
// the pill hosts (the blitz-composerview unit).
[Collection("Headless")]
public sealed class ComposerViewBlitzListTests(HeadlessSession headless)
{
    // Raises the Deactivated or Activated callback the headless window platform hands the window: the window's own IsActive
    // flips as an operating-system focus change flips it, and the text box keeps its own keyboard focus.
    internal static void SetWindowActive(AvaloniaWindow window, bool active)
    {
        const System.Reflection.BindingFlags all =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var platform = window.PlatformImpl!;
        var callback = (Action?)platform.GetType().GetProperty(active ? "Activated" : "Deactivated", all)!.GetValue(platform);
        callback!.Invoke();
        Settle(window);
    }

    // Until, naming what never happened when the wait runs out.
    internal static void UntilBecause(string what, ThreadRuntimePart12Harness h, AvaloniaWindow? window, Func<bool> condition)
    {
        try { Until(h, window, condition); }
        catch (TimeoutException) { throw new TimeoutException("never happened: " + what + "; errors: " + string.Join(" | ", h.App.Providers.CommandErrors.Values)); }
    }

    // ---- PR-COMPOSER-039 (:463) ----

    [Theory]
    [InlineData("a /pl")]
    [InlineData("\t/pl")]
    [InlineData("a\n/pl")]
    [InlineData(" /")]
    [Trait("Req", "PR-COMPOSER-039")] // App/Views/Composer/ComposerView.swift:463-466
    [Trait("Rule", "R-TEST-4")] // The slash at the start offers commands: A_slash_word_at_the_very_start_of_the_draft_offers_commands; the refresh is cancellable: Each_caret_report_cancels_the_refresh_...
    public void A_slash_word_that_does_not_start_the_draft_closes_the_list_and_offers_neither_commands_nor_files(string text)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/pl"));
            Until(h, null, () => view.Suggestions.IsVisible); // the control: the same word at the start does open it
            Assert.Equal(SuggestionState.Kind.Command, view.Suggestions.SuggestionKind);
            h.Main.OnMain(() => Type(view, text));
            Until(h, null, () => !view.Suggestions.IsVisible); // the refresh for this word ran and found no list
            Assert.Equal(SuggestionState.Kind.File, view.Suggestions.SuggestionKind); // neither list's state is left behind
        });
    }

    // ---- PR-COMPOSER-040 (:475-477) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-040")] // App/Views/Composer/ComposerView.swift:475-477
    [Trait("Rule", "R-TEST-4")] // The built-in's name, detail, flag and symbol: The_command_list_always_holds_plan_built_in_with_its_detail_and_the_command_symbol
    public void A_runtime_with_no_resolved_thread_offers_no_commands_at_all()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var view = Pill(h, Unresolved(h), workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            h.Main.RunUntilIdle();
            Until(h, null, () => true);
            Assert.False(view.Suggestions.IsVisible); // :476 the thread is not there: an empty list, not a list of /plan
            // The control: the same keystroke on a resolved thread offers it.
            var resolved = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(resolved, "/"));
            Until(h, null, () => resolved.Suggestions.IsVisible);
            Assert.Contains("/plan", Titles(resolved));
        });
    }

    // ---- PR-COMPOSER-041 (:478-480; Provider.swift:149-154) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-041")] // App/Views/Composer/ComposerView.swift:478-480; Core/Models/Provider.swift:149-154
    [Trait("Decision", "ED-234")] // OpenRouter is the port's own provider (OD-0049, OD-0060): it has no Swift case, and ED-234 lets it compact
    public void Compact_is_offered_on_the_seven_source_providers_and_on_no_other_source_provider_and_the_port_only_OpenRouter_compacts_by_ED_234()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            ProviderKind[] sevenSource =
            [
                ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Copilot, ProviderKind.Deepseek, ProviderKind.Meta,
                ProviderKind.Zai, ProviderKind.Pi,
            ];
            var offered = new List<ProviderKind>();
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            {
                h.Main.OnMain(() =>
                {
                    h.App.UpdateThread(h.Thread.Id, thread => thread with { Provider = provider });
                    Type(view, "");
                });
                Until(h, null, () => !view.Suggestions.IsVisible);
                h.Main.OnMain(() => Type(view, "/"));
                Until(h, null, () => view.Suggestions.IsVisible);
                var compact = view.Suggestions.Items.Where(item => item.Title == "/compact").ToArray();
                if (compact.Length == 0) continue;
                offered.Add(provider);
                var only = Assert.Single(compact);
                Assert.Equal("/compact ", only.Value);
                Assert.Equal("Summarize the conversation to free up context", only.Detail);
                Assert.Equal("command", only.Symbol);
                Assert.True(only.Command!.IsBuiltIn);
                Assert.Equal("Summarize the conversation to free up context", only.Command.Detail);
            }
            Assert.Equal(sevenSource.Append(ProviderKind.OpenRouter).Append(ProviderKind.Local).Order().ToArray(), offered.Order().ToArray()); // ED-410
            Assert.Equal(7, offered.Count(provider => provider is not (ProviderKind.OpenRouter or ProviderKind.Local)));
        });
    }

    // ---- PR-COMPOSER-043 (:482-484) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-043")] // App/Views/Composer/ComposerView.swift:482-484; Services/Providers: OD-0010 symbol table; no reference video shows the command list (CATALOG has no row for it): OD-0098, the Swift is the proof
    public void Each_command_row_reads_slash_name_with_its_detail_trailing_and_the_command_or_sparkles_symbol_drawn_from_the_lucide_table()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("rows-rendered");
            var view = Pill(h, h.Runtime, directory);
            h.Main.OnMain(() =>
            {
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new SlashCommand { Name = "review", Detail = "Review the diff" }]), ProviderKind.Codex, directory);
                Type(view, "/");
            });
            Until(h, null, () => view.Suggestions.IsVisible && view.Suggestions.Items.Count == 3);
            var items = view.Suggestions.Items.ToArray();
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            var popup = new Popup();
            var frame = h.Main.OnMain(() =>
            {
                popup.SetFixedContent(menu, new Size(340, 200));
                var made = popup.Child!;
                popup.Child = null;
                return made;
            });
            var window = h.Main.OnMain(() => Show(frame, h.App, 340));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var rows = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(menu).ToArray();
                Assert.Equal(items.Length, rows.Length);
                Assert.Contains(items, item => item.Command!.IsBuiltIn);
                Assert.Contains(items, item => !item.Command!.IsBuiltIn);
                for (var index = 0; index < rows.Length; index++)
                {
                    var (row, item) = (rows[index], items[index]);
                    var command = item.Command!;
                    Assert.Equal("/" + command.Name + " ", item.Value); // the value that would be inserted
                    Assert.Equal("/" + command.Name, One<MiddleTruncatingText>(row).FullText); // the title
                    var detail = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(text => text.Text == command.Detail);
                    Assert.Equal(HorizontalAlignment.Right, detail.HorizontalAlignment); // the detail on the trailing side
                    Assert.True(Frame(detail, row).X > Frame(One<MiddleTruncatingText>(row), row).Right);
                    var symbol = One<SwiftImage>(row);
                    var expected = command.IsBuiltIn ? "command" : "sparkles";
                    Assert.Equal(expected, symbol.SymbolName);
                    Assert.Equal(expected, symbol.Mapping!.Lucide); // OD-0010: the published symbol-by-symbol table, one for one
                    Assert.False(symbol.IsEmpty);
                }
            });
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-044 (:467-472) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-044")] // App/Views/Composer/ComposerView.swift:467-472
    public void The_highlight_is_kept_clamped_for_the_same_word_reset_for_another_and_an_empty_list_closes_instead_of_clamping()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("highlight-all");
            Touch(directory, "aa1.txt", "aa2.txt", "aa3.txt", "ab1.txt", "ab2.txt", "ab3.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@a"));
            Until(h, window, () => view.Suggestions.Items.Count == 6);
            for (var step = 0; step < 4; step++) Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal(4, view.Suggestions.Selected);

            h.Main.OnMain(() => Type(view, "@aa")); // the same word, from the same place: kept, clamped to the three left
            Until(h, window, () => view.Suggestions.Items.Count == 3);
            Assert.Equal(2, view.Suggestions.Selected);
            Assert.Equal(0, view.Suggestions.Range.Location);

            h.Main.OnMain(() => Type(view, "@aa @a")); // a word starting somewhere else: back to the first row
            Until(h, window, () => view.Suggestions.Range.Location == 4);
            Assert.Equal(0, view.Suggestions.Selected);

            h.Main.OnMain(() => window.KeyTextInput("zz")); // typed on at the caret: "@azz" matches nothing, so the list closes, no highlight is clamped
            Until(h, window, () => !view.Suggestions.IsVisible && !presenter.Calls[^1].Presented);
            Assert.Empty(view.Suggestions.Items);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-045 (:312-330, :429-437) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:323-326, :429-437
    [Trait("Rule", "R-TEST-4")] // The arrows, Tab, both Returns and Backspace: the four tests above this one; the caret move clearing the latch: Escape_latches_the_list_shut_...
    public void Escape_latches_so_a_refresh_still_queued_for_the_last_keystroke_does_not_reopen_the_list()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/p"));
            Until(h, null, () => view.Suggestions.IsVisible);
            // One turn: a further keystroke queues its refresh, then Escape closes the list before that refresh has run.
            var handled = h.Main.OnMain(() =>
            {
                Type(view, "/pl");
                Assert.True(view.Suggestions.IsVisible);
                return Press(view, Key.Escape);
            });
            Assert.True(handled);
            Assert.False(view.Suggestions.IsVisible);
            h.Main.RunUntilIdle();
            Until(h, null, () => true);
            Assert.False(view.Suggestions.IsVisible); // :434 the queued refresh saw the latch and stood down
            h.Main.OnMain(() => Type(view, "/pla")); // the next caret move clears it
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(["/plan"], Titles(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:320-322; upstream_defects.md entry 50
    [Trait("Rule", "R-TEST-4")] // The commits themselves: Tab_Return_and_Command_Return_each_commit_the_highlighted_row
    public void A_highlight_past_the_end_of_an_open_list_commits_nothing_and_the_key_is_still_consumed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            // The state the Swift would trap on: a stale hover can leave the highlight past the rows.
            var state = typeof(ComposerView).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
            var property = state.GetType().GetProperty("Suggestions")!;
            h.Main.OnMain(() => property.SetValue(state, view.Suggestions with { Selected = 7 }));
            foreach (var key in new[] { Key.Enter, Key.Tab })
            {
                Assert.True(h.Main.OnMain(() => Press(view, key)));
                Assert.Equal("/", runtime.Draft.Text);
                Assert.Null(runtime.Draft.Command);
                Assert.True(view.Suggestions.IsVisible);
            }
            Assert.Empty(runtime.Turns);
            Assert.Empty(runtime.FollowUps);
        });
    }

    // ---- PR-COMPOSER-048 (:244; Popovers.swift:117-133) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-048")] // App/Views/Composer/ComposerView.swift:244; UI/Chrome/Popovers.swift:117-133; no reference video shows either list: OD-0098, the Swift is the proof
    public void Both_headers_read_their_exact_words_in_11_point_semibold_secondary_text_with_the_8_6_8_3_margins_as_the_panel_shows_them()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("headers");
            Touch(directory, "main.swift", "mail.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, h.Runtime, directory);
            void AssertHeader(string words)
            {
                var header = One<PopoverSectionHeader>(Shown(presenter));
                var text = Assert.IsType<TextBlock>(header.Child);
                Assert.Equal(words, text.Text);
                Assert.Equal(11, text.FontSize);
                Assert.Equal(FontWeight.SemiBold, text.FontWeight);
                Assert.Equal(new Thickness(8, 6, 8, 3), text.Margin);
                Assert.Equal(HorizontalAlignment.Left, text.HorizontalAlignment);
                Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            }
            h.Main.OnMain(() => Type(view, "@mai"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0 && presenter.Calls[^1].Presented);
            Assert.Equal(SuggestionState.Kind.File, view.Suggestions.SuggestionKind);
            h.Main.OnMain(() => AssertHeader("Files"));
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, window, () => view.Suggestions.SuggestionKind == SuggestionState.Kind.Command && view.Suggestions.IsVisible);
            Pump(h, window);
            h.Main.OnMain(() => AssertHeader("Commands \U000000B7 Recently used first")); // U+00B7 MIDDLE DOT
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-050 (:245-248) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:245-248; Services/Providers/ProviderRegistry.swift:855-858
    [Trait("Rule", "R-TEST-4")] // The first failure's note and row: A_failed_command_load_shows_its_description_...; the forced reload: B3_retry_enters_a_new_command_load_...
    public void A_retry_that_fails_again_replaces_the_note_with_the_new_description_and_the_panel_keeps_its_one_note_and_one_retry_row()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("retry-fails-again");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            // The handshake that answers without a command list is a different failure from the one that never answers.
            var other = ProviderProbeStandIns.CopyFor("composer-retry-other-error").SetExitCode(0)
                .SetStdout("{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{}}}\n");
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude);
                ((HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!).Add(ProviderKind.Claude);
            });
            var (view, window, _, _) = OnScreen(h, h.Runtime, directory);
            try
            {
                UntilBecause("the mounted pill's load failed", h, window, () => h.App.Providers.CommandErrors.ContainsKey(key));
                Assert.Equal("Claude's command discovery did not finish. Try again.", h.App.Providers.CommandErrors[key]);
                h.Main.OnMain(() => Type(view, "/"));
                UntilBecause("the panel showed the first note", h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0 && presenter.Calls[^1].Presented
                    && LogicalQuery.SelfAndDescendants<PopoverNote>(Shown(presenter)).Any());
                Assert.Equal("Claude's command discovery did not finish. Try again.",
                    Assert.IsType<TextBlock>(One<PopoverNote>(Shown(presenter)).Child).Text);
                var rows = view.Suggestions.Items.Count;
                // Opening the list asks for a (stale) load itself; a retry clicked while that is in flight would only join it.
                var loads = (System.Collections.IDictionary)typeof(ProviderRegistry)
                    .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;
                UntilBecause("the load the opening started finished", h, window, () => !loads.Contains(key));
                var before = presenter.Calls.Count;
                h.Main.OnMain(() => h.App.Settings.SetBinaryPath(other.ExecutablePath, ProviderKind.Claude));
                h.Main.OnMain(() => Click(One<Button>(One<PopoverItem>(Shown(presenter)))));
                UntilBecause("the registry stored the second error", h, window, () => h.App.Providers.CommandErrors.TryGetValue(key, out var error) && error == "Claude could not load its commands.");
                UntilBecause("the panel showed the second note", h, window, () => presenter.Calls.Count > before && presenter.Calls[^1].Presented
                    && Assert.IsType<TextBlock>(One<PopoverNote>(Shown(presenter)).Child).Text == "Claude could not load its commands.");
                var panel = Shown(presenter);
                Assert.Single(LogicalQuery.SelfAndDescendants<PopoverNote>(panel)); // replaced, not added to
                Assert.Single(LogicalQuery.SelfAndDescendants<PopoverItem>(panel));
                Assert.Equal(rows, LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(panel).Count());
                Assert.Equal("Retry loading commands", Avalonia.Automation.AutomationProperties.GetName(One<Button>(One<PopoverItem>(panel))));
                Assert.Equal("arrow.clockwise", LogicalQuery.SelfAndDescendants<SwiftImage>(One<PopoverItem>(panel)).Single(image => image.SymbolName is not null).SymbolName);
                Assert.Equal("arrow.clockwise", One<SwiftImage>(One<PopoverItem>(panel)).Mapping!.Sf);
                Assert.False(One<SwiftImage>(One<PopoverItem>(panel)).IsEmpty); // OD-0010: drawn from the published table
            }
            finally
            {
                Close(h, window);
            }
        });
    }

    // ---- PR-COMPOSER-051 (:264-273; ProviderRegistry.swift:815-830, :841-863) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:264-273; Services/Providers/ProviderRegistry.swift:841-863
    [Trait("Rule", "R-TEST-4")] // The key's shape: Loading_commands_asks_the_registry_...; no directory: Without_a_working_directory_...; 600 s: B3_opening_the_command_panel_starts_a_stale_load_...
    public void Two_composers_on_one_provider_and_directory_launch_one_command_listing_and_both_see_its_list()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var script = ProviderProbeStandIns.CopyFor("composer-two-composers").WaitForRelease().SetExitCode(0)
                .SetStdout("{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[{\"name\":\"proof-shared\",\"description\":\"One listing for both\"}]}}}\n");
            var directory = h.Store.NewFolder("two-composers");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude));
            var first = Pill(h, h.Runtime, directory);
            var second = Pill(h, h.Runtime, directory);
            var loads = (System.Collections.IDictionary)typeof(ProviderRegistry)
                .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;
            try
            {
                var tasks = h.Main.OnMain(() => new[] { first.LoadCommands(), second.LoadCommands() });
                Until(h, null, () => script.CommandListings().Count == 1 && loads.Contains(key));
                for (var round = 0; round < 4; round++) Until(h, null, () => true); // the second composer has had every turn to launch its own
                Assert.Single(script.CommandListings());
                Assert.False(tasks[0].IsCompleted);
                Assert.False(tasks[1].IsCompleted); // the second awaits the first's listing
                script.Release();
                h.Main.Loop.Run(() => System.Threading.Tasks.Task.WhenAll(tasks), AppModelHarness.Bound);
                Assert.Single(script.CommandListings());
                Assert.Equal(["proof-shared"], h.App.Providers.Commands[key].Select(command => command.Name).ToArray());
                foreach (var view in new[] { first, second })
                {
                    h.Main.OnMain(() => Type(view, "/proof"));
                    Until(h, null, () => view.Suggestions.IsVisible);
                    Assert.Equal(["/proof-shared"], Titles(view));
                }
            }
            finally
            {
                script.Release();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // Services/Providers/ProviderRegistry.swift:819-830
    [Trait("Rule", "R-TEST-4")] // The rest of the clause: the test above and the PR-PROVREG-213/214/217 tests
    public void The_33rd_key_evicts_the_least_recently_fetched_one_together_with_its_error_entry()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var directories = Enumerable.Range(0, 33).Select(index => h.Store.NewFolder("evict" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            var oldest = ProviderRegistry.CommandKey(ProviderKind.Claude, directories[0]);
            var list = new ValueList<SlashCommand>([new SlashCommand { Name = "cached", Detail = "Cached" }]);
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(list, ProviderKind.Claude, directories[0])); // the oldest fetch
            // A failed reload keeps the cached list and stores its error under the same key.
            h.Main.Loop.Run(() => h.App.Providers.LoadCommands(ProviderKind.Claude, directories[0], force: true), AppModelHarness.Bound);
            Assert.True(h.App.Providers.CommandErrors.ContainsKey(oldest));
            Assert.True(h.App.Providers.Commands.ContainsKey(oldest));
            for (var index = 1; index < 32; index++)
            {
                h.Main.Advance(TimeSpan.FromSeconds(1));
                h.Main.OnMain(() => h.App.Providers.UpdateCommands(list, ProviderKind.Claude, directories[index]));
            }
            Assert.Equal(32, h.App.Providers.Commands.Count);
            Assert.True(h.App.Providers.Commands.ContainsKey(oldest)); // not yet: 32 keys fit
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(list, ProviderKind.Claude, directories[32]));
            Assert.Equal(32, h.App.Providers.Commands.Count);
            Assert.False(h.App.Providers.Commands.ContainsKey(oldest));
            Assert.False(h.App.Providers.CommandErrors.ContainsKey(oldest)); // the error entry went with it
            Assert.True(h.App.Providers.Commands.ContainsKey(ProviderRegistry.CommandKey(ProviderKind.Claude, directories[1])));
            Assert.True(h.App.Providers.Commands.ContainsKey(ProviderRegistry.CommandKey(ProviderKind.Claude, directories[32])));
        });
    }

    // ---- PR-COMPOSER-052 (:205-215) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:205-207; App/Views/Composer/ComposerTextView.swift:33-36
    [Trait("Rule", "R-TEST-4")] // The delayed list and error, and the load the opening starts: the three B3_ tests of this record
    public void Commands_landing_refresh_the_list_only_while_the_text_view_has_focus_in_the_active_window()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("landing-focus");
            var (view, window, other, _) = OnScreen(h, h.Runtime, directory);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, window, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));

            // The keyboard is elsewhere: the blur closed the list, and a list landing now reads nothing.
            h.Main.OnMain(() => other.Focus());
            Until(h, window, () => !view.Suggestions.IsVisible);
            var calls = presenter.Calls.Count;
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new SlashCommand { Name = "review", Detail = "Review diff" }]), ProviderKind.Codex, directory));
            Pump(h, window);
            h.Main.RunUntilIdle();
            Pump(h, window);
            Assert.False(view.Suggestions.IsVisible);
            Assert.Equal(calls, presenter.Calls.Count);

            // The text view has the keyboard but the window is not the active one: the same.
            h.Main.OnMain(() => One<ComposerNSTextView>(view).Focus());
            Pump(h, window);
            h.Main.OnMain(() => SetWindowActive(window, false));
            Assert.False(h.Main.OnMain(() => view.Controller.HasFocus));
            Assert.True(h.Main.OnMain(() => One<ComposerNSTextView>(view).IsFocused));
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new SlashCommand { Name = "build", Detail = "Build it" }]), ProviderKind.Codex, directory));
            Pump(h, window);
            h.Main.RunUntilIdle();
            Pump(h, window);
            Assert.False(view.Suggestions.IsVisible);
            Assert.Equal(calls, presenter.Calls.Count);

            // The control: active, focused, the next list lands and the draft's "/" offers it.
            h.Main.OnMain(() => SetWindowActive(window, true));
            Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new SlashCommand { Name = "ship", Detail = "Ship it" }]), ProviderKind.Codex, directory));
            Until(h, window, () => view.Suggestions.IsVisible && Titles(view).Contains("/ship"));
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-053 (:228-237) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-053")] // App/Views/Composer/ComposerView.swift:228-237; App/Views/Composer/ComposerTextView.swift:130-134
    [Trait("Rule", "R-TEST-4")] // A click landing inside the open panel (IsPointerOverPopup) needs a real popup window: ComposerSuggestionsNativeTests. Focus leaving for another control: Focus_leaving_for_a_control_outside_the_panel_...
    public void Losing_focus_while_a_refresh_is_still_searching_cancels_it_so_it_cannot_put_the_list_back()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("blur-searching");
            Touch(directory, "main.swift", "mail.txt");
            Indexed(h, directory);
            var (view, window, other, _) = OnScreen(h, Unresolved(h), directory);
            // One turn: the keystroke's @ refresh starts its search off the main actor, and the text view loses focus.
            var refresh = h.Main.OnMain(() =>
            {
                Type(view, "@ma");
                var pending = view.Controller.SuggestionRefresh;
                other.Focus();
                return pending;
            });
            Assert.NotNull(refresh);
            Until(h, window, () => refresh!.IsCancelled);
            h.Main.RunUntilIdle();
            Pump(h, window);
            Assert.False(view.Suggestions.IsVisible); // the answer arrived to a cancelled refresh and was dropped
            Assert.DoesNotContain(presenter.Calls, call => call.Presented);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-055 (:260, :1096-1099) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-055")] // App/Views/Composer/ComposerView.swift:1096-1099; chrome-design-tokens.md:285 Chrome.hover
    [Trait("Rule", "R-TEST-4")] // Hover selecting the row Return commits: Hovering_a_row_highlights_it_so_Return_commits_that_row; the scroll: Selecting_a_row_far_down_a_long_list_scrolls_it_into_view
    public void The_hovered_rows_fill_fades_in_over_a_tenth_of_a_second_on_an_ease_out()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("hover-fade");
            Touch(directory, "ma1.txt", "ma2.txt", "ma3.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.Items.Count == 3 && presenter.Calls.Count > 0);
            var rows = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(Shown(presenter)).ToArray();
            double Alpha(SuggestionPopoverRow row) =>
                Assert.IsAssignableFrom<ISolidColorBrush>(LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Background is not null).Background).Color.A / 255.0;
            var target = Chrome.Overlay(0.1).Resolve(rows[2].ActualThemeVariant).A / 255.0;
            Assert.True(Alpha(rows[2]) == 0, "before=" + Alpha(rows[2]));
            h.Main.OnMain(() =>
            {
                var button = One<Button>(rows[2]);
                button.RaiseEvent(new PointerEventArgs(
                    InputElement.PointerEnteredEvent, button, null!, button, default, 0, new PointerPointProperties(), KeyModifiers.None));
            });
            Pump(h, window); // pumps without moving the clock, so the fade is still at its start
            Assert.Equal(2, view.Suggestions.Selected);
            Assert.True(Alpha(rows[2]) == 0, "start=" + Alpha(rows[2]));
            h.Main.Advance(TimeSpan.FromSeconds(0.05));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.05)));
            var middle = Alpha(rows[2]);
            Assert.True(middle > 0.0001 && middle < target - 0.0001, "middle=" + middle + " target=" + target); // the hover is mid-fade at half the tenth of a second
            var expected = target * Animation.EaseOut(0.1).Progress(0.05);
            Assert.True(Math.Abs(expected - middle) < 0.005, "expected=" + expected + " middle=" + middle);
            h.Main.Advance(TimeSpan.FromSeconds(0.06));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.06)));
            Assert.True(Math.Abs(target - Alpha(rows[2])) < 0.0005, "target=" + target + " end=" + Alpha(rows[2]));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-055")] // App/Views/Composer/ComposerView.swift:260 .onChange(of: suggestions.selected) { proxy.scrollTo(selected) }
    [Trait("Rule", "R-TEST-4")] // The hover half: the two tests above and Hovering_a_row_highlights_it_so_Return_commits_that_row
    public void Selecting_a_row_far_down_a_long_list_scrolls_it_into_view()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var host = new Decorator();
            AvaloniaWindow window = null!;
            // Each show's frame is put into a window at once and laid out there, as a presented popover is, so the scroll the
            // selection posts (:260) finds the list laid out.
            var presenter = new HostingPresenter(host, () => window.UpdateLayout());
            using var seam = Presentation.Presenter.Override(presenter);
            window = h.Main.OnMain(() => Show(host, h.App, 340));
            var directory = h.Store.NewFolder("long-list");
            var commands = Enumerable.Range(0, 30).Select(index => new SlashCommand
            {
                Name = "cmd" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Detail = "Command " + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).ToArray();
            var (view, pillWindow, _, _) = OnScreen(h, h.Runtime, directory);
            h.Main.OnMain(() =>
            {
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>(commands), ProviderKind.Codex, directory);
                Type(view, "/cmd");
            });
            Until(h, pillWindow, () => view.Suggestions.Items.Count == 30 && presenter.Shown > 0);
            Pump(h, window);
            var scroll = h.Main.OnMain(() => One<ScrollViewer>(host));
            Assert.Equal(340, h.Main.OnMain(() => scroll.Viewport.Height)); // the 340 point panel, with rows past its foot
            Assert.True(h.Main.OnMain(() => scroll.Extent.Height) > 340);
            Assert.Equal(0, h.Main.OnMain(() => scroll.Offset.Y));
            for (var step = 0; step < 15; step++)
            {
                Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
                Pump(h, window);
            }
            Assert.Equal(15, view.Suggestions.Selected);
            scroll = h.Main.OnMain(() => One<ScrollViewer>(host));
            h.Main.OnMain(() =>
            {
                var selected = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(host).ElementAt(15);
                var top = selected.TranslatePoint(default, scroll)!.Value.Y;
                Assert.True(scroll.Offset.Y > 0, "the list scrolled");
                Assert.InRange(top, -0.5, scroll.Viewport.Height - selected.Bounds.Height + 0.5); // the whole row is inside the viewport
            });
            Close(h, window);
            Close(h, pillWindow);
        });
    }

}
