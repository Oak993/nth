using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The strip's right-hand buttons (App/Views/Chat/HydraPanel.swift:176-199): in the team panel a pop-out button for the head
/// on stage (only while another head stays behind) and always a dismiss button; in a popped-out panel one put-back button.
/// The head names are the roster's (Core/Models/Hydra.swift:231-232: index 0 is Hank, index 1 is Walter).
/// </summary>
[Collection("Headless")]
public sealed class HydraPanelStripButtonsTests(HeadlessSession headless)
{
    private const string Dismiss = "Dismiss the panel; heads keep working";

    private void Run(Action<HydraPanelContext> test, Action<Guid>? popOut = null, bool withoutPopOut = false, Action? dismiss = null, bool poppedOut = false) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var context = new HydraPanelContext(visible: true, popOut: popOut, withoutPopOut: withoutPopOut, dismiss: dismiss, poppedOut: poppedOut);
            context.Tick(1);
            test(context);
        });

    private static void Press(Control circleButton)
    {
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(circleButton));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static string[] Symbols(Control circleButton) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(circleButton).Select(i => i.SymbolName).OfType<string>().ToArray();

    [Fact]
    [Trait("Req", "PR-HYDRA-172")] // HydraPanel.swift:184-189 ChromeCircleButton(symbol: "arrow.up.right", help: "Pop \(name) out into its own panel") { popOut(selected.id) }
    [Trait("Req", "UI-HYDRA-021")] // HydraPanel.swift:184-189 the head on stage is the one popped out, named in the tooltip
    public void The_pop_out_button_names_the_head_on_stage_and_pops_that_head_out()
    {
        var popped = new List<Guid>();
        Run(c =>
        {
            var button = Assert.IsType<ChromeCircleButton>(Assert.Single(c.PopOutButtons()));
            Assert.Contains(c.Tips(), t => t == "Pop Hank out into its own panel");
            Assert.Contains("arrow.up.right", Symbols(button));
            Press(button);
            Assert.Equal([c.First.Id], popped);

            // The other head takes the stage: the button now names it and pops it out.
            c.Select(c.Second);
            c.Tick(1);
            var next = Assert.Single(c.PopOutButtons());
            Assert.DoesNotContain(c.Tips(), t => t == "Pop Hank out into its own panel");
            Assert.Contains(c.Tips(), t => t == "Pop Walter out into its own panel");
            Press(next);
            Assert.Equal([c.First.Id, c.Second.Id], popped);
        }, popOut: popped.Add);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-172")] // HydraPanel.swift:184 if let selected, let popOut, heads.count > 1: popping out never empties this panel
    [Trait("Req", "UI-HYDRA-021")] // HydraPanel.swift:184 present only when a head is on stage, a pop-out target exists AND more than one head is in the panel
    public void The_pop_out_button_needs_a_head_on_stage_a_pop_out_target_and_more_than_one_head() => Run(c =>
    {
        Assert.Single(c.PopOutButtons());

        c.InPanel(c.Second, false);
        c.Tick(1);
        Assert.Empty(c.PopOutButtons());
        Assert.Contains("1 head", c.Tips());
        Assert.Contains(Dismiss, c.Tips());

        c.InPanel(c.Second, true);
        c.Tick(1);
        Assert.Single(c.PopOutButtons());

        // No head in the panel at all: nothing on stage, so nothing to pop out.
        c.InPanel(c.First, false);
        c.InPanel(c.Second, false);
        c.Tick(1);
        Assert.Empty(c.PopOutButtons());
        Assert.Contains(Dismiss, c.Tips());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-172")] // HydraPanel.swift:29, :184 popOut is nil where the head cannot go anywhere: no button then, however many heads
    [Trait("Req", "UI-HYDRA-021")] // HydraPanel.swift:184 a pop-out target must exist
    public void Without_a_pop_out_target_there_is_no_pop_out_button_even_with_several_heads() => Run(c =>
    {
        Assert.Contains("2 heads", c.Tips());
        Assert.Empty(c.PopOutButtons());
        Assert.DoesNotContain(c.Tips(), t => t.StartsWith("Pop ", StringComparison.Ordinal));
        Assert.Contains(Dismiss, c.Tips());
    }, withoutPopOut: true);

    [Fact]
    [Trait("Req", "PR-HYDRA-172")] // HydraPanel.swift:190-192 ChromeCircleButton(symbol: "xmark", help: "Dismiss the panel; heads keep working") { dismiss() }
    public void The_dismiss_button_is_always_there_and_calls_dismiss()
    {
        var dismissed = 0;
        Run(c =>
        {
            var button = Assert.IsType<ChromeCircleButton>(Assert.Single(c.Find<ChromeCircleButton>(), b => Symbols(b).Contains("xmark")));
            Assert.Equal(Dismiss, ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(button))));
            Press(button);
            Assert.Equal(1, dismissed);
            // Still there with one head, and after the pop-out button has gone.
            c.InPanel(c.Second, false);
            c.Tick(1);
            Assert.Single(c.Find<ChromeCircleButton>(), b => Symbols(b).Contains("xmark"));
        }, dismiss: () => dismissed++);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-172")] // HydraPanel.swift:177-180 popped out: ChromeCircleButton(symbol: "arrow.down.left", help: "Put \(name) back in the team panel") { dismiss() }
    public void A_popped_out_panel_has_one_put_back_button_naming_its_head_and_no_other_buttons()
    {
        var dismissed = 0;
        Run(c =>
        {
            var button = Assert.Single(c.Find<ChromeCircleButton>());
            Assert.Contains("arrow.down.left", Symbols(button));
            Assert.Equal("Put Hank back in the team panel", ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(button))));
            Assert.DoesNotContain(c.Tips(), t => t == Dismiss || t.StartsWith("Pop ", StringComparison.Ordinal));
            Press(button);
            Assert.Equal(1, dismissed);
        }, dismiss: () => dismissed++, poppedOut: true);
    }
}
