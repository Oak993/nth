using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Testing;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.6 "First run": one card in the home, four choices each set to the safe side, the rules read-only,
// nothing outward and no mission before Done; ED-NEW-dextra-as8-8): the card writes the same "dextra" key as Settings › Assistant,
// offers at most one first duty, and Done records first run as finished.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-8")]
public sealed class FirstRunCardTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static IToggleProvider Toggle(SettingsSwitch toggle) =>
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement((Control)toggle.Child!));

    [Fact]
    public void The_card_says_hi_and_numbers_its_four_choices_then_the_rules()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var card = new FirstRunCard(new DextraSettings(new InMemorySettingsStore()));
            using var stage = new HomeStage(new ScrollViewer { Content = card }, 700, 900);

            var texts = Texts(card).ToList();
            var order = new[] { "Hi, I'm Dextra.", AssistantSettingsWords.FirstRunLead, "1  Brain", "2  What it may read", "3  Answer questions for me", "4  A first duty", "What it does alone and what it asks" }
                .Select(t => texts.IndexOf(t)).ToList();
            Assert.DoesNotContain(-1, order);
            Assert.Equal(order.OrderBy(i => i), order);
            Assert.Equal(2, AutomationProperties.GetHeadingLevel(Nodes<TextBlock>(card).First(t => t.Text == "Hi, I'm Dextra.")));
            Assert.Equal(["Does alone", "Asks first", "Always asks you to confirm", "Never"], AssistantSettingsWords.RuleGroups.Select(g => g.Heading));
            Assert.All(AssistantSettingsWords.RuleGroups.Select(g => g.Heading), h => Assert.Contains(h, texts));
            Assert.Equal("Hi, I'm Dextra.", AutomationProperties.GetName(card));
            Assert.Equal("Done, start Dextra", AutomationProperties.GetName(card.DoneButton));
        });
    }

    [Fact]
    public void Every_choice_starts_on_the_safe_side()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var card = new FirstRunCard(settings);
            using var stage = new HomeStage(new ScrollViewer { Content = card }, 700, 900);

            Assert.Equal("auto", card.Rows.ProviderMenu.SelectedId);
            Assert.Equal(ToggleState.On, Toggle(card.Rows.SameSwitch).ToggleState);
            Assert.Equal(ToggleState.Off, Toggle(card.Rows.OtherSwitch).ToggleState);
            Assert.Equal(ToggleState.Off, Toggle(card.Rows.DelegateSwitch).ToggleState);
            Assert.All(card.DutySwitches, d => Assert.Equal(ToggleState.Off, Toggle(d).ToggleState));
            Assert.False(settings.Value.FirstRunDone);
        });
    }

    [Fact]
    public void At_most_one_first_duty_is_on_and_turning_another_on_turns_the_first_off()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var card = new FirstRunCard(settings);
            using var stage = new HomeStage(new ScrollViewer { Content = card }, 700, 900);
            Assert.Equal(["Morning digest", "Watch failing builds", "Weekly chat tidy-up"], card.DutySwitches.Select(d => AutomationProperties.GetName(d)));

            var first = (SmallSwitch)card.DutySwitches[0].Child!;
            var off = first.KnobNow();
            Toggle(card.DutySwitches[0]).Toggle();
            stage.Layout();
            Assert.Equal("digest", settings.Value.FirstDuty);
            var on = first.KnobNow();
            Assert.True(on.X > off.X);
            Toggle(card.DutySwitches[1]).Toggle();
            stage.Layout();
            Assert.Equal("watch-builds", settings.Value.FirstDuty);
            Assert.Equal(ToggleState.Off, Toggle(card.DutySwitches[0]).ToggleState);
            Assert.Equal(off.X, first.KnobNow().X); // its knob moved back as well

            Toggle(card.DutySwitches[1]).Toggle();
            Assert.Null(settings.Value.FirstDuty);
        });
    }

    [Fact]
    public void Done_records_first_run_as_finished_and_hands_over()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var handed = 0;
            var card = new FirstRunCard(settings, done: () => handed++);
            using var stage = new HomeStage(new ScrollViewer { Content = card }, 700, 900);

            Click(card.DoneButton);
            Assert.True(settings.Value.FirstRunDone);
            Assert.Equal(1, handed);
        });
    }
}
