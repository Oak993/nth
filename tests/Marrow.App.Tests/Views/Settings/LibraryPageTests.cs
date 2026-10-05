using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Skills.Terms;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.App.Tests.Views.Settings;

// SK-15 (DL-17, OD-0099 "Sections C"): the Plugins page and the Skills page, each in layout D with its own Download all. Fake backend, scripted install seam, no network and no data root.
[Collection("Headless")]
public sealed class LibraryPageTests(HeadlessSession headless)
{
    private static string Words(Control? root) => string.Join(" | ", root is null ? [] : root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [Fact]
    [Trait("Decision", "ED-326")] // SK-15: Sections C, a page for each; neither shows the other's items
    public void Each_page_lists_only_its_own_items_one_row_each() => LibraryPageHarness.Run(headless, LibrarySection.Skills, skills =>
    {
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], skills.Page.RowViews.Keys.Order());
        Assert.Equal(["Fixture S1", "Fixture S2", "Fixture S3", "Fixture S4", "Fixture S5"], skills.Page.RowStack.Children.OfType<LibraryRowView>().Select(r => r.Title));
        Assert.DoesNotContain(skills.Page.RowViews.Keys, id => id.StartsWith('P'));
        LibraryPageHarness.Run(headless, LibrarySection.Plugins, plugins =>
        {
            Assert.Equal(["P1", "P2"], plugins.Page.RowViews.Keys.Order());
            Assert.Equal(["Fixture P1", "Fixture P2"], plugins.Page.RowStack.Children.OfType<LibraryRowView>().Select(r => r.Title));
        });
    });

    [Fact]
    [Trait("Decision", "ED-326")] // layout D: the first item is open in the detail pane with its header, its Download button and its facts
    public void The_first_item_is_open_with_its_header_its_Download_button_and_its_facts() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.Equal("S1", harness.Page.ShownDetail);
        var words = Words(harness.Page.DetailSlot);
        Assert.Contains("Fixture S1", words, StringComparison.Ordinal);
        Assert.Contains("Marrow-made summary of S1", words, StringComparison.Ordinal);
        Assert.Contains("2 skills \u00B7 3.5 MB \u00B7 example/s1 \u00B7 Apache-2.0", words, StringComparison.Ordinal);
        Assert.Contains("github.com/example/s1 at 0123456", words, StringComparison.Ordinal);
        Assert.Equal("download", harness.Page.Details["S1"].Action.Kind);
        Assert.Equal("Download\u00A0\u00B7 3.5 MB", LibraryButtons.Words(harness.Find<Button>("action-download")));
        Assert.Equal(StatusMark.Mark.Words, harness.Page.RowViews["S1"].Mark.Current);
        Assert.Equal("3.5 MB", harness.Page.RowViews["S1"].Mark.Label);
        Assert.True(harness.Page.RowViews["S1"].IsSelected);
        Assert.False(harness.Page.RowViews["S2"].IsSelected);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // a press on a row opens its detail (the pointer path of the window)
    public void Pressing_a_row_opens_its_detail() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var row = harness.Page.RowViews["S3"];
        var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), harness.Window)!.Value;
        harness.Window.MouseDown(at, MouseButton.Left);
        harness.Window.MouseUp(at, MouseButton.Left);
        harness.Settle();
        Assert.Equal("S3", harness.Controller.SelectedId);
        Assert.Equal("S3", harness.Page.ShownDetail);
        Assert.True(harness.Page.RowViews["S3"].IsSelected);
        Assert.False(harness.Page.RowViews["S1"].IsSelected);
        Assert.Contains("Marrow-made summary of S3", Words(harness.Page.DetailSlot), StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the keyboard reaches a row too
    public void Return_on_a_focused_row_opens_its_detail() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var row = harness.Page.RowViews["S2"];
        row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        harness.Settle();
        Assert.Equal("S2", harness.Page.ShownDetail);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // DL-17: the Download all confirmation sums the sizes and starts nothing on cancel
    public void Download_all_confirmation_sums_the_sizes_and_cancel_starts_nothing() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.Equal("Download all skills \u00B7 14 MB", harness.Page.DownloadAllWords);
        Assert.Null(harness.Page.Sheet);
        Assert.True(harness.Press("download-all"));
        var dialog = harness.Page.Sheet!.Dialog;
        Assert.Equal("Download all skills?", dialog.TitleShown);
        Assert.Equal("3 items from github.com.", dialog.SubShown);
        Assert.Equal("About 14 MB to download, about 14 MB on disk.", dialog.StatementShown);
        Assert.Equal("13.6 MB", dialog.DownloadShown);
        Assert.Equal("about 14 MB", dialog.OnDiskShown);
        Assert.Equal("github.com", dialog.FromShown);
        Assert.Equal("Nothing downloads until you press Continue.", dialog.FootShown);
        Assert.StartsWith("Included \u00B7 5", dialog.IncludedShown, StringComparison.Ordinal);
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], dialog.Lines.Keys.Order());
        Assert.False(dialog.LargeOpen);
        Assert.True(dialog.PlainOpen);
        Assert.NotNull(harness.TryFind("dialog-continue"));
        Assert.Empty(harness.Install.Calls);

        Assert.True(harness.Press("dialog-cancel"));
        Assert.Null(harness.Page.Sheet);
        Assert.Null(harness.TryFind("dialog"));
        Assert.Empty(harness.Install.Calls);
        Assert.Equal(RunPhase.None, harness.Controller.Dialog.Phase);
        Assert.Equal("Download all skills \u00B7 14 MB", harness.Page.DownloadAllWords);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // Escape in the confirmation is Cancel: nothing starts
    public void Escape_in_the_confirmation_cancels_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Press("download-all");
        Assert.NotNull(harness.Page.Sheet);
        harness.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        harness.Settle();
        Assert.Null(harness.Page.Sheet);
        Assert.Equal(RunPhase.None, harness.Controller.Dialog.Phase);
        Assert.Empty(harness.Install.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 review of version 3: the large-download warning only above 100 MB
    public void The_warning_opens_the_box_above_one_hundred_megabytes_and_a_plain_size_sits_at_or_below_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Press("download-all");
        Assert.True(harness.Page.Sheet!.Dialog.PlainOpen);
        Assert.False(harness.Page.Sheet.Dialog.LargeOpen);
        harness.Press("dialog-cancel");
        LibraryPageHarness.Run(headless, LibrarySection.Skills, big =>
        {
            big.Press("download-all");
            var dialog = big.Page.Sheet!.Dialog;
            Assert.True(dialog.LargeOpen);
            Assert.False(dialog.PlainOpen);
            Assert.StartsWith("This can be a large download: 1.05 GB (1,045.7 MB)", dialog.StatementShown, StringComparison.Ordinal);
        }, catalogue: LibraryFixtures.WithSize(LibraryFixtures.Catalogue(), "S2", 1040.2));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // Continue downloads, one item at a time; the dialog ends done and the rows read installed
    public void Continue_downloads_and_the_dialog_ends_done_with_the_rows_installed() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Press("download-all");
        Assert.True(harness.Press("dialog-continue"));
        harness.Wait(harness.Controller.RunCompletion);
        Assert.Equal(["S1", "S2", "S3"], harness.Install.Calls);
        var dialog = harness.Page.Sheet!.Dialog;
        Assert.Equal("Downloaded", dialog.TitleShown);
        Assert.Equal("All 3 are on this PC: about 14 MB.", dialog.SubShown);
        Assert.NotNull(harness.TryFind("dialog-close"));
        Assert.Equal(StatusMark.Mark.Check, harness.Page.RowViews["S1"].Mark.Current);
        Assert.Equal(StatusMark.Mark.Words, harness.Page.RowViews["S4"].Mark.Current);
        Assert.True(harness.Press("dialog-close"));
        Assert.Null(harness.Page.Sheet);
        Assert.Equal("Installed", harness.Words(harness.Page.DetailSlot).Single(w => w == "Installed"));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 Minimize: Hide keeps the run going and the Download all button brings the dialog back
    public void Hide_keeps_the_run_going_and_Download_all_shows_the_dialog_again() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        harness.Press("download-all");
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S1"));
        Assert.Equal("Downloading all skills", harness.Page.Sheet!.Dialog.TitleShown);
        Assert.True(harness.Press("dialog-hide"));
        Assert.Null(harness.Page.Sheet);
        Assert.Equal(RunPhase.Downloading, harness.Controller.Dialog.Phase);
        harness.Install.Report("S1", 0.5);
        harness.Settle();
        Assert.Null(harness.Page.Sheet);
        Assert.Equal(StatusMark.Mark.Ring, harness.Page.RowViews["S1"].Mark.Current);
        Assert.Equal(0.5, harness.Page.RowViews["S1"].Mark.RingValue);
        Assert.True(harness.Press("download-all"));
        Assert.NotNull(harness.Page.Sheet);
        harness.Install.Release("S1");
        harness.Wait(harness.Controller.RunCompletion);
        Assert.Equal("Downloaded", harness.Page.Sheet!.Dialog.TitleShown);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the run is the controller's: a page built again finds it
    public void A_page_built_again_finds_the_run_and_its_dialog() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S1");
        harness.Press("download-all");
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S1"));
        Assert.NotNull(harness.Page.Sheet);
        var margin = (Border)harness.Root.Children[0];
        margin.Child = null;
        harness.Settle();
        Assert.Null(harness.Page.Sheet);
        var again = new SkillsSettingsPage();
        margin.Child = again;
        harness.Settle();
        Assert.NotNull(again.Sheet);
        Assert.Equal("Downloading all skills", again.Sheet!.Dialog.TitleShown);
        harness.Install.Release("S1");
        harness.Wait(harness.Controller.RunCompletion);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 amendment: Pause, Cancel and Retry of the dialog
    public void Pause_and_Resume_swap_and_Cancel_closes_the_dialog_keeping_what_finished() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.HoldOn("S2");
        harness.Press("download-all");
        harness.Press("dialog-continue");
        harness.Wait(harness.Install.Started("S2"));
        Assert.True(harness.Press("dialog-pause"));
        harness.Until(() => harness.Page.Sheet!.Dialog.TitleShown == "Paused");
        Assert.NotNull(harness.TryFind("dialog-resume"));
        Assert.True(harness.Press("dialog-resume"));
        harness.Wait(harness.Install.Started("S2"));
        Assert.True(harness.Press("dialog-stop"));
        harness.Wait(harness.Controller.RunCompletion);
        Assert.Null(harness.Page.Sheet);
        Assert.Equal(StatusMark.Mark.Check, harness.Page.RowViews["S1"].Mark.Current);
        Assert.Equal(StatusMark.Mark.Words, harness.Page.RowViews["S2"].Mark.Current);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // a failure shows why, Retry fetches only the failed one
    public void A_failed_item_shows_its_reason_and_the_dialog_offers_Retry_for_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Install.FailOn("S2", "The file didn\u2019t match its checksum.");
        harness.Press("download-all");
        harness.Press("dialog-continue");
        harness.Wait(harness.Controller.RunCompletion);
        var dialog = harness.Page.Sheet!.Dialog;
        Assert.Equal("2 of 3 downloaded", dialog.TitleShown);
        Assert.Equal("Retry 1", LibraryButtons.Words(harness.Find<Button>("dialog-retry")));
        Assert.Equal(StatusMark.Mark.Warning, dialog.Lines["S2"].Mark.Current);
        Assert.Equal(StatusMark.Mark.Warning, harness.Page.RowViews["S2"].Mark.Current);
        harness.Press("dialog-close");
        harness.Controller.Select("S2");
        harness.Settle();
        var detail = harness.Page.Details["S2"];
        Assert.Equal("failed", detail.Action.Kind);
        Assert.True(detail.FailureOpen);
        Assert.Equal("The file didn\u2019t match its checksum.", detail.FailureShown);
        harness.Install.StopFailing("S2");
        Assert.True(harness.Press("action-retry"));
        harness.Until(() => harness.Controller.Rows.Single(r => r.Id == "S2").State == ItemState.Installed);
        Assert.Equal(["S1", "S2", "S3", "S2"], harness.Install.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // DL-15 drawn: the row label, the licence text of the pinned file, the separate read control; the Download button waits for it
    public void An_item_under_a_vendors_terms_shows_its_label_the_licence_text_and_the_read_control_before_it_can_download() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Controller.Select("S4");
        harness.Settle();
        var detail = harness.Page.Details["S4"];
        Assert.Contains("Under Anthropic's terms", Words(detail), StringComparison.Ordinal);
        Assert.Equal("Marrow-made fixture terms for S4", detail.Terms!.LicenceTextShown);
        Assert.True(detail.Terms.ReadEnabled);
        Assert.Equal("I have read these terms", AutomationProperties.GetName(detail.Terms.ReadControl));
        Assert.False(harness.Find<Button>("action-download").IsEnabled);
        Assert.False(harness.Press("action-download"));
        Assert.Empty(harness.Install.Calls);

        Assert.True(harness.Press("terms-read"));
        Assert.Equal(["accept S4"], harness.Backend.Calls);
        Assert.True(detail.Terms.Accepted);
        Assert.False(detail.Terms.ReadEnabled);
        Assert.True(harness.Find<Button>("action-download").IsEnabled);
        Assert.True(harness.Press("action-download"));
        harness.Until(() => harness.Install.Calls.Count == 1);
        Assert.Equal(["S4"], harness.Install.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the row of an open item carries no terms words
    public void An_open_item_has_no_terms_step() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.Null(harness.Page.Details["S1"].Terms);
        Assert.Null(harness.TryFind("terms-read"));
        Assert.DoesNotContain("terms", Words(harness.Page.Details["S1"]), StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099: per-skill switches inside an installed set; a switch applies to new chats
    public void An_installed_sets_skills_have_live_switches_that_write_enabled_json() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.Install("S1", LibraryFixtures.Pin);
        harness.Controller.Select("S1");
        harness.Controller.Select("S2");
        harness.Controller.Select("S1");
        harness.Settle();
        var box = harness.Page.Details["S1"].Switches;
        Assert.Equal(["S1/skills/docx", "S1/skills/xlsx"], box.Rows.Select(r => r.Key));
        Assert.Equal("0 of 2 listed", box.ListedText);
        var toggle = harness.All().OfType<SmallSwitch>().First();
        toggle.Focus();
        toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        harness.Settle();
        Assert.Equal([("S1/skills/docx", true)], harness.Backend.Writes);
        Assert.Equal("1 of 2 listed", box.ListedText);
        Assert.Equal("Listed in every session: 1 skill", harness.Page.ListingShown);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // a set that is not downloaded lists its skills with the switches waiting
    public void A_set_that_is_not_downloaded_shows_its_skills_with_dead_switches() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var box = harness.Page.Details["S1"].Switches;
        Assert.Equal("Not listed: not downloaded", box.ListedText);
        Assert.All(box.Rows, r => Assert.False(r.Row.IsEnabled));
        var toggle = harness.All().OfType<SmallSwitch>().First();
        toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        Assert.Empty(harness.Backend.Writes);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // plugins switch per plugin folder with their skill counts
    public void The_Plugins_page_switches_per_plugin_folder() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, harness =>
    {
        harness.Backend.Install("P1", LibraryFixtures.Pin);
        harness.Controller.Select("P2");
        harness.Controller.Select("P1");
        harness.Settle();
        var box = harness.Page.Details["P1"].Switches;
        Assert.Equal(["P1/sales", "P1/legal"], box.Rows.Select(r => r.Key));
        Assert.Contains("3 skills", Words((Control)box.Rows[0].Row), StringComparison.Ordinal);
        Assert.Equal("0 skills listed", box.ListedText);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // ONDEMAND-PLAN 3.6: a need the probe found on this PC says so
    public void A_need_found_on_this_PC_says_so() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var found = AddonsState.Empty.MergeFound(harness.Backend.Catalogue, ["T1"], [new FoundOnPc("T1", @"C:\Python\python.exe")]);
        harness.Backend.State = found;
        harness.Controller.Select("S2");
        harness.Controller.Select("S1");
        harness.Settle();
        var words = Words(harness.Page.Details["S1"]);
        Assert.Contains("Python", words, StringComparison.Ordinal);
        Assert.Contains("Found on this PC", words, StringComparison.Ordinal);
        Assert.Contains("Needs", words, StringComparison.Ordinal);
    }, catalogue: LibraryFixtures.WithPythonNeed(LibraryFixtures.Catalogue()));

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 / skills-PLAN 3.1: the footer carries the not-affiliated line, word for word
    public void The_footer_carries_the_not_affiliated_line() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.Contains(SkillsNotices.Footer, harness.Words(), StringComparer.Ordinal);
        LibraryPageHarness.Run(headless, LibrarySection.Plugins, plugins => Assert.Contains(SkillsNotices.Footer, plugins.Words(), StringComparer.Ordinal));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // ONDEMAND-PLAN 3.8 and section 6: Reset skills and tools, two presses
    public void Reset_takes_two_presses() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.Equal(false, harness.Page.Reset!.Asked);
        Assert.True(harness.Press("reset"));
        Assert.Equal(true, harness.Page.Reset.Asked);
        Assert.True(harness.Page.Reset.WhyOpen);
        Assert.Empty(harness.Backend.Calls);
        Assert.True(harness.Press("reset-cancel"));
        Assert.Equal(false, harness.Page.Reset.Asked);
        Assert.False(harness.Page.Reset.WhyOpen);
        harness.Press("reset");
        Assert.True(harness.Press("reset-confirm"));
        harness.Until(() => harness.Backend.Calls.Contains("reset"));
        Assert.Equal(["reset"], harness.Backend.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the trash of an installed row asks first, then removes
    public void Removing_an_installed_item_takes_two_presses_and_the_first_removes_nothing() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.Install("S1", LibraryFixtures.Pin);
        harness.Controller.Select("S2");
        harness.Controller.Select("S1");
        harness.Settle();
        Assert.Equal("installed", harness.Page.Details["S1"].Action.Kind);
        Assert.True(harness.Press("action-trash"));
        Assert.Equal("remove", harness.Page.Details["S1"].Action.Kind);
        Assert.True(harness.Page.Details["S1"].RemoveWhyOpen);
        Assert.Empty(harness.Backend.Calls);
        Assert.True(harness.Press("action-remove-cancel"));
        Assert.Equal("installed", harness.Page.Details["S1"].Action.Kind);
        harness.Press("action-trash");
        Assert.True(harness.Press("action-remove"));
        harness.Until(() => harness.Backend.Calls.Contains("remove S1"));
        Assert.Equal(["remove S1"], harness.Backend.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // ONDEMAND-PLAN 3.7 and 3.5: an update on offer and a damaged copy have their own action
    public void An_update_on_offer_and_a_damaged_copy_show_Update_and_Repair() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.UpdateAvailable, "old", "new", null), new ItemRecord("S2", ItemState.Damaged, "x", null, null)], [], [], []);
        harness.Controller.Select("S2");
        harness.Controller.Select("S1");
        harness.Settle();
        Assert.Equal("update", harness.Page.Details["S1"].Action.Kind);
        Assert.Contains("Update available old \u2192 new", Words(harness.Page.Details["S1"].Action), StringComparison.Ordinal);
        Assert.True(harness.Press("action-update"));
        harness.Until(() => harness.Backend.Calls.Contains("update S1"));
        harness.Controller.Select("S2");
        harness.Settle();
        Assert.Equal("repair", harness.Page.Details["S2"].Action.Kind);
        Assert.True(harness.Press("action-repair"));
        harness.Until(() => harness.Backend.Calls.Contains("repair S2"));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // ONDEMAND-PLAN 4.3: "Already available through your providers", read-only, on the Skills page only
    public void Already_available_through_your_providers_lists_what_a_provider_reports_on_the_Skills_page() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.False(harness.Page.Providers.IsOpen);
        harness.Backend.Reports.Add([new ProviderSkillGroup("Claude", [new ExistingSkill("docx", "Word files")]), new ProviderSkillGroup("Codex", [new ExistingSkill("imagegen", "Pictures")])]);
        harness.Controller.Select("S2");
        harness.Settle();
        Assert.True(harness.Page.Providers.IsOpen);
        Assert.Equal(["Claude", "Codex"], harness.Page.Providers.Providers);
        Assert.Equal(["docx", "imagegen"], harness.Page.Providers.SkillNames);
        Assert.Contains("Already available through your providers", harness.Words(), StringComparer.Ordinal);
        Assert.Null(harness.TryFind("provider-download"));
        LibraryPageHarness.Run(headless, LibrarySection.Plugins, plugins =>
        {
            plugins.Backend.Reports.Add([new ProviderSkillGroup("Claude", [new ExistingSkill("docx", "Word files")])]);
            plugins.Controller.Select("P2");
            plugins.Settle();
            Assert.DoesNotContain("Already available through your providers", plugins.Words(), StringComparer.Ordinal);
        });
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the install seam fails closed: the row says why, nothing is fetched
    public void A_backend_claiming_bound_still_shows_its_unbound_install_failure() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.CanFetch = true; // deliberately misreported: preserve every failure-path assertion; normal unbound controls are tested separately.
        harness.Controller.Select("S2"); harness.Controller.Select("S1"); harness.Settle();
        Assert.True(harness.Press("action-download"));
        harness.Until(() => harness.Controller.Rows[0].State == ItemState.Failed);
        Assert.Equal("failed", harness.Page.Details["S1"].Action.Kind);
        Assert.Equal("Downloading isn\u2019t available in this build yet.", harness.Page.Details["S1"].FailureShown);
        Assert.Empty(harness.Install.Calls);
    }, install: new FailClosedFirstInstall());

    [Fact]
    [Trait("Decision", "ED-326")] // a skills host that could not be opened: the pages say so and keep the footer
    public void A_page_without_a_host_says_so_and_still_has_its_footer() => headless.OnUiThread(() =>
    {
        using var seam = SkillsPageSeams.Controllers.Override(new LibraryControllers(null));
        var page = new SkillsSettingsPage();
        var window = new Avalonia.Controls.Window { Content = new Panel { Children = { page } }, Width = 568, Height = 400 };
        window.Show();
        var words = page.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Skills and tools are unavailable in this run.", words);
        Assert.Contains(SkillsNotices.Footer, words);
        Assert.Empty(page.RowViews);
        window.Close();
    });
}
