using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4; canvas 5 "Your first duty is set."; ED-NEW-dextra-as10-14): the card that offers the two switches
// when the first duty is made. Its words are the canvas's; both switches are off until tapped; it appears once and is kept until
// dismissed; a tap changes the real thing (the store, the Run entry).
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
public sealed class FirstDutyCardTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";

    private readonly FakeRunKey key = new();
    private JsonObject? stored;

    private BackgroundSwitches Switches() => new(() => stored?.DeepClone() as JsonObject, document => stored = (JsonObject)document.DeepClone(), new StartWithWindows(key), () => Exe);

    [Fact]
    public void The_card_says_what_the_canvas_says_and_both_rows_are_off()
    {
        var card = new FirstDutyCard(Switches());
        Assert.Equal("Your first duty is set.", FirstDutyCard.Title);
        Assert.Equal("Two choices, both off until you tap them. Letting agents spend in the background is your call.", FirstDutyCard.Body);
        Assert.Equal(
            [
                new SwitchRow("keep-working", "Keep working when the window is closed", "nth stays in the tray; Stop is in the tray menu.", false),
                new SwitchRow("start-with-windows", "Start with Windows, hidden", "Duties catch up once if nth was closed.", false),
            ],
            card.Rows);
        Assert.False(card.Visible);
    }

    [Fact]
    public void The_first_duty_shows_it_once_and_a_restart_does_not_show_it_again()
    {
        var card = new FirstDutyCard(Switches());
        var changes = 0;
        card.Changed += () => changes++;
        card.FirstDutyMade();
        Assert.True(card.Visible);
        card.FirstDutyMade();
        Assert.Equal(1, changes);
        card.Dismiss();
        Assert.False(card.Visible);
        var again = new FirstDutyCard(Switches());
        again.FirstDutyMade();
        Assert.False(again.Visible);
    }

    [Fact]
    public void A_tap_flips_the_real_switch_and_the_row_follows()
    {
        var switches = Switches();
        var card = new FirstDutyCard(switches);
        Assert.Null(card.Toggle(FirstDutyCard.KeepId));
        Assert.True(switches.KeepWorkingWhenClosed);
        Assert.True(card.Rows[0].On);
        Assert.False(card.Rows[1].On);
        Assert.Null(card.Toggle(FirstDutyCard.StartId));
        Assert.True(card.Rows[1].On);
        Assert.Single(key.Values);
        Assert.Null(card.Toggle(FirstDutyCard.KeepId));
        Assert.False(card.Rows[0].On);
        Assert.NotNull(card.Toggle("something-else"));
    }

    [Fact]
    public void The_card_tells_its_view_when_a_switch_changes_elsewhere()
    {
        var switches = Switches();
        var card = new FirstDutyCard(switches);
        var changes = 0;
        card.Changed += () => changes++;
        switches.SetKeepWorking(true); // the tray's item
        Assert.Equal(1, changes);
        Assert.True(card.Rows[0].On);
    }
}
