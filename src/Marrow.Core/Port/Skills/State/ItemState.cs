// PORT-ONLY: first-release feature unit SK-7 (DL-7 of porting/first-release/skills-ONDEMAND-PLAN.md section 7); there is no Swift twin.
// The state of one catalogue item and the transitions the plan allows between states.
namespace Marrow.Core.Port.Skills.State;

/// <summary>
/// The nine item states of DL-7. <c>Failed</c> is 5.4's and 3.9's, <c>Damaged</c> is 3.5's, <c>UpdateAvailable</c> is 3.7's, <c>Removing</c> is 3.8's;
/// <c>Queued</c> is the wait behind other downloads of Download all.
/// </summary>
public enum ItemState
{
    Available,
    Queued,
    Downloading,
    Checking,
    Installed,
    UpdateAvailable,
    Failed,
    Damaged,
    Removing,
}

/// <summary>
/// One item's record: its state, the pin it is installed at (null until a first install is checked), the pin a download in flight or an offered update is for,
/// and the message of a failure. <see cref="Problem"/> names the first invariant the record breaks; a record that breaks one never enters a transition
/// and is never written to or read from state.json (ED-NEW-f-sk-7).
/// </summary>
public sealed record ItemRecord(string Id, ItemState State, string? InstalledPin, string? TargetPin, string? Failure)
{
    public static ItemRecord Available(string id) => new(id, ItemState.Available, null, null, null);

    public string? Problem()
    {
        if (!StateRules.IsId(Id)) return $"'{Id}' is not a usable item id";
        if (InstalledPin is not null && string.IsNullOrWhiteSpace(InstalledPin)) return "the installed pin is blank";
        if (TargetPin is not null && string.IsNullOrWhiteSpace(TargetPin)) return "the target pin is blank";
        if (Failure is not null && State != ItemState.Failed) return $"a {State} item has no failure message";
        return State switch
        {
            ItemState.Available => InstalledPin is not null || TargetPin is not null ? "an available item has no pins" : null,
            ItemState.Queued or ItemState.Downloading or ItemState.Checking => TargetPin is null ? $"a {State} item names the pin it is for" : null,
            ItemState.Installed or ItemState.Damaged or ItemState.Removing =>
                InstalledPin is null ? $"a {State} item has an installed pin" : TargetPin is not null ? $"a {State} item has no target pin" : null,
            ItemState.UpdateAvailable =>
                InstalledPin is null ? "an update is offered for an installed item" : TargetPin is null ? "an offered update names its pin" : TargetPin == InstalledPin ? "an offered update differs from the installed pin" : null,
            ItemState.Failed => string.IsNullOrWhiteSpace(Failure) ? "a failed item says what failed" : TargetPin is null ? "a failed item names the pin it was for" : null,
        };
    }
}

/// <summary>An illegal state change; the record or state it was asked of is unchanged.</summary>
public sealed class IllegalTransitionException(string message) : InvalidOperationException(message);

/// <summary>
/// The edges. From ONDEMAND-PLAN.md 5.4 (asked, downloading, checking, done, failed, Retry, cancel), 3.2 (cancel keeps the .part), 3.5 (damaged, Repair is a fresh
/// download of that version), 3.7 (update available), 3.8 (removing), 3.9 (failure and Retry) and the OD-0099 review item 3 (a failed item can be checked after the
/// file was placed by hand). A cancel (and giving up on a failure) returns an item to where it came from, known from its pins: no installed version means Available,
/// a download of the installed pin itself means Damaged (it was a Repair), any other target means UpdateAvailable. The rest is an ENGINEERING DECISION
/// (ED-NEW-f-sk-7).
/// </summary>
public static class ItemStateMachine
{
    private static bool ReturnsTo(ItemRecord from, ItemState to) => to switch
    {
        ItemState.Available => from.InstalledPin is null,
        ItemState.Damaged => from.InstalledPin is not null && from.TargetPin == from.InstalledPin,
        ItemState.UpdateAvailable => from.InstalledPin is not null && from.TargetPin != from.InstalledPin,
        _ => false,
    };

    public static bool IsLegal(ItemRecord from, ItemState to)
    {
        if (from.Problem() is not null) return false;
        return from.State switch
        {
            ItemState.Available => to is ItemState.Queued or ItemState.Downloading,
            ItemState.Queued => to is ItemState.Downloading or ItemState.Failed || ReturnsTo(from, to),
            ItemState.Downloading => to is ItemState.Checking or ItemState.Failed || ReturnsTo(from, to),
            ItemState.Checking => to is ItemState.Installed or ItemState.Failed,
            ItemState.Installed => to is ItemState.UpdateAvailable or ItemState.Damaged or ItemState.Removing,
            ItemState.UpdateAvailable => to is ItemState.UpdateAvailable or ItemState.Installed or ItemState.Queued or ItemState.Downloading or ItemState.Damaged or ItemState.Removing,
            ItemState.Failed => to is ItemState.Queued or ItemState.Downloading or ItemState.Checking || ReturnsTo(from, to) || (to == ItemState.Removing && from.InstalledPin is not null),
            ItemState.Damaged => to is ItemState.Queued or ItemState.Downloading or ItemState.Removing,
            ItemState.Removing => to == ItemState.Available,
        };
    }

    /// <summary>
    /// Moves the record to <paramref name="to"/> or throws <see cref="IllegalTransitionException"/>. <paramref name="targetPin"/> is required from Available (the pin the
    /// download is for), when an update is first offered or re-pointed, and refused anywhere else except as a restatement of the pin already pending; <paramref name="failure"/>
    /// is required for Failed and refused for every other target.
    /// </summary>
    public static ItemRecord Apply(ItemRecord record, ItemState to, string? targetPin = null, string? failure = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Problem() is { } problem) throw new IllegalTransitionException($"{record.Id}: the record is not valid ({problem})");
        if (!IsLegal(record, to)) throw new IllegalTransitionException($"{record.Id}: {record.State} -> {to} is not a legal change");

        IllegalTransitionException Refuse(string why) => new($"{record.Id}: {record.State} -> {to}: {why}");
        string Required(string? given) => string.IsNullOrWhiteSpace(given) ? throw Refuse("a pin is required") : given;
        string Matches(string? given, string? pending) => given is null || given == pending ? pending! : throw Refuse($"the pin pending is {pending}, not {given}");
        void NoPin() { if (targetPin is not null) throw Refuse("a pin is not accepted here"); }

        if (to != ItemState.Failed && failure is not null) throw Refuse("only a failure carries a message");
        var installed = record.InstalledPin;
        var from = record.State;
        string? target = null;
        string? message = null;
        switch (to)
        {
            case ItemState.Queued:
            case ItemState.Downloading:
                target = from switch
                {
                    ItemState.Available => Required(targetPin),
                    ItemState.Damaged => Matches(targetPin, installed),
                    _ => Matches(targetPin, record.TargetPin),
                };
                break;
            case ItemState.Checking:
                NoPin();
                target = record.TargetPin;
                break;
            case ItemState.Installed:
                NoPin();
                if (from == ItemState.Checking)
                {
                    installed = record.TargetPin;
                }
                break;
            case ItemState.UpdateAvailable:
                if (from is ItemState.Installed or ItemState.UpdateAvailable)
                {
                    target = Required(targetPin);
                    if (target == installed) throw Refuse("the offered pin is the installed pin");
                    if (target == record.TargetPin) throw Refuse("that update is already offered");
                }
                else
                {
                    NoPin();
                    target = record.TargetPin;
                }
                break;
            case ItemState.Failed:
                NoPin();
                if (string.IsNullOrWhiteSpace(failure)) throw Refuse("a failure needs a message");
                target = record.TargetPin;
                message = failure;
                break;
            case ItemState.Available:
                NoPin();
                if (from == ItemState.Removing) installed = null;
                break;
            case ItemState.Damaged:
            case ItemState.Removing:
                NoPin();
                break;
        }
        var next = new ItemRecord(record.Id, to, installed, target, message);
        return next.Problem() is { } broken ? throw Refuse($"the result would not be valid ({broken})") : next;
    }
}
