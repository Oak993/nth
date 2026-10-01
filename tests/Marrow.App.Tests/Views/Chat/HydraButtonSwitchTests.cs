using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The per-chat switch a right-click on the Hydra mark opens (App/Views/Chat/HydraButton.swift:200-230): one of two shapes,
/// each item closing the popover, and the Settings-off branch writing twice.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonSwitchTests(HeadlessSession headless)
{
    private void Run(Action<HydraButtonRig> body, bool hydraOn = true, bool hydraEnabledInSettings = true) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new HydraButtonRig(hydraOn, introRead: true, hydraEnabledInSettings);
            body(rig);
        });

    private static HydraSwitchPopover RightClick(HydraButtonRig rig)
    {
        var at = rig.MarkCentre;
        rig.Main.OnMain(() =>
        {
            rig.Window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
            rig.Window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        });
        rig.Drain();
        return rig.Open<HydraSwitchPopover>();
    }

    private static string[] Texts(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(text => text.Text ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-HYDRA-163")] // HydraButton.swift:210-216 Hydra off app-wide: one header and one item
    [Trait("Req", "UI-HYDRA-004")] // HydraButton.swift:211
    public void With_hydra_off_in_settings_the_switch_has_only_the_settings_header_and_one_item()
    {
        Run(rig =>
        {
            var popover = RightClick(rig);

            Assert.Contains("Hydra is off in Settings", Texts(popover));
            Assert.Single(LogicalQuery.SelfAndDescendants<PopoverSectionHeader>(popover));
            var item = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverItem>(popover));
            Assert.Contains("Turn Hydra on", Texts(item));
            Assert.DoesNotContain(Texts(popover), text => text.Contains("for this chat", StringComparison.Ordinal));
        }, hydraOn: false, hydraEnabledInSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-163")] // HydraButton.swift:212-216 the settings-off item writes the app-wide switch AND turns the chat's on
    public void Turning_hydra_on_from_the_settings_off_shape_writes_both_switches_re_pins_the_pair_and_closes()
    {
        Run(rig =>
        {
            var pair = new HydraPair(ProviderKind.Codex) { OrchestratorEffort = "high" };
            rig.Main.OnMain(() => rig.Model.Settings.AddHydraPair(pair));
            var popover = RightClick(rig);

            rig.Choose(HydraButtonRig.Item(popover, "Turn Hydra on"));

            Assert.True(rig.Model.Settings.HydraEnabled); // :213
            var thread = rig.Model.Thread(rig.Lead.Id)!;
            Assert.True(thread.HydraEnabled); // :214 setHydra(true, for:)
            Assert.Equal(pair.Id, thread.HydraPairID); // which re-pins a pair
            Assert.Equal("high", thread.Effort); // and may change the chat's effort
            Assert.True(rig.Model.Settings.HydraDefaultEnabled);
            Assert.Null(rig.PopupShowing<HydraSwitchPopover>()); // :215 isHydraSwitchShown = false
        }, hydraOn: false, hydraEnabledInSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-163")] // HydraButton.swift:218-226 with Hydra on: header by the chat's state, two checkable items
    public void With_hydra_on_the_switch_names_the_chats_state_and_checks_the_matching_item()
    {
        Run(rig =>
        {
            var popover = RightClick(rig);
            Assert.Contains("Hydra is on for this chat", Texts(popover));
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverItem>(popover).Count());
            Assert.True(HydraButtonRig.IsChecked(HydraButtonRig.Item(popover, "Turn Hydra on for this chat"))); // :219 isChecked: isOn
            Assert.False(HydraButtonRig.IsChecked(HydraButtonRig.Item(popover, "Turn Hydra off for this chat"))); // :223 isChecked: !isOn
        });

        Run(rig =>
        {
            var popover = RightClick(rig);
            Assert.Contains("Hydra is off for this chat", Texts(popover));
            Assert.False(HydraButtonRig.IsChecked(HydraButtonRig.Item(popover, "Turn Hydra on for this chat")));
            Assert.True(HydraButtonRig.IsChecked(HydraButtonRig.Item(popover, "Turn Hydra off for this chat")));
        }, hydraOn: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-163")] // HydraButton.swift:223-226 turning it off for this chat
    public void Turning_hydra_off_for_the_chat_writes_it_and_closes_the_popover()
    {
        Run(rig =>
        {
            var popover = RightClick(rig);

            rig.Choose(HydraButtonRig.Item(popover, "Turn Hydra off for this chat"));

            Assert.False(rig.Model.Thread(rig.Lead.Id)!.HydraEnabled); // :224 setHydra(false, for:)
            Assert.False(rig.Model.Settings.HydraDefaultEnabled);
            Assert.True(rig.Model.Settings.HydraEnabled); // the app-wide switch is left alone
            Assert.Null(rig.PopupShowing<HydraSwitchPopover>()); // :225
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-163")] // HydraButton.swift:219-222 turning it on for this chat
    public void Turning_hydra_on_for_the_chat_writes_it_and_closes_the_popover()
    {
        Run(rig =>
        {
            var popover = RightClick(rig);

            rig.Choose(HydraButtonRig.Item(popover, "Turn Hydra on for this chat"));

            Assert.True(rig.Model.Thread(rig.Lead.Id)!.HydraEnabled); // :220 setHydra(true, for:)
            Assert.True(rig.Model.Settings.HydraDefaultEnabled);
            Assert.Null(rig.PopupShowing<HydraSwitchPopover>()); // :221
        }, hydraOn: false);
    }
}
