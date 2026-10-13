using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 4.2 "Keyboard": every button on an irreversible card; 16 "Enter never sends"; the canvas's kHint and
// hold handlers; ED-NEW-dextra-as8-6): the button acts on a pointer click or a full one-second hold of Space; Enter, a short Space
// and a screen reader's Invoke only show the hint.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-6")]
public sealed class HoldToActButtonTests(HeadlessSession headless)
{
    private static IDisposable Quiet() => HoldToActButton.HintPresenter.Override(static (_, _) => { });

    private static (HoldToActButton Hold, Button Other, StackPanel Root, List<int> Acted) Make()
    {
        var hold = new HoldToActButton(new Border { Width = 80, Height = 26, Background = Avalonia.Media.Brushes.Gray }, "Send");
        var acted = new List<int>();
        hold.Acted += () => acted.Add(acted.Count + 1);
        var other = new Button { Content = "Elsewhere" };
        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 20, Margin = new Thickness(40), Children = { hold, other } };
        return (hold, other, root, acted);
    }

    [Fact]
    public void Enter_never_acts_and_says_how_to_confirm()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, _, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            hold.Focus(NavigationMethod.Tab);

            stage.Press(Key.Enter);
            Assert.Empty(acted);
            Assert.Equal(HoldToActButton.EnterHint, hold.Hint);
            Assert.Equal("Enter does nothing here. Click it, or hold Space for one second.", HoldToActButton.EnterHint);
            stage.Advance(TimeSpan.FromSeconds(2));
            Assert.Null(hold.Hint); // the hint goes by itself
        });
    }

    [Fact]
    public void A_short_tap_of_Space_cancels_and_says_to_hold_it()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, _, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            hold.Focus(NavigationMethod.Tab);

            stage.Down(Key.Space);
            Assert.True(hold.IsHolding);
            stage.Advance(TimeSpan.FromMilliseconds(600));
            stage.Up(Key.Space);
            Assert.False(hold.IsHolding);
            Assert.Equal(HoldToActButton.ShortHint, hold.Hint);
            stage.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(acted);
        });
    }

    [Fact]
    public void Holding_Space_for_a_full_second_acts_once_and_key_repeat_never_restarts_it()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, _, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            hold.Focus(NavigationMethod.Tab);

            stage.Down(Key.Space);
            stage.Advance(TimeSpan.FromMilliseconds(500));
            stage.Down(Key.Space); // the keyboard's auto-repeat
            stage.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Empty(acted);
            stage.Advance(TimeSpan.FromMilliseconds(2));
            Assert.Equal([1], acted);
            Assert.False(hold.IsHolding);
            stage.Up(Key.Space);
            stage.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal([1], acted);
        });
    }

    [Fact]
    public void Losing_focus_during_a_hold_cancels_it()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, other, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            hold.Focus(NavigationMethod.Tab);

            stage.Down(Key.Space);
            other.Focus(NavigationMethod.Tab);
            stage.Advance(TimeSpan.FromSeconds(2));
            Assert.False(hold.IsHolding);
            Assert.Empty(acted);
        });
    }

    [Fact]
    public void A_screen_readers_Invoke_only_shows_the_hint_and_the_help_text_says_so()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, _, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            var peer = ControlAutomationPeer.CreatePeerForElement(hold);

            Assert.Equal("Send", peer.GetName());
            Assert.Equal(HoldToActButton.EnterHint, peer.GetHelpText());
            Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke();
            stage.Layout();
            Assert.Empty(acted);
            Assert.Equal(HoldToActButton.EnterHint, hold.Hint);
        });
    }

    [Fact]
    public void A_pointer_click_acts()
    {
        headless.OnUiThread(() =>
        {
            using var hints = Quiet();
            var (hold, _, root, acted) = Make();
            using var stage = new HomeStage(root, 400, 300);
            var at = stage.Box(hold).Center;

            stage.Window.MouseMove(at);
            stage.Window.MouseDown(at, MouseButton.Left);
            stage.Window.MouseUp(at, MouseButton.Left);
            stage.Layout();
            Assert.Equal([1], acted);
            Assert.Null(hold.Hint);
        });
    }
}
