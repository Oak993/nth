using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Advisor;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Port.Advisor;

// PORT-ONLY: ED-NEW-ignisadvisor-1 (owner 2026-10-11). The Claude advisor as a model-picker entry: where it goes, what its row
// says, and the too-old note with its Update action, which opens Settings › Updates (where nth updates Claude Code). The list is
// the real one over a real AppModel whose CLIs cannot resolve; Claude Code's version is the one the provider check would read.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-ignisadvisor-1")]
public sealed class AdvisorPickerTests(HeadlessSession headless)
{
    private const string EntryID = "claude-sonnet-5-5+advisor-opus";
    private const string EntryName = "Sonnet 5.5 + Opus advisor";
    private const string Subtitle = "Opus steps in only when Sonnet is stuck · about half the cost";

    private static readonly ModelOption[] Claude = [Option("opus", "Opus"), Option("sonnet", "Sonnet"), Option("haiku", "Haiku", efforts: false)];

    private static EffortListEnv Env(string? claudeVersion, Func<ChatThread>? thread = null, bool installed = true) =>
        Make(thread ?? (() => Thread(ProviderKind.Claude, "sonnet")), model =>
        {
            foreach (var (kind, id) in new[] { (ProviderKind.Claude, "opus"), (ProviderKind.Claude, "sonnet"), (ProviderKind.Claude, "haiku"), (ProviderKind.Codex, "c1") })
                model.Settings.AddToModelList(Pin(kind, id));
            typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map =>
                map[ProviderKind.Claude] = installed
                    ? new ProviderStatus { Executable = SwiftURL.Create("/offline/claude")!, Version = claudeVersion, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }
                    : new ProviderStatus()));
        }, (ProviderKind.Claude, Claude), (ProviderKind.Codex, [Option("c1", "C1")]));

    // The rows as drawn, light and dark, when MARROW_ADVISOR_SHOTS_DIR names a folder (the headless renderer's frame, as the
    // other evidence captures do); otherwise nothing.
    private static void Shot(Avalonia.Controls.Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("MARROW_ADVISOR_SHOTS_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            window.Background = new Avalonia.Media.SolidColorBrush(theme == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Media.Color.FromRgb(0x24, 0x24, 0x26) : Avalonia.Media.Colors.White);
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(folder, name + "-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        window.RequestedThemeVariant = null; window.Background = null;
        Dispatcher.UIThread.RunJobs();
    }

    // The advisor row's words under its name (its detail's runs, the Update action left out).
    private static string? Words(Control row)
    {
        var detail = row.GetVisualDescendants().OfType<TextBlock>().Skip(1).First();
        return detail.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)).TrimEnd() : detail.Text;
    }

    // Neither the name nor the words under it is cut short.
    private static void AssertWhole(Control row)
    {
        foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Take(2))
            Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
    }

    // The list shows every row without scrolling (the rows' heights add up to the list's).
    private static void AssertNoScroll(Control list)
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, $"extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}");
    }

    private static Button? UpdateButton(Control row) =>
        row.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => AutomationProperties.GetName(button) == "Update Claude Code");

    [Fact]
    public void The_words_are_the_owners_and_the_note_shows_only_when_claude_code_cannot_use_the_advisor()
    {
        Assert.Equal("Your Claude Code is too old for this. Update it to use it.", ClaudeAdvisorModels.TooOldNote);
        Assert.Equal("Update", ClaudeAdvisorModels.UpdateAction);
        var entry = Assert.Single(ClaudeAdvisorModels.All);
        Assert.Equal((EntryID, EntryName, Subtitle, "claude-sonnet-5-5", "opus"), (entry.Id, entry.Name, entry.Detail, entry.MainModel, entry.Advisor));

        foreach (var ready in new[] { "2.1.284", "2.1.284 (Claude Code)", "2.1.296 (Claude Code)", "2.2.0", "3.0.0" })
            Assert.Null(AdvisorPicker.Note(ready));
        foreach (var old in new[] { "2.1.283", "2.1.283 (Claude Code)", "2.0.999", "1.0.0" })
            Assert.Equal(ClaudeAdvisorModels.TooOldNote, AdvisorPicker.Note(old));
        foreach (var unknown in new string?[] { null, "", "Claude Code" })
            Assert.Equal(ClaudeAdvisorModels.VersionUnknownNote, AdvisorPicker.Note(unknown));
    }

    [Fact]
    public void The_entry_goes_right_after_sonnet_else_after_the_last_claude_model_and_never_without_claude() => headless.OnUiThread(() =>
    {
        using var env = Env("2.1.296");
        var registry = env.Model.Providers;
        ModelCatalog.Entry E(ProviderKind kind, string id) => new(kind, new ModelOption(id, id, null, ValueList<string>.Empty, null));

        var list = new List<ModelCatalog.Entry> { E(ProviderKind.Codex, "c1"), E(ProviderKind.Claude, "opus"), E(ProviderKind.Claude, "sonnet"), E(ProviderKind.Claude, "haiku") };
        AdvisorPicker.AddEntries(registry, list);
        Assert.Equal(["c1", "opus", "sonnet", EntryID, "haiku"], list.Select(entry => entry.Option.Id));
        Assert.Equal(ProviderKind.Claude, list[3].Provider);
        Assert.Equal((EntryName, Subtitle), (list[3].Option.Name, list[3].Option.Detail));
        Assert.Equal(Claude[1].Efforts, list[3].Option.Efforts); // Sonnet's efforts
        AdvisorPicker.AddEntries(registry, list); // once only
        Assert.Single(list, entry => entry.Option.Id == EntryID);

        var fullId = new List<ModelCatalog.Entry> { E(ProviderKind.Claude, "claude-sonnet-5-5"), E(ProviderKind.Claude, "opus") };
        AdvisorPicker.AddEntries(registry, fullId);
        Assert.Equal(["claude-sonnet-5-5", EntryID, "opus"], fullId.Select(entry => entry.Option.Id));

        var noSonnet = new List<ModelCatalog.Entry> { E(ProviderKind.Claude, "opus"), E(ProviderKind.Codex, "c1"), E(ProviderKind.Claude, "haiku"), E(ProviderKind.Codex, "c2") };
        AdvisorPicker.AddEntries(registry, noSonnet);
        Assert.Equal(["opus", "c1", "haiku", EntryID, "c2"], noSonnet.Select(entry => entry.Option.Id));

        var noClaude = new List<ModelCatalog.Entry> { E(ProviderKind.Codex, "c1"), E(ProviderKind.Codex, "sonnet") };
        AdvisorPicker.AddEntries(registry, noClaude);
        Assert.Equal(["c1", "sonnet"], noClaude.Select(entry => entry.Option.Id));

        // The registry knows the entry as a Claude model (a chat that chose it keeps resolving); for no other provider.
        Assert.Equal(EntryName, registry.Model(EntryID, ProviderKind.Claude)?.Name);
        Assert.NotEqual(EntryName, registry.Model(EntryID, ProviderKind.Codex)?.Name);
    });

    [Fact]
    public void Moving_onto_or_off_the_entry_changes_the_launch_and_no_other_choice_does()
    {
        Assert.True(AdvisorPicker.ChangesLaunch("sonnet", EntryID));
        Assert.True(AdvisorPicker.ChangesLaunch(EntryID, "opus"));
        Assert.True(AdvisorPicker.ChangesLaunch(null, EntryID));
        Assert.False(AdvisorPicker.ChangesLaunch(EntryID, EntryID));
        Assert.False(AdvisorPicker.ChangesLaunch("sonnet", "opus"));
        Assert.False(AdvisorPicker.ChangesLaunch(null, "opus"));
    }

    [Fact]
    public void A_new_enough_claude_code_shows_the_subtitle_and_no_update_action() => headless.OnUiThread(() =>
    {
        using var env = Env("2.1.296 (Claude Code)");
        var list = new ModelList(env.Thread, false, _ => { }, () => { }) { Width = 330 }; // the model panel's width
        Shot(env.Show(list), "ignis-advisor-picker-ready");
        Assert.Equal(["Opus", "Sonnet", EntryName, "Haiku", "C1"], Models(list).Select(Name));
        var row = ModelRow(list, EntryName);
        Assert.Equal(Subtitle, Words(row));
        AssertWhole(row);
        AssertNoScroll(list);
        Assert.Equal(46 + ModelListRow.ExtraLine, row.Bounds.Height, 0.5); // two lines of words; every other row keeps 46
        Assert.All(Models(list).Where(other => other != row), other => Assert.Equal(46, other.Bounds.Height, 0.5));
        Assert.Null(UpdateButton(row));
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == ClaudeAdvisorModels.TooOldNote);
    });

    [Fact]
    public void A_too_old_claude_code_shows_the_note_and_the_update_action_on_the_entry_only_and_choosing_still_works() => headless.OnUiThread(() =>
    {
        using var env = Env("2.1.283 (Claude Code)");
        var chosen = new List<string>();
        var list = new ModelList(env.Thread, false, entry => chosen.Add(entry.Option.Id), () => { }) { Width = 330 }; // the model panel's width
        var window = env.Show(list);
        Shot(window, "ignis-advisor-picker-too-old");
        var row = ModelRow(list, EntryName);
        Assert.Equal(ClaudeAdvisorModels.TooOldNote, Words(row));
        AssertWhole(row);
        AssertNoScroll(list);
        var update = Assert.IsType<Button>(UpdateButton(row));
        Assert.Equal("Update", Assert.IsType<TextBlock>(update.Content).Text);
        Assert.True(update.IsEffectivelyVisible);
        foreach (var other in Models(list).Where(other => Name(other) != EntryName))
        {
            Assert.Null(UpdateButton(other));
            Assert.NotEqual(ClaudeAdvisorModels.TooOldNote, Detail(other));
        }

        // The entry stays pickable: the chat keeps it and runs on Sonnet without the flag until Claude Code is updated.
        var rowButton = row.GetVisualDescendants().OfType<Button>().First();
        Assert.True(rowButton.IsEnabled);
        rowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Assert.Equal([EntryID], chosen);

        // Update opens Settings on the Updates page and does not choose the model as well.
        var opened = new List<SettingsPage>();
        var previous = AdvisorPicker.OpenSettings;
        AdvisorPicker.OpenSettings = opened.Add;
        try { Click(window, update); }
        finally { AdvisorPicker.OpenSettings = previous; }
        Assert.Equal([SettingsPage.Updates], opened);
        Assert.Equal([EntryID], chosen);
    });

    [Fact]
    public void An_unknown_version_explains_itself_and_offers_the_same_update_action() => headless.OnUiThread(() =>
    {
        using var env = Env(null);
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        env.Show(list);
        var row = ModelRow(list, EntryName);
        Assert.Equal(ClaudeAdvisorModels.VersionUnknownNote, Words(row));
        AssertWhole(row);
        Assert.NotNull(UpdateButton(row));
    });

    [Fact]
    public void Without_claude_code_found_the_picker_offers_no_advisor_entry() => headless.OnUiThread(() =>
    {
        using var env = Env(null, installed: false);
        Assert.False(AdvisorPicker.HasClaudeCode(env.Model.Providers));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        env.Show(list);
        Assert.Equal(["Opus", "Sonnet", "Haiku", "C1"], Models(list).Select(Name));
        var entries = new List<ModelCatalog.Entry> { new(ProviderKind.Claude, Claude[1]) };
        AdvisorPicker.AddEntries(env.Model.Providers, entries);
        Assert.Single(entries);
    });

    [Fact]
    public void A_locked_entry_says_new_chats_only_and_offers_no_update() => headless.OnUiThread(() =>
    {
        using var env = Env("2.1.283", () => Thread(ProviderKind.Codex, "c1"));
        var list = new ModelList(env.Thread, true, _ => { }, () => { });
        env.Show(list);
        var row = ModelRow(list, EntryName);
        Assert.Equal("New chats only", Words(row));
        Assert.Null(UpdateButton(row));
    });

    [Fact]
    public void The_update_action_targets_the_page_where_nth_updates_claude_code() => headless.OnUiThread(() =>
    {
        Assert.Equal(SettingsPage.Updates, AdvisorPicker.UpdatePage);
        using var env = Env("2.1.283"); // the app's settings store, which the accent colour reads
        var opened = new List<SettingsPage>();
        var previous = AdvisorPicker.OpenSettings;
        AdvisorPicker.OpenSettings = opened.Add;
        try
        {
            var link = AdvisorPicker.UpdateLink();
            Assert.Equal("Update Claude Code", AutomationProperties.GetName(link));
            Assert.Equal("Update Claude Code", ToolTip.GetTip(link));
            var click = new RoutedEventArgs(Button.ClickEvent);
            link.RaiseEvent(click);
            Assert.True(click.Handled); // the row underneath never sees it
        }
        finally { AdvisorPicker.OpenSettings = previous; }
        Assert.Equal([SettingsPage.Updates], opened);
    });
}
