// PORT-ONLY: Tactus pill v2, the card the pill grows into (OD-0145, ED-NEW-pcctl-20, ED-NEW-pcctl-26; DESIGN.md 10.4 "Answer
// from the pill", 10.5; canvas pc-control-ui pill v2 `.pk`, `bodyOf`, `wireBody`, `showAsk`). The header is the pill's own
// row (ControlPill); this is everything under it: one ask's body at a time, laid out once at its final size before any
// motion, inside the clip that follows the card's growing shape, so the body is revealed by the glass and never fades.
// Paging and the next ask slide the bodies sideways under that clip (spring response 0.28 s, damping 1). Every press goes
// through the misclick shield (CardShield: the 400 ms shield and its 35 % to full rise, the 150 ms rest, inert controls
// under a resting pointer, "Always allow"'s two separate clicks) and every key through its key rules (Enter never
// answers, Space presses through the shield, Esc folds). It never answers by itself: it raises what the user chose.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

/// <summary>What one ask's body shows (the App adapts the broker's asks to these; UI never sees the broker).</summary>
public abstract record PillCardBody(string AskId, int Revision);

/// <summary>An app approval (DESIGN.md 4.1, 10.4 "Approval body").</summary>
/// <param name="Who">"This chat", or the Hydra head that asks ("Head 3"), as the summary line names it.</param>
/// <param name="Helper">The fast helper's model when it is on ("Also seen by GLM-5 Vision (fast helper)", section 2.12).</param>
public sealed record PillApprovalBody(string AskId, int Revision, string App, string Path, bool Risky, bool UseAsked, bool AllowAlways, string Who = "This chat", string? Helper = null)
    : PillCardBody(AskId, Revision);

/// <summary>One row of a confirm's content: its label, the value read from the window (or "Could not read"), and whether it changed.</summary>
public sealed record PillConfirmRow(string Label, string Value, bool Unreadable = false, bool Changed = false, bool Long = false);

/// <summary>A guarded action's confirm (DESIGN.md 4.3, 10.4 "Confirm body"): the element as the app shows it, then the exact content.</summary>
/// <param name="ChangedNote">After a re-read at the click found a change: "The recipients changed while this waited. Check, then confirm."</param>
public sealed record PillConfirmBody(string AskId, int Revision, string App, string Element, string Verb, IReadOnlyList<PillConfirmRow> Rows, bool Unreadable = false, string? ChangedNote = null)
    : PillCardBody(AskId, Revision);

/// <summary>"Show this window?" (DESIGN.md 10.4 "Show-window body").</summary>
public sealed record PillShowBody(string AskId, int Revision, string App, string? Why) : PillCardBody(AskId, Revision);

/// <summary>A pc_ask question or the provider's own question (DESIGN.md 10.4 "Question body").</summary>
/// <param name="Model">The model's name ("GPT-6.1 Sol"), never nth's.</param>
/// <param name="Secret">Asks for a secret: "Answer this in the chat" with Open chat, and no composer.</param>
/// <param name="Page">"1 of 3" for a provider question with several questions.</param>
public sealed record PillQuestionBody(string AskId, int Revision, string Model, string Question, IReadOnlyList<string> Choices, bool Multi = false, bool AllowText = true, bool Secret = false, string? Page = null, bool CanDismiss = true)
    : PillCardBody(AskId, Revision);

/// <summary>A provider's own approval: shown, never answered in the pill (DESIGN.md 10.4).</summary>
/// <param name="What">"GPT-6.1 Sol wants to run a command."</param>
/// <param name="FirstLine">The request's first line (a command, in a mono block).</param>
public sealed record PillProviderBody(string AskId, int Revision, string What, string FirstLine) : PillCardBody(AskId, Revision);

/// <summary>An ask that ended while its card held a draft: the draft stays with Discard and Send to chat.</summary>
public sealed record PillWithdrawnBody(string AskId, int Revision, bool AnsweredInChat, string Draft) : PillCardBody(AskId, Revision);

/// <summary>
/// The overlay could not take the keyboard after a click in the reply field (a foreground lock nth cannot pass): "Type
/// your answer in the chat" with Open chat, the draft going with it (DESIGN.md 10.4 "It never steals focus").
/// </summary>
public sealed record PillTypeInChatBody(string AskId, int Revision, string Draft) : PillCardBody(AskId, Revision);

/// <summary>What the user chose in the card.</summary>
public enum PillCardChoice
{
    Deny,
    AllowChat,
    AllowAlways,
    Confirm,
    Cancel,
    Show,
    NotNow,
    Chip,
    Send,
    Dismiss,
    OpenChat,
    Discard,
    SendToChat,
}

/// <summary>
/// A choice for one ask at one revision. <paramref name="Use"/> is the approval's Use chip; <paramref name="Text"/> is the
/// reply field's text (an approval's or a confirm's reason, a question's answer, a draft) or the chip a quick reply sent;
/// <paramref name="Chips"/> are a multi-select question's chosen chips.
/// </summary>
public sealed record PillCardAnswer(string AskId, int Revision, PillCardChoice Choice, bool Use = true, string? Text = null, IReadOnlyList<string>? Chips = null);

/// <summary>The card's own commands (not answers): fold it away (Esc, Later).</summary>
public enum PillCardCommand
{
    Fold,
}

public sealed class PillCard : Canvas
{
    /// <summary>The paging slide's spring response (DESIGN.md 10.4 "Queue").</summary>
    public const double PageResponse = 0.28;

    /// <summary>How long "Always allow"'s armed look lasts (the shield's 3 s).</summary>
    public const double ArmedSeconds = CardShield.AlwaysArmedMs / 1000;

    private readonly IPillAnimator animator;
    private readonly Func<double> clock;
    private readonly Action<double, Action> after;
    // each ask's draft with its caret and selection, in memory only until the ask ends (never on disk: it may be private)
    private readonly Dictionary<string, (string Text, int Start, int End)> drafts = new(StringComparer.Ordinal);
    private bool spaceDown;
    private int armGeneration;
    private PillVector? pointer;

    public PillCard(IPillAnimator animator, Func<double> clock, Action<double, Action> after)
    {
        this.animator = animator;
        this.clock = clock;
        this.after = after;
        Width = PillMorph.CardWidth;
        ClipToBounds = false;
        Background = Brushes.Transparent;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key == Key.Space) spaceDown = false;
        }, RoutingStrategies.Tunnel);
        PointerMoved += (_, e) => Moved(e.GetPosition(this));
        PointerExited += (_, _) => Moved(null);
    }

    public CardShield Shield { get; } = new();

    /// <summary>The body shown now (null: no card).</summary>
    public CardBody? Current { get; private set; }

    /// <summary>A body sliding out after a page (tests).</summary>
    public CardBody? Leaving { get; private set; }

    /// <summary>The user chose something for an ask.</summary>
    public event Action<PillCardAnswer>? Answered;

    public event Action<PillCardCommand>? Command;

    /// <summary>The card's body height changed (the reply field opened or grew, or a new body): the pill resizes the card.</summary>
    public event Action? HeightChanged;

    /// <summary>The reply field's text changed for an ask (the draft the chat gets with Open chat, kept if the ask ends).</summary>
    public event Action<string, string>? DraftChanged;

    /// <summary>The shown body's reply field text ("" when it has none).</summary>
    public string Draft => Current?.Composer?.Field.Text ?? "";

    /// <summary>Puts <paramref name="text"/> in the shown body's reply field (the mirror follows its leader's draft).</summary>
    public void SetDraft(string text)
    {
        if (Current is { } body && body.Composer?.Field.Text != text) Restore(body, (text, text.Length, text.Length));
    }

    /// <summary>The asks that still wait: the drafts of every other ask are dropped.</summary>
    public void Forget(IEnumerable<string> waiting)
    {
        var keep = waiting.ToHashSet(StringComparer.Ordinal);
        foreach (var id in drafts.Keys.Where(id => !keep.Contains(id)).ToList()) drafts.Remove(id);
    }

    /// <summary>The body's height at its final layout (DIP), capped by <see cref="BodyCap"/>.</summary>
    public double BodyHeight => Current is null ? 0 : Math.Min(Current.DesiredSize.Height, BodyCap);

    /// <summary>The most the body may take: the card's height cap less the header.</summary>
    public double BodyCap { get; private set; } = PillMorph.MaxCardHeight - PillLayout.Height;

    private double NowMs => clock() * 1000;

    /// <summary>
    /// Shows <paramref name="model"/>: in place (<paramref name="direction"/> 0: the first body, or a new revision of the same
    /// ask), or sliding in from the right (1, the next) or the left (-1, the previous). <paramref name="cap"/> is the card's
    /// height cap for this work area. The body is laid out once at its final size here; the shield starts again.
    /// </summary>
    public void Show(PillCardBody model, int direction, double cap, PillVector? pointerAt = null, bool reduce = false)
    {
        var now = clock();
        if (Current is { } shown && shown.Model == model) return;
        pointer = pointerAt ?? pointer;
        BodyCap = Math.Max(0, cap - PillLayout.Height);
        var body = Make(model);
        Keep(Current);
        if (drafts.TryGetValue(model.AskId, out var draft)) Restore(body, draft);
        body.Pressed += Pressed;
        body.Relaid += () => Relayout(body);
        if (body.Composer is { } composer)
        {
            composer.KeyPressed += e => ComposerKey(body, composer, e);
            composer.Field.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && ReferenceEquals(Current, body)) DraftChanged?.Invoke(model.AskId, composer.Field.Text ?? "");
            };
        }
        var old = Current;
        Current = body;
        Children.Add(body);
        Fit(body);
        if (old is not null && direction != 0)
        {
            Slide(old, body, direction, now, reduce);
        }
        else if (old is not null)
        {
            Children.Remove(old);
            animator.SetTranslation(body, 0, 0);
        }
        else
        {
            animator.SetTranslation(body, 0, 0);
        }
        Guard(body, now, newBody: true);
        HeightChanged?.Invoke();
    }

    /// <summary>
    /// The overlay did not come to the front after the reply field's click: the card says "Type your answer in the chat"
    /// with Open chat; the draft goes with it.
    /// </summary>
    public void KeyboardRefused()
    {
        if (Current is not { } body || body.Model is PillTypeInChatBody) return;
        Show(new PillTypeInChatBody(body.Model.AskId, body.Model.Revision, Draft), 0, BodyCap + PillLayout.Height);
    }

    /// <summary>The card folded into the pill: its bodies go once the collapse is over.</summary>
    public void Clear(double afterSeconds)
    {
        var body = Current;
        Keep(body);
        Current = null;
        if (body is null) return;
        after(afterSeconds, () =>
        {
            if (!ReferenceEquals(Current, body)) Children.Remove(body);
        });
    }

    /// <summary>The pointer as the card sees it, in the card's DIP (null: away). Set before a body shows so controls under it start inert.</summary>
    public void Moved(Point? at)
    {
        pointer = at is { } p ? new PillVector(p.X, p.Y) : null;
        var before = Current?.Controls.Where(c => Shield.IsInert(c.Id)).Select(c => c.Id).ToList() ?? [];
        Shield.Moved(NowMs, pointer);
        if (Current is null) return;
        foreach (var control in Current.Controls)
            if (before.Contains(control.Id) && !Shield.IsInert(control.Id)) animator.SetOpacity(control.Button, 1);
    }

    /// <summary>The controls the shield and the placement see, in the card's DIP.</summary>
    public IReadOnlyList<CardControl> Layout()
    {
        if (Current is not { } body) return [];
        var list = new List<CardControl>();
        foreach (var control in body.Controls)
        {
            // shown within the card: the input band hides its pill's content while the mirror draws it moving, and the dodge
            // reads the layout just as the card starts to open
            if (!Shown(control.Button)) continue;
            if (control.Button.TranslatePoint(new Point(control.Button.HitInset, 0), this) is not { } at) continue;
            list.Add(new CardControl(control.Id, new PillRect(at.X, at.Y, control.Button.Bounds.Width - control.Button.HitInset, control.Button.Bounds.Height), control.Kind));
        }
        return list;
    }

    /// <summary>Marks controls the placement could not clear (they wait until the pointer leaves them once).</summary>
    public void MarkInert(IReadOnlyList<string> ids)
    {
        Shield.MarkInert(ids);
        if (Current is null) return;
        foreach (var control in Current.Controls)
            if (ids.Contains(control.Id)) animator.SetOpacity(control.Button, CardShield.ShieldFrom);
    }

    // ---- drafts ----

    // The draft of a body that leaves is kept for its ask (an empty field drops it).
    private void Keep(CardBody? body)
    {
        if (body?.Composer is not { } composer || body.Model is PillTypeInChatBody or PillWithdrawnBody) return;
        if (composer.HasText) drafts[body.Model.AskId] = (composer.Field.Text ?? "", composer.Field.SelectionStart, composer.Field.SelectionEnd);
        else drafts.Remove(body.Model.AskId);
    }

    // A body for an ask with a draft opens its reply field with the text, the caret and the selection where they were.
    private static void Restore(CardBody body, (string Text, int Start, int End) draft)
    {
        if (body.Composer is not { } composer) return;
        if (!composer.IsVisible) body.Act(ApprovalCardBody.ReplyId);
        composer.Field.Text = draft.Text;
        composer.Field.SelectionStart = Math.Clamp(draft.Start, 0, draft.Text.Length);
        composer.Field.SelectionEnd = Math.Clamp(draft.End, 0, draft.Text.Length);
    }

    // ---- bodies ----

    private static CardBody Make(PillCardBody model) => model switch
    {
        PillApprovalBody approval => new ApprovalCardBody(approval),
        PillConfirmBody confirm => new ConfirmCardBody(confirm),
        PillShowBody show => new ShowWindowCardBody(show),
        PillQuestionBody { Secret: true } secret => new ProviderPointerBody(secret),
        PillQuestionBody question => new QuestionCardBody(question),
        PillProviderBody provider => new ProviderPointerBody(provider),
        PillWithdrawnBody withdrawn => new WithdrawnCardBody(withdrawn),
        PillTypeInChatBody chat => new ProviderPointerBody(chat),
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    // Laid out once at the final size: the scrolling part takes what the cap leaves after the part that never scrolls.
    private void Fit(CardBody body)
    {
        body.Below.Measure(new Size(PillMorph.CardWidth, double.PositiveInfinity));
        body.ScrollCap = BodyCap - body.Below.DesiredSize.Height;
        body.Measure(new Size(PillMorph.CardWidth, double.PositiveInfinity));
        SetLeft(body, 0);
        SetTop(body, 0);
        // arranged here as the canvas would, so its layout is ready while a band hides the content (the dodge reads it)
        body.Arrange(new Rect(body.DesiredSize));
        Height = Math.Min(body.DesiredSize.Height, BodyCap);
    }

    private bool Shown(Visual control)
    {
        for (Visual? at = control; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
        {
            if (!at.IsVisible) return false;
        }
        return true;
    }

    private void Relayout(CardBody body)
    {
        if (!ReferenceEquals(body, Current)) return;
        Fit(body);
        UpdateLayout();
        // A height change is a new layout under a resting pointer: the shield and the inert rule start again, armed stays.
        Guard(body, clock(), newBody: false);
        HeightChanged?.Invoke();
    }

    private void Slide(CardBody old, CardBody body, int direction, double now, bool reduce)
    {
        Leaving = old;
        old.IsHitTestVisible = false;
        if (reduce)
        {
            Children.Remove(old);
            Leaving = null;
            animator.SetTranslation(body, 0, 0);
            animator.Fade(body, PillTrack.Of(now, now + PillCurve.ReducedFade, t => Math.Clamp((t - now) / PillCurve.ReducedFade, 0, 1)));
            return;
        }
        var width = PillMorph.CardWidth;
        var inCurve = PillCurve.Spring(direction * width, 0, 0, PageResponse, now);
        var outCurve = PillCurve.Spring(0, -direction * width, 0, PageResponse, now);
        var end = End(inCurve, now);
        animator.Translate(body, PillTrack.Of(now, end, t => inCurve.Done(t) ? 0 : inCurve.Value(t)), PillTrack.Of(now, end, _ => 0));
        animator.Translate(old, PillTrack.Of(now, end, t => outCurve.Done(t) ? outCurve.To : outCurve.Value(t)), PillTrack.Of(now, end, _ => 0));
        after(end - now + ControlPill.HideMargin, () =>
        {
            Children.Remove(old);
            if (ReferenceEquals(Leaving, old)) Leaving = null;
        });
    }

    private static double End(PillCurve curve, double now)
    {
        var end = now;
        while (!curve.Done(end) && end < now + PillMotion.LongestSeconds) end += PillTrack.Step;
        return end;
    }

    // The shield starts again with this layout: answers rise from 35 % to full over 400 ms; inert ones wait at 35 %.
    private void Guard(CardBody body, double now, bool newBody)
    {
        UpdateLayout();
        Shield.Show(now * 1000, Layout(), pointer, newBody);
        foreach (var control in body.Controls)
        {
            if (control.Kind == CardControlKind.Free) continue;
            if (Shield.IsInert(control.Id))
            {
                animator.SetOpacity(control.Button, CardShield.ShieldFrom);
                continue;
            }
            var id = control.Id;
            animator.Fade(control.Button, PillTrack.Of(now, now + (CardShield.ShieldMs / 1000), t => Shield.Opacity(t * 1000, id)));
        }
        if (newBody) Disarmed(body);
    }

    // ---- presses ----

    private void Pressed(CardBodyControl control)
    {
        if (Current is not { } body || !body.Controls.Contains(control)) return;
        Counted(body, control, Shield.Click(NowMs, control.Id, control.Button.LastKind, control.Button.LastClickCount));
    }

    private void Counted(CardBody body, CardBodyControl control, CardClick click)
    {
        switch (click)
        {
            case CardClick.Fires:
                if (control.Kind == CardControlKind.Always) Disarmed(body);
                if (body.Act(control.Id) is { } answer) Answered?.Invoke(answer);
                else if (control.Id == ApprovalCardBody.ReplyId || control.Id == ConfirmCardBody.ReplyId) body.Opened();
                break;
            case CardClick.NotRested:
                Flash(body, CardShield.RestHint);
                break;
            case CardClick.TooSoon:
                Flash(body, CardShield.AgainHint);
                break;
            case CardClick.Armed:
                Armed(body, control);
                break;
        }
    }

    private void Armed(CardBody body, CardBodyControl control)
    {
        if (body is not ApprovalCardBody approval) return;
        var generation = ++armGeneration;
        approval.Arm(true, clock(), animator);
        after(ArmedSeconds, () =>
        {
            if (generation != armGeneration || !ReferenceEquals(Current, body)) return;
            if (!Shield.IsArmed(NowMs, control.Id)) approval.Arm(false, clock(), animator);
        });
    }

    private void Disarmed(CardBody body)
    {
        armGeneration++;
        if (body is ApprovalCardBody approval && approval.IsArmed) approval.Arm(false, clock(), animator);
    }

    private void Flash(CardBody body, string? message) =>
        body.Footer?.Flash(message, after, (target, x, y) => animator.Translate(target, x, y), clock());

    // ---- keys (CardShield.Key) ----

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Current is not { } body) return;
        var key = e.Key switch
        {
            Key.Enter => CardKey.Enter,
            Key.Space => CardKey.Space,
            Key.Escape => CardKey.Escape,
            _ => CardKey.Other,
        };
        if (key == CardKey.Other) return;
        var composer = body.Composer;
        var inComposer = composer?.Focused == true;
        if (inComposer) return; // the field reports its own keys (ComposerKey) first
        var repeat = key == CardKey.Space && spaceDown;
        if (key == CardKey.Space) spaceDown = true;
        var focused = Focused(body);
        var result = Shield.Key(NowMs, key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), repeat, focused?.Id, false, false, false);
        Acted(body, result, focused, null);
        e.Handled = true;
    }

    private void ComposerKey(CardBody body, MiniComposer composer, KeyEventArgs e)
    {
        if (!ReferenceEquals(body, Current)) return;
        var key = e.Key == Key.Escape ? CardKey.Escape : CardKey.Enter;
        var result = Shield.Key(NowMs, key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), false, null, true, composer.Composing, composer.HasText);
        // An open IME composition keeps its keys: Enter commits it and Esc cancels it, in the IME.
        if (result.Action is CardKeyAction.CommitComposition or CardKeyAction.CancelComposition) return;
        Acted(body, result, null, composer);
        e.Handled = true;
    }

    private void Acted(CardBody body, CardKeyResult result, CardBodyControl? focused, MiniComposer? composer)
    {
        switch (result.Action)
        {
            case CardKeyAction.FlashEnterHint:
                Flash(body, CardShield.EnterHint);
                break;
            case CardKeyAction.SendText:
                if (body.Act(MiniComposer.SendId) is { } answer) Answered?.Invoke(answer);
                break;
            case CardKeyAction.NewLine:
                composer?.NewLine();
                break;
            case CardKeyAction.Fold:
                Command?.Invoke(PillCardCommand.Fold);
                break;
            case CardKeyAction.Press when focused is not null:
                Counted(body, focused, result.Click);
                break;
        }
    }

    private static CardBodyControl? Focused(CardBody body)
    {
        var focused = TopLevel.GetTopLevel(body)?.FocusManager?.GetFocusedElement();
        return body.Controls.FirstOrDefault(c => ReferenceEquals(c.Button, focused));
    }
}
