// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-18; DESIGN.md 2.6, 2.7, 2.8). Rung R4, the borrow
// of the real pointer or focus, the last rung of the ladder. It offers a way only while "Allow real mouse and keyboard
// when needed" is on and the NP-4 probe proved an activation and a restore way for the app; otherwise it answers
// needs_real_input with its reason. Clicks, wheels and drags are pointer steps (no activation call: the click activates
// the window as a user's click does); keys and text are focus steps (the proven activation, then UI Automation focus on
// the control, checked before any key goes out). Text goes in borrows of 64 characters at most, so the user's input is
// never held longer than one blink; between two borrows the engine's boundary check runs. Each borrow is one call to the
// shared RealInputLease, which waits for the gate.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Port.PcControl;

public sealed class PcBorrowRung(Func<PcSettings> settings, RealInputLease? lease = null, IUiaReader? reader = null) : IPcInputRung
{
    /// <summary>A page scrolled with the wheel: 5 notches (15 lines at Windows' default of 3 lines a notch).</summary>
    public const int PageNotches = 5;

    /// <summary>Lines a wheel notch scrolls by Windows' default.</summary>
    public const int LinesPerNotch = 3;

    private const ushort Control = 0x11;
    private const ushort A = 0x41;

    public PcRung Rung => PcRung.R4;

    public PcRungOffer Offer(PcInputRequest request, PcFramework framework)
    {
        if (!settings().RealInput) return PcRungOffer.Refuse(PcErrorCode.NeedsRealInput, PcRealInputReason.Off);
        if (request.Kind == PcInputKind.Command) return PcRungOffer.None;
        var kind = request.Kind is PcInputKind.Click or PcInputKind.Scroll or PcInputKind.Drag ? PcBorrowKind.Pointer : PcBorrowKind.Focus;
        if (lease is null) return PcRungOffer.Refuse(PcErrorCode.NeedsRealInput, PcRealInputReason.ActivationUnproven);
        if (lease.Unproven(request.Window.App.ExeName, kind) is { } reason) return PcRungOffer.Refuse(PcErrorCode.NeedsRealInput, reason);
        return request.Kind switch
        {
            PcInputKind.Click => Click(request, lease),
            PcInputKind.Scroll => Scroll(request, lease),
            PcInputKind.Drag => Drag(request, lease),
            PcInputKind.Type => Type(request, lease),
            PcInputKind.Key => Keys(request, lease),
            _ => PcRungOffer.None,
        };
    }

    /// <summary>The borrow's answer as the ladder's outcome.</summary>
    public static PcUiaOutcome Outcome(PcBorrowOutcome outcome) => outcome.Status switch
    {
        PcBorrowStatus.Done => new PcUiaOutcome(null, null),
        PcBorrowStatus.Deferred => new PcUiaOutcome(PcErrorCode.DeferredByUser, outcome.Reason),
        PcBorrowStatus.NeedsRealInput => new PcUiaOutcome(PcErrorCode.NeedsRealInput, outcome.Reason),
        PcBorrowStatus.NotSent => new PcUiaOutcome(outcome.Code ?? PcErrorCode.NeedsRealInput, outcome.Reason),
        _ => new PcUiaOutcome(outcome.Code ?? PcErrorCode.NotSupported, outcome.Reason),
    };

    /// <summary>Wheel notches for a scroll amount: lines by Windows' 3 a notch, pages by 5 notches; at least one.</summary>
    public static int Notches(PcScrollUnit unit, double amount) => Math.Max(1, unit == PcScrollUnit.Page
        ? (int)Math.Ceiling(amount * PageNotches)
        : (int)Math.Ceiling(amount / LinesPerNotch));

    /// <summary>A drag's arrays: down at the first point, one move each later point, then the up; and the gaps between.</summary>
    public static (List<IReadOnlyList<PcInputEvent>> Arrays, List<int> Gaps) DragArrays(IReadOnlyList<PcPoint> path, IReadOnlyList<ushort> modifiers, int holdMs,
        PcRect desktop, Func<ushort, ushort> scan)
    {
        var arrays = new List<IReadOnlyList<PcInputEvent>>
        {
            new List<PcInputEvent>([PcInputPlan.MoveTo(path[0], desktop), .. PcInputPlan.ModifiersDown(modifiers, scan), PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTDOWN)]),
        };
        var gaps = new List<int>();
        for (var i = 1; i < path.Count; i++)
        {
            arrays.Add([PcInputPlan.MoveTo(path[i], desktop)]);
            gaps.Add(i == 1 ? Math.Max(holdMs, InputInjector.DragStepMs) : InputInjector.DragStepMs);
        }
        arrays.Add([PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTUP), .. PcInputPlan.ModifiersUp(modifiers, scan)]);
        gaps.Add(InputInjector.DragStepMs);
        return (arrays, gaps);
    }

    /// <summary>Text cut into pieces of at most 64 characters, never between the two halves of a surrogate pair.</summary>
    public static List<string> Pieces(string text)
    {
        var pieces = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(PcInputPlan.TextChunk, text.Length - start);
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
            pieces.Add(text.Substring(start, length));
            start += length;
        }
        return pieces;
    }

    private bool RealInputAllowed()
    {
        var live = settings();
        return live.Enabled && live.RealInput;
    }

    private static string App(PcInputRequest request) => Path.GetFileNameWithoutExtension(request.Window.App.ExePath);

    private static PcPoint? PointOf(PcInputRequest request)
    {
        if (request.Point is { } point) return point;
        if (request.Element is { Bounds.IsEmpty: false } element) return new PcPoint(element.Bounds.X + (element.Bounds.Width / 2), element.Bounds.Y + (element.Bounds.Height / 2));
        return null;
    }

    private static PcBorrowRequest Base(PcInputRequest request, PcBorrowKind kind, string detail) =>
        new(request.Window.App.Key, kind, request.Window.Handle, request.Window.App.ExeName)
        {
            Detail = detail,
            Words = PcBusyWords.Words(request),
            Waiting = request.Waiting,
        };

    private static PcRungOffer One(string method, string detail, bool writes, Func<CancellationToken, Task<PcUiaOutcome>> run) =>
        new([new PcRungPlan(PcRung.R4, method, run) { Detail = detail, Writes = writes }]);

    private PcRungOffer Click(PcInputRequest request, RealInputLease lease)
    {
        if (PointOf(request) is not { } point) return PcRungOffer.None;
        var what = request.Element is { } element ? PcVerify.Describe(element) : "the point";
        return One("borrow_click", "clicked " + what + " with the real mouse", false, async token =>
        {
            var host = lease.Host;
            var click = PcInputPlan.Click(point, host.Desktop(), request.Button, request.Count, request.Modifiers, host.ScanCode);
            var borrow = Base(request, PcBorrowKind.Pointer, (request.Count == 2 ? "a double-click in " : "one click in ") + App(request)) with
            {
                Chunks = [click],
                Point = point,
            };
            return Outcome(await lease.Borrow(borrow, token, RealInputAllowed).ConfigureAwait(false));
        });
    }

    private PcRungOffer Scroll(PcInputRequest request, RealInputLease lease)
    {
        if (PointOf(request) is not { } point) return PcRungOffer.None;
        return One("borrow_scroll", "scrolled with the real mouse wheel", false, async token =>
        {
            var host = lease.Host;
            var wheel = PcInputPlan.Wheel(point, host.Desktop(), request.Direction, Notches(request.Unit, request.Amount));
            var borrow = Base(request, PcBorrowKind.Pointer, "the wheel in " + App(request)) with { Chunks = [wheel], Point = point };
            return Outcome(await lease.Borrow(borrow, token, RealInputAllowed).ConfigureAwait(false));
        });
    }

    private PcRungOffer Drag(PcInputRequest request, RealInputLease lease)
    {
        if (request.Path.Count < 2) return PcRungOffer.None;
        return One("borrow_drag", "dragged with the real mouse", false, async token =>
        {
            var host = lease.Host;
            var (arrays, gaps) = DragArrays(request.Path, request.Modifiers, request.HoldMs, host.Desktop(), host.ScanCode);
            // The whole drag holds the user's input: it must fit the drag cap with room for the restore.
            if (gaps.Sum() + RealInputLease.WatchdogSlackMs > RealInputLease.DragCapMs)
            {
                return new PcUiaOutcome(PcErrorCode.NotSupported, "a drag this long cannot borrow the mouse (" + RealInputLease.DragCapMs
                    + " ms at most, " + InputInjector.DragStepMs + " ms a point): use fewer points or a shorter hold");
            }
            var borrow = Base(request, PcBorrowKind.Pointer, "a drag in " + App(request)) with
            {
                Chunks = arrays,
                GapsMs = gaps,
                Point = request.Path[0],
                HoldCapMs = RealInputLease.DragCapMs,
            };
            return Outcome(await lease.Borrow(borrow, token, RealInputAllowed).ConfigureAwait(false));
        });
    }

    private PcRungOffer Type(PcInputRequest request, RealInputLease lease)
    {
        if (request.Text is not { } text) return PcRungOffer.None;
        var element = request.Element;
        var what = element is null ? "the focused field" : PcVerify.Describe(element);
        return One("borrow_type", "typed into " + what + " with the real keyboard", true, async token =>
        {
            var host = lease.Host;
            var pieces = Pieces(text);
            var done = 0;
            for (var i = 0; i < pieces.Count; i++)
            {
                if (i > 0 && request.Boundary is { } boundary && await boundary(done, text.Length, token).ConfigureAwait(false) is { } changed)
                    return new PcUiaOutcome(PcErrorCode.ExpectFailed, changed);
                var keys = PcInputPlan.Text(pieces[i], host.ScanCode).SelectMany(c => c).ToList();
                // Replace mode selects the field's text first (Ctrl+A is not a clipboard command).
                if (i == 0 && !request.Insert) keys.InsertRange(0, PcInputPlan.Chord(new KeyChord([Control], A), 1, host.ScanCode));
                var borrow = Base(request, PcBorrowKind.Focus, "typing in " + App(request)) with
                {
                    Chunks = [keys],
                    Focus = Focuser(request),
                    FocusControl = element?.NativeWindow ?? 0,
                };
                var outcome = await lease.Borrow(borrow, token, RealInputAllowed).ConfigureAwait(false);
                if (!outcome.Done) return Outcome(outcome);
                done += pieces[i].Length;
            }
            return new PcUiaOutcome(null, null);
        });
    }

    private PcRungOffer Keys(PcInputRequest request, RealInputLease lease)
    {
        if (request.Chord is not { } chord) return PcRungOffer.None;
        var keys = request.KeysText ?? "the keys";
        return One("borrow_keys", "pressed " + keys + " with the real keyboard", request.Element is null, async token =>
        {
            var host = lease.Host;
            var borrow = Base(request, PcBorrowKind.Focus, "keys in " + App(request)) with
            {
                Chunks = [PcInputPlan.Chord(chord, request.Repeat, host.ScanCode)],
                Focus = Focuser(request),
                FocusControl = request.Element?.NativeWindow ?? 0,
            };
            return Outcome(await lease.Borrow(borrow, token, RealInputAllowed).ConfigureAwait(false));
        });
    }

    // UI Automation focus on the step's element (the keys' target); null when the keys go to the app's own focus.
    private Func<CancellationToken, Task<bool>>? Focuser(PcInputRequest request)
    {
        if (request.Element is not { } element || reader is null) return null;
        var window = request.Window.Handle;
        return async token => (await reader.Perform(window, element.RuntimeId, new PcElementCommand(PcElementAction.Focus), token).ConfigureAwait(false)).Done;
    }
}
