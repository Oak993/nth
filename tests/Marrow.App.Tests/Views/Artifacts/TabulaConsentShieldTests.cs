using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Artifacts;

// PORT-ONLY: web surfaces security review 2026-10-13, finding L1 (ED-NEW-web-sec-artifacts-3; DESIGN.md 8.4 and 10.1: "Prompts
// never take focus when they appear. Enter does nothing in them; Esc refuses. F6 moves into the open prompt ... For 400 ms after a
// prompt appears, clicks and keys on its buttons are ignored", "a Space within 400 ms of the prompt appearing or of focus entering
// it is ignored"). The artifact chooses when its consent cards appear (nth.complete, nth.callTool or files.pick on a timer), so a
// click or Enter the user meant for the page must never land on Allow. The cards follow Visus's prompt rules on the Visus clock.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-sec-artifacts-3")]
public sealed class TabulaConsentShieldTests(HeadlessSession headless)
{
    private const string Page = "<!doctype html><html><body><h3>Orbit</h3></body></html>";

    private static void Key(Interactive target, Key key, RoutedEvent? routed = null) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = routed ?? InputElement.KeyDownEvent, Key = key });

    [Fact]
    public void A_card_takes_no_focus_ignores_clicks_for_400_ms_and_Enter_never_answers_it() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        VisusHost.Time = rig.Runtime.Time;
        try
        {
            var first = rig.Show(ArtifactKind.Html, Page);
            var address = new ArtifactAddress(TabulaRig.Chat, first.Artifact.Id);
            var asked = TabulaBridgeHost.Instance.ConfirmModelAsync(address, "Claude Sonnet", CancellationToken.None);
            rig.Layout();
            Assert.Equal(TabulaBridgeHost.ModelTitle("Claude Sonnet"), rig.View.Cards.Showing);
            var allow = rig.View.Cards.Buttons[0];
            var card = (Control)rig.View.Cards.Popup!.Child!;

            // Nothing in the card holds the keyboard when it appears: a key meant for the page reaches no button.
            Assert.All(rig.View.Cards.Buttons, button => Assert.False(button.IsFocused, "a card button took the focus as the card appeared"));
            Assert.False(card.IsKeyboardFocusWithin);

            // A click that lands as the card appears does nothing; Enter does nothing at any time.
            TabulaRig.Click(allow);
            rig.Layout();
            Assert.False(asked.IsCompleted, "a click within 400 ms of the card appearing answered it");
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(399));
            TabulaRig.Click(allow);
            rig.Layout();
            Assert.False(asked.IsCompleted, "a click at 399 ms answered the card");
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(600));
            Key(allow, Avalonia.Input.Key.Enter);
            rig.Layout();
            Assert.False(asked.IsCompleted, "Enter answered the card");

            // After the shield, the button answers.
            TabulaRig.Click(allow);
            rig.Layout();
            Assert.True(asked.IsCompletedSuccessfully && asked.Result);
        }
        finally
        {
            VisusHost.Time = TimeProvider.System;
        }
    });

    [Fact]
    public void F6_enters_the_card_restarts_the_shield_Space_waits_and_Escape_refuses_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        VisusHost.Time = rig.Runtime.Time;
        try
        {
            var first = rig.Show(ArtifactKind.Html, Page);
            var address = new ArtifactAddress(TabulaRig.Chat, first.Artifact.Id);
            var tool = TabulaBridgeHost.Instance.ConfirmToolAsync(address, "github", "search_issues", CancellationToken.None);
            rig.Layout();
            var card = (Control)rig.View.Cards.Popup!.Child!;
            rig.Runtime.Advance(TimeSpan.FromSeconds(2));

            // F6 puts the keyboard on the card's words, never on a button.
            Assert.True(VisusPrompts.FocusNewest(TopLevel.GetTopLevel(rig.View)));
            rig.Layout();
            Assert.True(card.IsKeyboardFocusWithin, "F6 did not reach the open card");
            Assert.All(rig.View.Cards.Buttons, button => Assert.False(button.IsFocused, "F6 landed on a button"));

            // The keyboard entering the card restarts the shield: a Space within 400 ms presses nothing.
            var allow = rig.View.Cards.Buttons[0];
            Key(allow, Avalonia.Input.Key.Space);
            Key(allow, Avalonia.Input.Key.Space, InputElement.KeyUpEvent);
            TabulaRig.Click(allow);
            rig.Layout();
            Assert.False(tool.IsCompleted, "a key within 400 ms of the keyboard entering the card answered it");

            // Escape is the safe answer, shield or not.
            Key(card.GetVisualDescendants().OfType<Control>().First(c => c.IsFocused), Avalonia.Input.Key.Escape);
            rig.Layout();
            Assert.True(tool.IsCompletedSuccessfully);
            Assert.False(tool.Result);
            Assert.Null(rig.View.Cards.Showing);
            Assert.False(VisusPrompts.AnyIn(TopLevel.GetTopLevel(rig.View)), "the closed card stayed in the F6 list");
        }
        finally
        {
            VisusHost.Time = TimeProvider.System;
        }
    });
}
