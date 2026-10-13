using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.6 the four choices, 5.4 the reading switches, 4.3 "Answer questions for me", 12.2 Permissions,
// 12.6 the stop chord, 16 "Settings › Assistant in its own page"; ED-NEW-dextra-as8-8): the page writes every choice to the one
// "dextra" key, shows the brain's account and model once a provider is picked, lists the kept grants with Revoke, and its switches
// redraw when another view changes a choice.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-8")]
public sealed class AssistantSettingsPageTests(HeadlessSession headless)
{
    private static readonly Guid Work = Guid.NewGuid(), Home = Guid.NewGuid();

    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static void Flip(SettingsSwitch toggle) =>
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement((Control)toggle.Child!)).Toggle();

    private static bool IsOn(SettingsSwitch toggle) =>
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement((Control)toggle.Child!)).ToggleState == ToggleState.On;

    private static BrainChoices Brain() => new(
        () => [ProviderKind.Claude, ProviderKind.Codex],
        provider => provider == ProviderKind.Claude ? [(Work, "Work"), (Home, "Home")] : [],
        provider => provider == ProviderKind.Claude ? [("opus", "Opus"), ("sonnet", "Sonnet")] : []);

    private static Grant Login(GrantScope scope) =>
        new(Guid.NewGuid(), "dextra", ActionClass.Login, GrantTarget.Site("https://shop.example"), scope) { Created = Now.AddMinutes(-5) };

    [Fact]
    public void The_page_holds_its_sections_in_order_and_is_a_listed_ready_page()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var page = new AssistantSettingsPage(new DextraSettings(new InMemorySettingsStore()));
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            var texts = Texts(page).ToList();
            var order = new[] { AssistantSettingsWords.Lead, "Dextra", "Brain", "What it may read", "Answer questions for me", "Stop chord", "Permissions", "What it does alone and what it asks" }
                .Select(t => texts.IndexOf(t)).ToList();
            Assert.DoesNotContain(-1, order);
            Assert.Equal(order.OrderBy(i => i), order);
            Assert.True(AssistantSettingsPage.IsReady);
            Assert.Equal("Assistant", SettingsPage.Assistant.Title);
            Assert.Contains("dextra", SettingsPage.Assistant.Keywords);
            Assert.Contains("Never", texts); // the rules' four groups, read-only
        });
    }

    [Fact]
    public void Each_switch_writes_its_choice_and_redraws_when_another_view_changes_it()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings);
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            Assert.True(IsOn(page.OnSwitch));
            Flip(page.OnSwitch);
            Assert.False(settings.Value.On);
            Flip(page.Rows.SameSwitch);
            Assert.False(settings.Value.ReadSameProvider);
            Flip(page.Rows.OtherSwitch);
            Assert.True(settings.Value.ReadOtherProviders);

            Assert.False(page.Rows.ScopeRow.IsVisible);
            Flip(page.Rows.DelegateSwitch);
            stage.Layout();
            Assert.Equal(DelegateScope.ThisProject, settings.Value.Delegate);
            Assert.True(page.Rows.ScopeRow.IsVisible);
            Assert.Equal(AssistantSettingsWords.Delegate, AutomationProperties.GetName(page.Rows.ScopePicker));

            // changed elsewhere (the first-run card): the switch's knob moves back
            var other = (SmallSwitch)page.Rows.OtherSwitch.Child!;
            var knob = other.KnobNow();
            settings.Update(p => p with { ReadOtherProviders = false });
            stage.Layout();
            Assert.False(IsOn(page.Rows.OtherSwitch));
            Assert.True(other.KnobNow().X < knob.X);
        });
    }

    [Fact]
    public void Picking_a_provider_offers_its_accounts_and_models_and_Automatic_clears_them()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings, new AssistantSettingsParts { Brain = Brain() });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);
            var rows = page.Rows;

            Assert.Equal(["auto", "Claude", "Codex"], rows.ProviderMenu.Items.Select(i => i.Id));
            Assert.Equal("auto", rows.ProviderMenu.SelectedId);
            Assert.False(rows.AccountRow.IsVisible);
            Assert.False(rows.ModelRow.IsVisible);

            rows.ProviderMenu.Select("Claude");
            stage.Layout();
            Assert.Equal(ProviderKind.Claude, settings.Value.BrainProvider);
            Assert.True(rows.AccountRow.IsVisible);
            Assert.True(rows.ModelRow.IsVisible);
            Assert.Equal(["first", Work.ToString("D"), Home.ToString("D")], rows.AccountMenu.Items.Select(i => i.Id));
            Assert.Equal(["default", "opus", "sonnet"], rows.ModelMenu.Items.Select(i => i.Id));
            rows.AccountMenu.Select(Home.ToString("D"));
            rows.ModelMenu.Select("opus");
            Assert.Equal(Home, settings.Value.BrainAccount);
            Assert.Equal("opus", settings.Value.BrainModel);

            rows.ProviderMenu.Select("Codex");
            stage.Layout();
            Assert.Null(settings.Value.BrainAccount); // a new provider starts from its first account and default model
            Assert.Null(settings.Value.BrainModel);
            Assert.False(rows.AccountRow.IsVisible); // one account: nothing to pick

            rows.ProviderMenu.Select("auto");
            stage.Layout();
            Assert.Null(settings.Value.BrainProvider);
            Assert.False(rows.ModelRow.IsVisible);
        });
    }

    [Fact]
    public void The_stop_chord_offers_the_four_chords_and_the_default_is_stored_as_none()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings);
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            Assert.Equal(AssistantSettingsWords.Chords, page.ChordMenu.Items.Select(i => i.Id));
            Assert.Equal("Ctrl+Alt+Esc", page.ChordMenu.SelectedId);
            page.ChordMenu.Select("Ctrl+Alt+Pause");
            Assert.Equal("Ctrl+Alt+Pause", settings.Value.StopChord);
            Assert.Equal("Ctrl+Alt+Pause", settings.Chord);
            page.ChordMenu.Select("Ctrl+Alt+Esc");
            Assert.Null(settings.Value.StopChord);
        });
    }

    [Fact]
    public void Permissions_lists_the_live_grants_and_Revoke_takes_one_away()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var mission = Guid.NewGuid();
            var standing = Login(GrantScope.Standing);
            var forMission = Login(GrantScope.ForMission(mission));
            var revoked = Login(GrantScope.Standing).Revoked(Now.AddMinutes(-1));
            var ended = Login(GrantScope.UntilDate(Now.AddMinutes(-1)));
            var grants = new List<Grant> { standing, forMission, revoked, ended };
            var asked = new List<Guid>();
            var page = new AssistantSettingsPage(new DextraSettings(new InMemorySettingsStore()), new AssistantSettingsParts
            {
                Grants = () => [.. grants],
                Revoke = id => { asked.Add(id); return grants.RemoveAll(g => g.Id == id) > 0; },
                MissionTitle = id => id == mission ? "Espresso machines under $800" : null,
            });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            Assert.Null(page.NoGrantsText);
            Assert.Equal([standing.Id, forMission.Id], page.GrantRows.Select(r => r.Grant.Id));
            Assert.Equal("Revoke Sign in · https://shop.example", AutomationProperties.GetName(page.GrantRows[0].Revoke));
            var texts = Texts(page);
            Assert.Contains("Until you revoke it", texts);
            Assert.Contains("This mission: Espresso machines under $800", texts);

            Click(page.GrantRows[0].Revoke);
            Click(page.GrantRows[0].Revoke);
            Assert.Equal([standing.Id, forMission.Id], asked);
            Assert.Empty(page.GrantRows);
            Assert.Equal(AssistantSettingsWords.NoGrants, page.NoGrantsText!.Text);
        });
    }
}
