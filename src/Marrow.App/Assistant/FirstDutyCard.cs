// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4; canvas 5 "Your first duty is set."; unit AS-10; ED-NEW-dextra-as10-14). No Swift twin.
// What the duties view shows under the list when the first standing duty has just been made: two separate switches, both off until
// the owner taps them. The words are the canvas's. The card is the model; the view (AS-8's duties view) draws it, with each row a
// switch whose accessible name is its label. It appears once, for the first duty, and is kept until the owner dismisses it.
namespace Marrow.App.Assistant;

/// <summary>One switch of the card.</summary>
public sealed record SwitchRow(string Id, string Label, string Note, bool On);

public sealed class FirstDutyCard
{
    public const string Title = "Your first duty is set.";

    public const string Body = "Two choices, both off until you tap them. Letting agents spend in the background is your call.";

    public const string KeepId = "keep-working";

    public const string StartId = "start-with-windows";

    private readonly BackgroundSwitches switches;
    private bool visible;

    public FirstDutyCard(BackgroundSwitches switches)
    {
        this.switches = switches ?? throw new ArgumentNullException(nameof(switches));
        switches.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public bool Visible => visible;

    public IReadOnlyList<SwitchRow> Rows =>
    [
        new SwitchRow(KeepId, BackgroundSwitches.KeepWorkingWords, BackgroundSwitches.KeepWorkingNote, switches.KeepWorkingWhenClosed),
        new SwitchRow(StartId, BackgroundSwitches.StartWithWindowsWords, BackgroundSwitches.StartWithWindowsNote, switches.StartWithWindowsOn),
    ];

    /// <summary>The first duty was made: the card appears, once.</summary>
    public void FirstDutyMade()
    {
        if (switches.FirstDutyOffered || visible) return;
        visible = true;
        switches.MarkFirstDutyOffered(); // tells the view (the switches' Changed is the card's)
    }

    /// <summary>The owner tapped a switch. Returns why it could not change, or null.</summary>
    public string? Toggle(string id) => id switch
    {
        KeepId => switches.SetKeepWorking(!switches.KeepWorkingWhenClosed),
        StartId => switches.SetStartWithWindows(!switches.StartWithWindowsOn),
        _ => "That is not one of the switches.",
    };

    public void Dismiss()
    {
        if (!visible) return;
        visible = false;
        Changed?.Invoke();
    }
}
