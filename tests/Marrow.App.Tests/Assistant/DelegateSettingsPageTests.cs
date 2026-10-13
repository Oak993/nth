using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "Choose a delegate: the Assistant itself or any head", 2.6 "Answer questions for me ... off, with This project only and Everywhere";
// ED-NEW-dextra-as22-9): the Answers with menu beside the scope. Dextra's own brain, or a provider whose one-shot model can read a question (Claude, Codex) that is on and
// installed; it shows once the delegate is on, and its detail line says truthfully what is still the owner's.
[Collection("Headless")]
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-9")]
public sealed class DelegateSettingsPageTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static void Flip(SettingsSwitch toggle) =>
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement((Control)toggle.Child!)).Toggle();

    private static BrainChoices Brain(params ProviderKind[] providers) => new(() => providers, _ => [], _ => []);

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-open-7")]
    public void The_menu_shows_once_the_delegate_is_on_offers_dextra_and_the_providers_that_can_read_a_question_and_writes_the_choice()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings, new AssistantSettingsParts { Brain = Brain(ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Cursor) });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);
            var rows = page.Rows;

            Assert.False(rows.WithRow.IsVisible);                                                       // off by default: nothing more to choose
            Flip(rows.DelegateSwitch);
            stage.Layout();
            Assert.True(rows.WithRow.IsVisible);
            // DESIGN.md 4.3 and CU-DX-OPEN item 1 supersede AS-22's provider-only menu (ED-NEW-dextra-open-1).
            Assert.Equal(["dextra", "Claude", "Codex", "Cursor"], rows.WithMenu.Items.Select(i => i.Id));
            Assert.Equal("dextra", rows.WithMenu.SelectedId);
            Assert.Equal("Dextra", rows.WithMenu.Items.First().Label.Text);

            rows.WithMenu.Select("Codex");
            // CU-DX-OPEN2 item 1 requires unavailable providers to be visible with a reason, rather than selectable.
            Assert.Null(settings.Value.DelegateWith);
            Assert.False(rows.WithMenu.Items.Single(i => i.Id == "Codex").IsEnabled);
            Assert.Contains("unavailable", rows.WithMenu.Items.Single(i => i.Id == "Codex").Label.Text!, StringComparison.OrdinalIgnoreCase);
            rows.WithMenu.Select("Claude");
            Assert.Equal(new DelegateWho(ProviderKind.Claude), settings.Value.DelegateWith);
            rows.WithMenu.Select("dextra");
            Assert.Null(settings.Value.DelegateWith);
        });
    }

    [Fact]
    public void A_provider_picked_earlier_that_is_no_longer_offered_stays_in_the_list_and_a_chat_that_turned_it_on_shows_the_row()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            settings.Update(p => p with { DelegateWith = new DelegateWho(ProviderKind.Codex), DelegateChats = new ValueList<DelegateChatChoice>([new(Guid.NewGuid(), true)]) });
            var page = new AssistantSettingsPage(settings, new AssistantSettingsParts { Brain = Brain(ProviderKind.Claude) });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);
            Assert.True(page.Rows.WithRow.IsVisible);                                                   // the scope is off, one chat has it on
            Assert.Equal(["dextra", "Claude", "Codex"], page.Rows.WithMenu.Items.Select(i => i.Id));
            Assert.Equal("Codex", page.Rows.WithMenu.SelectedId);
        });
    }

    [Fact]
    public void The_switchs_detail_line_says_what_still_comes_to_the_owner_and_not_that_a_check_is_pending()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var page = new AssistantSettingsPage(new DextraSettings(new InMemorySettingsStore()));
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);
            var texts = Texts(page);
            Assert.Contains(AssistantSettingsWords.DelegateDetail, texts);
            Assert.DoesNotContain(texts, t => t.Contains("safety suite", StringComparison.Ordinal));
            foreach (var word in new[] { "Money", "passwords", "sent out", "deleting", "installing", "promises", "design picks" })
                Assert.Contains(word, AssistantSettingsWords.DelegateDetail, StringComparison.Ordinal);
            Assert.Equal("Answers with", AutomationProperties.GetName(page.Rows.WithMenu));
        });
    }
}
