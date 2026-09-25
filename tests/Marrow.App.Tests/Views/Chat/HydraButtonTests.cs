using System;
using System.Linq;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraButtonTests(HeadlessSession headless)
{
    // PopoverItem's title renders through MiddleTruncatingText (Popovers.cs), which only computes its Text inside
    // MeasureOverride; a control that is constructed but never laid out (no window.Show()) keeps Text empty. Every
    // test that reads a PopoverItem's title needs a real layout pass first.
    private static void Show(Control control)
    {
        var window = new AvaloniaWindow { Content = control };
        window.Show();
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-005")] // App/Views/Chat/HydraButton.swift:212-216
    public void The_settings_off_switch_item_uses_the_turn_hydra_on_title()
    {
        headless.OnUiThread(() =>
        {
            var item = new PopoverItem("Turn Hydra on", () => { });
            Show(item);
            Assert.Contains("Turn Hydra on", TextOf(item));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-006")] // App/Views/Chat/HydraButton.swift:218
    public void The_chat_switch_header_reflects_its_own_state()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);

            var on = new HydraSwitchPopover(thread, true, Binding<bool>.Constant(false));
            var off = new HydraSwitchPopover(thread, false, Binding<bool>.Constant(false));
            Show(on);
            Show(off);

            Assert.Contains("Hydra is on for this chat", TextOf(on));
            Assert.Contains("Hydra is off for this chat", TextOf(off));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-007")] // App/Views/Chat/HydraButton.swift:219-222
    public void The_chat_switch_on_item_uses_its_exact_title()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraSwitchPopover(thread, true, Binding<bool>.Constant(false));
            Show(popover);

            Assert.Contains("Turn Hydra on for this chat", TextOf(popover));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-008")] // App/Views/Chat/HydraButton.swift:223-226
    public void The_chat_switch_off_item_uses_its_exact_title()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraSwitchPopover(thread, true, Binding<bool>.Constant(false));
            Show(popover);

            Assert.Contains("Turn Hydra off for this chat", TextOf(popover));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-009")] // App/Views/Chat/HydraButton.swift:305,317
    public void The_idle_popover_has_the_hydra_is_on_header_and_is_290_points_wide()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraIdlePopover(thread);

            Assert.Equal(290, popover.Width);
            Assert.Contains("Hydra is on", TextOf(popover));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-010")] // App/Views/Chat/HydraButton.swift:306-311
    public void The_no_pair_idle_popover_note_names_the_chat_model_and_effort()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraIdlePopover(thread);

            Assert.Contains("Heads run on this chat's model and effort.", TextOf(popover));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-011")] // App/Views/Chat/HydraButton.swift:312-313
    public void The_idle_popover_explains_when_heads_go_out()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraIdlePopover(thread);

            Assert.Contains(
                "No heads are out yet. Ask for something big enough to split up and this chat sends them out, each with a panel of its own.",
                TextOf(popover));
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverDivider>(popover).Count());
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-012")] // App/Views/Chat/HydraButton.swift:314-315
    public void The_bottom_idle_popover_note_points_to_right_click()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var thread = new ChatThread(Guid.Empty, ProviderKind.Codex, null, null, RuntimeMode.Supervised);
            var popover = new HydraIdlePopover(thread);

            Assert.Contains("Right-click the mark to switch Hydra off for this chat.", TextOf(popover));
        });
    }

    private static string[] TextOf(Control root) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(root)
            .Select(text => text.Text ?? "")
            .ToArray();
}
