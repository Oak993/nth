// PORT-ONLY: Tactus pill v2, the card's misclick protection (OD-0144, OD-0145, ED-NEW-pcctl-20; DESIGN.md 4.1, 4.3, 10.4
// "Safety", 10.5; critiques C12 and S7). The card lives in a no-activate overlay that can appear, page or grow under a
// resting pointer, so an answer counts only when it is clearly meant:
// - never in the first 400 ms of a body or a new layout (the answer controls fade from 35 % to full meanwhile);
// - a mouse click only after the pointer rested on that control for 150 ms (touch and pen are not delayed, they cannot
//   be stray hovers); the hover timer starts again with every layout;
// - a control that landed under the pointer (within 4 DIP) stays inert, at 35 %, until the pointer has left it once;
// - "Always allow" takes two separate clicks: the first arms it for 3 s, the second counts only when it is not the second
//   click of a double-click and comes at least 600 ms after the first.
// Later, the pager, the composer and its Send, Open chat and Reply instead are free of all of it. Keys: Enter never
// answers (outside the composer it flashes a hint; in the composer it sends the text, or commits an open IME
// composition, and Shift+Enter is a new line); Space presses the focused control under the same shield and two-step rule
// (a held key's repeats never count); Esc folds the card, or with an IME composition open only cancels the composition.
// Pure: times in ms on the caller's clock, positions in the card's own DIP.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>How a card control is protected.</summary>
public enum CardControlKind
{
    /// <summary>An approval, confirm or show-window button, a chip, Dismiss or Discard: shield, rest and inert rules.</summary>
    Answer,

    /// <summary>"Always allow": an answer that also takes two separate clicks.</summary>
    Always,

    /// <summary>Later, the pager arrows, the composer and its Send, Open chat, Reply instead: no protection.</summary>
    Free,
}

/// <summary>One control of the card's current layout, in the card's DIP.</summary>
public sealed record CardControl(string Id, PillRect Bounds, CardControlKind Kind);

/// <summary>What a click or a Space press on a card control did.</summary>
public enum CardClick
{
    /// <summary>It counts: the control acts.</summary>
    Fires,

    /// <summary>Inside the 400 ms shield: ignored.</summary>
    Shielded,

    /// <summary>A mouse click before the pointer rested 150 ms on the control: ignored, with the rest hint.</summary>
    NotRested,

    /// <summary>The control landed under the pointer and the pointer has not left it yet: ignored.</summary>
    Inert,

    /// <summary>"Always allow"'s first click: it now reads "Click again to always allow" for 3 s.</summary>
    Armed,

    /// <summary>A second click on an armed "Always allow" inside 600 ms or as part of a double-click: ignored, still armed.</summary>
    TooSoon,

    /// <summary>No such control in the current layout.</summary>
    Unknown,
}

/// <summary>The keys the card handles itself.</summary>
public enum CardKey
{
    Enter,
    Space,
    Escape,
    Other,
}

/// <summary>What a key in the card does.</summary>
public enum CardKeyAction
{
    /// <summary>Nothing of the card's own (the key goes on to the focused control as typing, or nowhere).</summary>
    None,

    /// <summary>Enter outside the composer: "Enter does nothing here" flashes; nothing is answered.</summary>
    FlashEnterHint,

    /// <summary>Enter in the composer with text and no composition: the text is sent.</summary>
    SendText,

    /// <summary>Shift+Enter in the composer: a new line.</summary>
    NewLine,

    /// <summary>Enter with an IME composition open: the composition is committed, nothing is sent.</summary>
    CommitComposition,

    /// <summary>Esc with an IME composition open: only the composition is cancelled (and that Esc counts toward nothing).</summary>
    CancelComposition,

    /// <summary>Esc: the card folds back into the pill and the keyboard is handed back. It never answers.</summary>
    Fold,

    /// <summary>Space on a focused control: see the click result.</summary>
    Press,
}

/// <summary>A key's action and, for <see cref="CardKeyAction.Press"/>, what the press did.</summary>
public readonly record struct CardKeyResult(CardKeyAction Action, CardClick Click = CardClick.Unknown);

public sealed class CardShield
{
    /// <summary>No answer counts this long after a body or a layout shows.</summary>
    public const double ShieldMs = 400;

    /// <summary>Answer controls start at this opacity and reach full at the end of the shield (linear).</summary>
    public const double ShieldFrom = 0.35;

    /// <summary>A mouse click counts only after the pointer rested on the control this long.</summary>
    public const double RestMs = 150;

    /// <summary>"Always allow"'s second click comes at least this long after the first.</summary>
    public const double AlwaysGapMs = 600;

    /// <summary>"Always allow" stays armed this long after its first click.</summary>
    public const double AlwaysArmedMs = 3000;

    /// <summary>A control within this distance of the pointer counts as under it.</summary>
    public const double UnderMargin = 4;

    public const string EnterHint = "Enter does nothing here";
    public const string RestHint = "Rest on a button for a moment, then click";
    public const string AgainHint = "Click again, a moment later";
    public const string ArmedLabel = "Click again to always allow";

    private readonly Dictionary<string, Entry> controls = new(StringComparer.Ordinal);
    private double shownAt = double.NegativeInfinity;
    private string? armed;
    private double armedAt;

    /// <summary>When the current shield ends (for the UI's one timer).</summary>
    public double ShieldEndsAt => shownAt + ShieldMs;

    /// <summary>
    /// A body, a page, the next ask or any height change shows at <paramref name="at"/> (when its body starts to show) with
    /// <paramref name="layout"/>: the shield and every hover timer start again, and each protected control under the pointer
    /// turns inert. <paramref name="newBody"/> also disarms "Always allow".
    /// </summary>
    public void Show(double at, IReadOnlyList<CardControl> layout, PillVector? pointer, bool newBody = true)
    {
        shownAt = at;
        if (newBody) armed = null;
        controls.Clear();
        foreach (var control in layout)
        {
            var under = pointer is { } p && control.Kind != CardControlKind.Free && Under(control.Bounds, p);
            controls[control.Id] = new Entry(control) { Inert = under, Inside = pointer is { } q && Contains(control.Bounds, q) };
        }
        if (armed is not null && !controls.ContainsKey(armed)) armed = null;
    }

    /// <summary>Marks controls inert that the placement could not clear (they wait until the pointer leaves them once).</summary>
    public void MarkInert(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            if (controls.TryGetValue(id, out var entry) && entry.Control.Kind != CardControlKind.Free) entry.Inert = true;
    }

    /// <summary>The mouse moved to <paramref name="point"/> in the card's DIP, or left the card (null).</summary>
    public void Moved(double now, PillVector? point)
    {
        foreach (var entry in controls.Values)
        {
            var inside = point is { } p && Contains(entry.Control.Bounds, p);
            if (inside && !entry.Inside) entry.RestingSince = Math.Max(now, shownAt);
            if (!inside) entry.RestingSince = null;
            entry.Inside = inside;
            if (entry.Inert && (point is not { } q || !Under(entry.Control.Bounds, q))) entry.Inert = false;
        }
    }

    /// <summary>A click on control <paramref name="id"/>: <paramref name="clickCount"/> is 2 for the second click of a double-click.</summary>
    public CardClick Click(double now, string id, PillPointerKind kind, int clickCount = 1)
    {
        if (!controls.TryGetValue(id, out var entry)) return CardClick.Unknown;
        if (entry.Control.Kind == CardControlKind.Free) return CardClick.Fires;
        if (now < ShieldEndsAt) return CardClick.Shielded;
        if (kind == PillPointerKind.Mouse)
        {
            if (entry.Inert) return CardClick.Inert;
            if (entry.RestingSince is not { } since || now - since < RestMs) return CardClick.NotRested;
        }
        return Second(now, entry, doubleClick: clickCount > 1);
    }

    /// <summary>
    /// A key while the card has the keyboard. <paramref name="focused"/> is the focused control's id (null: nothing, which is
    /// where focus starts); <paramref name="inComposer"/> when the composer's field has focus.
    /// </summary>
    public CardKeyResult Key(double now, CardKey key, bool shift, bool repeat, string? focused, bool inComposer, bool composing, bool composerHasText)
    {
        switch (key)
        {
            case CardKey.Escape:
                return new CardKeyResult(composing ? CardKeyAction.CancelComposition : CardKeyAction.Fold);
            case CardKey.Enter when inComposer:
                if (composing) return new CardKeyResult(CardKeyAction.CommitComposition);
                if (shift) return new CardKeyResult(CardKeyAction.NewLine);
                return new CardKeyResult(composerHasText ? CardKeyAction.SendText : CardKeyAction.None);
            case CardKey.Enter:
                return new CardKeyResult(CardKeyAction.FlashEnterHint);
            case CardKey.Space when !inComposer && focused is not null:
                if (repeat) return new CardKeyResult(CardKeyAction.None);
                if (!controls.TryGetValue(focused, out var entry)) return new CardKeyResult(CardKeyAction.Press, CardClick.Unknown);
                if (entry.Control.Kind == CardControlKind.Free) return new CardKeyResult(CardKeyAction.Press, CardClick.Fires);
                if (now < ShieldEndsAt) return new CardKeyResult(CardKeyAction.Press, CardClick.Shielded);
                return new CardKeyResult(CardKeyAction.Press, Second(now, entry, doubleClick: false));
            default:
                return new CardKeyResult(CardKeyAction.None);
        }
    }

    /// <summary>The control's opacity: inert ones stay at 35 %; answers rise from 35 % to full over the shield.</summary>
    public double Opacity(double now, string id)
    {
        if (!controls.TryGetValue(id, out var entry) || entry.Control.Kind == CardControlKind.Free) return 1;
        if (entry.Inert) return ShieldFrom;
        var progress = Math.Clamp((now - shownAt) / ShieldMs, 0, 1);
        return ShieldFrom + ((1 - ShieldFrom) * progress);
    }

    /// <summary>True while "Always allow" (<paramref name="id"/>) reads "Click again to always allow".</summary>
    public bool IsArmed(double now, string id) => armed == id && now - armedAt < AlwaysArmedMs;

    /// <summary>True while the control waits for the pointer to leave it.</summary>
    public bool IsInert(string id) => controls.TryGetValue(id, out var entry) && entry.Inert;

    private CardClick Second(double now, Entry entry, bool doubleClick)
    {
        if (entry.Control.Kind != CardControlKind.Always) return CardClick.Fires;
        if (!IsArmed(now, entry.Control.Id))
        {
            // A double-click's second click never arms either: it is part of the same gesture as its first.
            if (doubleClick) return CardClick.TooSoon;
            armed = entry.Control.Id;
            armedAt = now;
            return CardClick.Armed;
        }
        if (doubleClick || now - armedAt < AlwaysGapMs) return CardClick.TooSoon;
        armed = null;
        return CardClick.Fires;
    }

    private static bool Contains(PillRect rect, PillVector point) =>
        point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;

    private static bool Under(PillRect rect, PillVector point) => Contains(rect.Inflate(UnderMargin), point);

    private sealed class Entry(CardControl control)
    {
        public CardControl Control { get; } = control;
        public bool Inert { get; set; }
        public bool Inside { get; set; }
        public double? RestingSince { get; set; }
    }
}
