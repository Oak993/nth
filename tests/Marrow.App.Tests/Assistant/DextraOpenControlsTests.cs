using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Marrow.App.Assistant;
using Marrow.Testing;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-open-3")]
[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class DextraOpenControlsTests(HeadlessSession headless)
{
    [Fact]
    public void Turning_on_a_suggestion_kind_keeps_keyboard_focus_on_its_status()
    {
        headless.OnUiThread(() =>
        {
            using var rig = DigestRigs.Quiet();
            rig.Host.Digests!.Book.Update(l => l with { Off = new ValueList<SuggestionKind>([SuggestionKind.FromBrain]) });
            var page = new AssistantSettingsPage(new DextraSettings(new InMemorySettingsStore()), new AssistantSettingsParts { Digests = () => rig.Host.Digests });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 1000);
            var turnOn = Nodes<Button>(page).Single(b => AutomationProperties.GetName(b) == "Turn on ideas from dextra suggestions");
            Assert.True(turnOn.Focus());
            turnOn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(rig.Host.Digests.IsOff(SuggestionKind.FromBrain));
            var row = Nodes<Control>(page).Single(c => AutomationProperties.GetName(c) == "Ideas from Dextra: suggestions on");
            Assert.True(row.IsFocused);
        });
    }

    [Fact]
    public void A_kept_duty_edit_keeps_the_owners_existing_budget_and_report_choice()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DutyHostRig(tray: false);
            var profile = new AssistantProfile("dextra", "Dextra") { Duties = new ValueList<DutyDraft>([new("Watch numbers")]) };
            var built = AsturDutyDrafts.Build(profile, "Watch numbers", "", rig.Clock.GetUtcNow(), TimeZoneInfo.Utc).Duty!;
            Assert.NotNull(rig.Host.Save(built with { BudgetUsd = 3m, Report = ReportStyle.Always }).Duty);
            var sheet = new AsturDutiesSheet(() => profile, rig.Host);
            using var stage = new HomeStage(sheet, 480);
            Nodes<DetailButton>(sheet).Single(b => b.Title == "Review duty").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Nodes<DetailButton>(sheet).Single(b => b.Title == "Save duty changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = rig.Host.Scheduler.Find(built.Id)!;
            Assert.Equal(3m, saved.BudgetUsd);
            Assert.Equal(ReportStyle.Always, saved.Report);
        });
    }

    [Fact]
    public void Only_done_records_through_the_digest_are_removed_from_the_board()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            var old = Job("Old completed", MissionState.Done) with { Ended = Now.AddMinutes(-5) };
            var later = Job("Later completed العربية", MissionState.Done) with { Ended = Now.AddMinutes(-1) };
            var live = Job("Still working", MissionState.Working);
            jobs.Set(old, later, live);
            DateTimeOffset? digest = null;
            Action? digestChanged = null;
            var home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls())
            {
                DigestedThrough = () => digest,
                SuggestionsChanged = changed =>
                {
                    Assert.Null(digestChanged);
                    digestChanged = changed;
                    return new CallbackDisposable(() => digestChanged = null);
                },
            });
            var board = home.Board.Missions;
            using var stage = new HomeStage(home, 500);
            Assert.NotNull(board.Card(old.Id));
            digest = Now.AddMinutes(-3);
            Assert.NotNull(digestChanged);
            digestChanged();
            Assert.Null(board.Card(old.Id));
            Assert.NotNull(board.Card(later.Id));
            Assert.NotNull(board.Card(live.Id));
        });
    }

    [Fact]
    public void A_long_RTL_kept_duty_requires_review_then_explicit_creation()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DutyHostRig(tray: false);
            var profile = new AssistantProfile("dextra", "متجر · Shop") { Duties = new ValueList<DutyDraft>([new("Watch the weekly numbers and prepare the report العربية")]) };
            var sheet = new AsturDutiesSheet(() => profile, rig.Host);
            using var stage = new HomeStage(sheet, 480);
            Assert.Empty(rig.Host.Scheduler.Duties);
            var buttons = Nodes<DetailButton>(sheet).ToArray();
            Assert.False(buttons.Single(b => b.Title == "Create duty").IsVisible);
            buttons.Single(b => b.Title == "Review duty").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(rig.Host.Scheduler.Duties);
            var confirm = buttons.Single(b => b.Title == "Create duty");
            Assert.True(confirm.IsVisible);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<ManualTrigger>(Assert.Single(rig.Host.Scheduler.Duties).Duty.Trigger);
        });
    }
}
