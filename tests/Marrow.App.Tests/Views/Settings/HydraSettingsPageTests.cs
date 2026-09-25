using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B2 1fbf81e), :1-589.
// AppModelHarness's TestRuntime makes MainActorModel writes (ChromeSegmentedPicker/GlassPickerButton hover state
// etc. fired during AttachedToVisualTree) require an active Pump: ManualMainLoop.CheckAccess() is only true while
// RunUntilIdle() is running, so both the page's construction/attachment and any later interaction must happen
// inside harness.Main.OnMain(...), not just inside the outer headless.OnUiThread(...) dispatch (same pattern found
// in App.Views.Terminal.TerminalPanel's tests this session).
[Collection("Headless")]
public sealed class HydraSettingsPageTests(HeadlessSession headless)
{
    private static string AllText(Control control) =>
        string.Join("\n", LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? ""));

    private static Control Build(AppModel model)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new HydraSettingsPage();
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return host;
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:8-46
    public void The_switch_card_shows_its_title_and_subtitle()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model);
                var text = AllText(host);
                Assert.Contains("Hydra", text, StringComparison.Ordinal);
                Assert.Contains("One chat leads a team of heads on big jobs.", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:40-45
    public void The_off_note_shows_only_while_Hydra_is_off()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = false;
                var host = Build(model);
                Assert.Contains("Switch Hydra on to set up its heads.", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")]
    public void The_off_note_is_absent_while_Hydra_is_on()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = true;
                var host = Build(model);
                Assert.DoesNotContain("Switch Hydra on to set up its heads.", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:47-107
    public void Every_settings_section_renders_its_title()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model);
                var text = AllText(host);
                Assert.Contains("Heads", text, StringComparison.Ordinal);
                Assert.Contains("Messages", text, StringComparison.Ordinal);
                Assert.Contains("When the team is done", text, StringComparison.Ordinal);
                Assert.Contains("Pairs", text, StringComparison.Ordinal);
                Assert.Contains("Where heads run", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:52-54
    public void The_checkout_rows_detail_reflects_head_isolation()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraIsolateHeads = true;
                var host = Build(model);
                Assert.Contains("Each head works in a copy of its own; its changes land in the chat's checkout when it reports.",
                    AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")]
    public void The_checkout_rows_detail_reflects_shared_checkout()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraIsolateHeads = false;
                var host = Build(model);
                Assert.Contains("Heads work in the chat's checkout itself.", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:96
    public void The_queued_follow_ups_row_is_blocked_when_every_message_already_goes_to_a_head()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAlwaysHeads = true;
                var host = Build(model);
                Assert.Contains("Every message already goes to a head.", AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-160")] // App/Views/Settings/HydraSettingsPage.swift:141-144
    public void With_no_pairs_the_row_says_so_and_offers_an_add_button()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model);
                Assert.Contains("No pairs yet", AllText(host), StringComparison.Ordinal);
                var addButtons = LogicalQuery.SelfAndDescendants<Button>(host)
                    .Where(button => AllText(button).Contains("Add pair", StringComparison.Ordinal))
                    .ToArray();
                Assert.NotEmpty(addButtons);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-160")] // App/Views/Settings/HydraSettingsPage.swift:145-151
    public void One_row_renders_per_pair_and_titles_it_with_the_pair_summary()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var pair = new HydraPair(ProviderKind.Claude);
            harness.Main.OnMain(() =>
            {
                model.Settings.AddHydraPair(pair);
                var host = Build(model);
                var title = HydraPairSummary.Title(pair, model.Providers);
                Assert.Contains(title, AllText(host), StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-161")] // App/Views/Settings/HydraSettingsPage.swift:277-288
    public void Clicking_a_pair_rows_remove_button_removes_it()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var pair = new HydraPair(ProviderKind.Claude);
            harness.Main.OnMain(() =>
            {
                model.Settings.AddHydraPair(pair);
                var host = Build(model);

                var remove = LogicalQuery.SelfAndDescendants<Button>(host)
                    .Single(button => (ToolTip.GetTip(button) as string) == "Remove this pair");
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Empty(model.Settings.HydraPairs);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-162")] // App/Views/Settings/HydraSettingsPage.swift:222-236
    public void Clicking_add_pair_adds_one_pair()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model);
                var before = model.Settings.HydraPairs.Count;

                var add = LogicalQuery.SelfAndDescendants<Button>(host)
                    .First(button => AllText(button).Contains("Add pair", StringComparison.Ordinal));
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(before + 1, model.Settings.HydraPairs.Count);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:167-189
    public void Where_heads_run_names_the_agent_and_thread_providers()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model);
                var text = AllText(host);
                Assert.Contains("Claude, Codex and Copilot", text, StringComparison.Ordinal);
                Assert.Contains("Other providers", text, StringComparison.Ordinal);
                Assert.Contains("Across providers", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:194-200
    public void ToggleRow_dims_and_disables_while_blocked()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAlwaysHeads = true;
                var host = Build(model);

                var row = LogicalQuery.SelfAndDescendants<ChromeRow>(host)
                    .Single(candidate => AllText(candidate).Contains("Queued follow-ups start on heads", StringComparison.Ordinal));
                Assert.False(row.IsEnabled);
                Assert.Equal(0.5, row.Opacity);
            });
        });
    }
}

// STOP App.Views.Settings.HydraSettingsPage #1 (helper Claude, 2026-09-25)
// Kind: missing dependency
// Swift: DroppyCode/App/AppModel+HydraPairs.swift:88-116; App/Views/Settings/HydraSettingsPage.swift:278,389
// Requirements: PR-SETTINGS-161, PR-SETTINGS-162 (partial: the pair edits and removal work, but skip real-app
// side effects a hand-verified UI pass alone would not catch)
// What is unclear or missing: HydraPairRow's remove button (:278) and HydraPairEditor's field writes (:389) call
// AppModel.removeHydraPair/updateHydraPair, not AppSettings.RemoveHydraPair/UpdateHydraPair directly. The AppModel
// versions (App/AppModel+HydraPairs.swift:88-116, confirmed by reading that file, not yet ported - no
// AppModel+HydraPairs.cs exists in this tree) additionally propagate the change to every open chat and project
// that references the pair: a chat leading with the pair takes the new model/effort at once (or leaves the pair
// if the lead provider changed), a removed pair clears every thread's and project's reference to it and the
// remembered lastHydraPairID. This unit calls the plain AppSettings-level methods instead, which write the pair
// itself correctly but skip that propagation entirely.
// Readings or options: (a) port AppModel+HydraPairs.swift's two methods here too -> scope creep into a separate,
// already-inventoried unit not covered by this packet; (b) call the settings-level methods and document the gap,
// as done here, matching the App.DroppyCodeApp #1 and App.Views.Chat.HydraProgressBar #1 precedent for a real
// missing cross-unit dependency.
// Needed to proceed: App.AppModel_HydraPairs (or wherever AppModel+HydraPairs.swift lands) ported, then swap the
// two call sites in HydraSettingsPage.cs (HydraPairRow's remove button, HydraPairEditor.Update) from
// model.Settings.RemoveHydraPair/UpdateHydraPair to model.RemoveHydraPair/UpdateHydraPair.
// Done so far: every other requirement in this packet is implemented and tested above, including 0 MARROW_STUB
// across all six types this file declares. SettingsSwitch (Views/Settings/SettingsView.cs), a separate not-yet-
// started unit's own MARROW_STUB that nearly every toggle row in this page needs, was also implemented here as a
// small, self-contained 8-line fix (its Swift body is a bare labelless Toggle) rather than left blocking the
// whole page; the rest of SettingsView.swift's much larger surface is untouched, left for that unit's own work.
