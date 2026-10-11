// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.3, 2.6,
// 2.8, critique C6, C7, C31). The rungs. R1 runs the element's own UI Automation pattern in the target process (no
// pointer, no keyboard, no focus). R2 sends one message to one child window: BM_CLICK on a stock button, EM_REPLACESEL into
// a stock edit (insert at the caret, undoable), a WM_CHAR stream (Unicode windows only, never while a modifier is held),
// a plain posted key, posted mouse messages at a point inside the window, and a menu command after the menu-state check.
// R4 (borrowing the real pointer or focus) is not built yet; it answers needs_real_input with its reason. Shortcuts become
// commands first (the element whose AcceleratorKey or AccessKey matches, Alt+F4 as Close, Enter and Esc on a dialog as its
// OK and Cancel buttons); a chord is never posted, because apps read the real modifier state.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>A Win32 menu item: its command id, its text (with the "\t" shortcut part) and whether it is disabled or greyed.</summary>
public sealed record PcMenuItem(int Id, string Text, bool Disabled);

/// <summary>
/// Messages to one child window (rung R2). Sent messages use SendMessageTimeout with SMTO_ABORTIFHUNG; posted ones never
/// wait. Nothing here changes the foreground, the focus or the real cursor. Never used by automated tests.
/// </summary>
public interface IPcMessageSink
{
    /// <summary>The control that has focus inside the window's thread (GetGUIThreadInfo); 0 when none.</summary>
    nint FocusControl(nint window);

    /// <summary>The deepest child window at a screen point inside the window (ChildWindowFromPointEx); the window itself when none.</summary>
    nint ChildAt(nint window, PcPoint screen);

    bool IsUnicode(nint control);

    bool ButtonClick(nint control);

    /// <summary>EM_REPLACESEL at the caret, or after EM_SETSEL(0, -1) when <paramref name="all"/>; undoable.</summary>
    bool ReplaceSelection(nint control, string text, bool all);

    /// <summary>WM_SETTEXT.</summary>
    bool SetText(nint control, string text);

    /// <summary>A WM_CHAR stream (a surrogate pair as two messages).</summary>
    bool Chars(nint control, string text);

    bool Key(nint control, ushort virtualKey, bool extended, int repeat);

    /// <summary>WM_MOUSEMOVE then button down and up (count times) at a screen point, posted to the control.</summary>
    bool Mouse(nint control, PcPoint screen, PcMouseButton button, int count);

    /// <summary>Button down at the first point, moves along the path, up at the last, posted to the control.</summary>
    bool Drag(nint control, IReadOnlyList<PcPoint> path);

    /// <summary>The window's menu item whose text ends in "\t" and the chord; null when none.</summary>
    PcMenuItem? MenuItem(nint window, KeyChord chord);

    bool Command(nint window, int id);

    /// <summary>ShowWindow with SW_SHOWNOACTIVATE (restore) or SW_SHOWMINNOACTIVE (minimize): no activation.</summary>
    bool ShowNoActivate(nint window, bool minimize);
}

/// <summary>Rung R1: the element's own UI Automation pattern.</summary>
public sealed class PcUiaRung(IUiaReader reader) : IPcInputRung
{
    public PcRung Rung => PcRung.R1;

    public PcRungOffer Offer(PcInputRequest request, PcFramework framework)
    {
        var element = request.Element;
        switch (request.Kind)
        {
            case PcInputKind.Click:
                if (element is null) return PcRungOffer.None;
                if (element.Has(PcStates.Disabled)) return PcRungOffer.Refuse(PcErrorCode.Disabled, PcVerify.Describe(element) + " is disabled");
                if (request.Button != PcMouseButton.Left || request.Modifiers.Count > 0) return PcRungOffer.None;
                if (request.Count >= 2) return element.Can(PcActions.Invoke) ? One(request, element, new PcElementCommand(PcElementAction.Invoke), "invoke") : PcRungOffer.None;
                if (element.Can(PcActions.Invoke)) return One(request, element, new PcElementCommand(PcElementAction.Invoke), "invoke");
                if (element.Can(PcActions.Toggle)) return One(request, element, new PcElementCommand(PcElementAction.Toggle), "toggle");
                if (element.Can(PcActions.Select) && !element.Has(PcStates.Selected)) return One(request, element, new PcElementCommand(PcElementAction.Select), "select");
                if (element.Can(PcActions.ExpandCollapse))
                    return One(request, element, new PcElementCommand(element.Has(PcStates.Expanded) ? PcElementAction.Collapse : PcElementAction.Expand), "expand");
                if (element.Can(PcActions.Select)) return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " is already selected");
                if (PcElementMatch.Editable(element))
                    return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " needs no click: use pc_type or pc_set_value on it");
                return PcRungOffer.None;
            case PcInputKind.Type:
                if (element is null) return PcRungOffer.None;
                if (element.Has(PcStates.Password)) return PcRungOffer.Refuse(PcErrorCode.PasswordField, "this is a password field");
                if (element.Has(PcStates.Disabled)) return PcRungOffer.Refuse(PcErrorCode.Disabled, PcVerify.Describe(element) + " is disabled");
                if (!element.Can(PcActions.Value)) return PcRungOffer.None;
                if (element.Has(PcStates.ReadOnly)) return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " is read-only");
                // The caret is not read here, so insert adds the text at the end (DESIGN.md 2.8 step 1; ED-NEW-pcctl-8).
                var value = request.Insert ? (element.Value ?? "") + (request.Text ?? "") : request.Text ?? "";
                var typed = One(request, element, new PcElementCommand(PcElementAction.SetValue) { Value = value }, "set_value");
                return new PcRungOffer([typed.Plans[0] with { Writes = true, Detail = "typed into " + PcVerify.Describe(element) }]);
            case PcInputKind.Key:
                return Shortcut(request);
            case PcInputKind.Scroll:
                if (element is null || !element.Can(PcActions.Scroll)) return PcRungOffer.None;
                return One(request, element, new PcElementCommand(PcElementAction.Scroll)
                {
                    Direction = request.Direction,
                    Unit = request.Unit,
                    Count = Math.Max(1, (int)Math.Ceiling(request.Amount)),
                }, "scroll");
            case PcInputKind.Command:
                return Command(request, element);
            default:
                return PcRungOffer.None;
        }
    }

    private PcRungOffer Command(PcInputRequest request, PcUiNode? element)
    {
        if (element is null || request.Command is not { } command) return PcRungOffer.None;
        if (element.Has(PcStates.Disabled)) return PcRungOffer.Refuse(PcErrorCode.Disabled, PcVerify.Describe(element) + " is disabled");
        switch (command.Action)
        {
            case PcElementAction.SetValue:
                if (element.Has(PcStates.Password)) return PcRungOffer.Refuse(PcErrorCode.PasswordField, "this is a password field");
                if (!element.Can(PcActions.Value) && !element.Can(PcActions.RangeValue)) return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " has no value to set");
                if (element.Has(PcStates.ReadOnly)) return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " is read-only");
                var set = One(request, element, command, "set_value");
                return new PcRungOffer([set.Plans[0] with { Writes = true }]);
            case PcElementAction.Toggle when element.Can(PcActions.Toggle):
            case PcElementAction.Select when element.Can(PcActions.Select):
            case PcElementAction.Expand or PcElementAction.Collapse when element.Can(PcActions.ExpandCollapse):
            case PcElementAction.ScrollIntoView when element.Can(PcActions.ScrollIntoView):
            case PcElementAction.Close when element.Can(PcActions.Window):
                return One(request, element, command, Word(command.Action));
            case PcElementAction.Maximize when element.Can(PcActions.Window):
            case PcElementAction.Focus when element.Can(PcActions.Focus):
                // WindowPattern maximize and UIA SetFocus can activate a window that is behind: they run only while the
                // window is already in front and the user has not worked in it for 2 s (ED-NEW-pcctl-8). Anywhere else they
                // would take the user's foreground, so they are left to a borrow (R4).
                return request.InFront && !request.UserActive ? One(request, element, command, Word(command.Action)) : PcRungOffer.None;
            case PcElementAction.Minimize or PcElementAction.Restore:
                return PcRungOffer.None;
            default:
                return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " has no " + Word(command.Action) + " action");
        }
    }

    private static string Word(PcElementAction action) => action switch
    {
        PcElementAction.ScrollIntoView => "scroll_into_view",
        _ => action.ToString().ToLowerInvariant(),
    };

    // A shortcut becomes the command that shows it, or a known pattern (DESIGN.md 2.8 shortcut ladder steps 1 and 3).
    private PcRungOffer Shortcut(PcInputRequest request)
    {
        if (request.Chord is not { } chord || request.Tree is not { } tree) return PcRungOffer.None;
        var root = tree.Elements.TryGetValue(1, out var window) ? window : null;
        if (chord.Key == 0x73 && chord.Modifiers.Count == 1 && chord.Modifiers[0] == KeyChordParser.Alt)
            return root is not null && root.Can(PcActions.Window) ? One(request, root, new PcElementCommand(PcElementAction.Close), "close") : PcRungOffer.None;
        var matches = tree.Lines.Select(l => tree.Elements[l.Index])
            .Where(n => !n.Has(PcStates.Disabled) && (n.Can(PcActions.Invoke) || n.Can(PcActions.Toggle) || n.Can(PcActions.ExpandCollapse))
                && (Same(n.Shortcut, chord) || Same(n.AccessKey, chord)))
            .ToList();
        if (matches.Count > 0)
        {
            // A menu item and a toolbar button often share one shortcut and one command: equal names are one command.
            if (matches.Select(m => PcElementMatch.Norm(m.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                return PcRungOffer.Refuse(PcErrorCode.AmbiguousTarget, "more than one command shows " + Show(request) + ": " + string.Join(", ", matches.Select(PcVerify.Describe)));
            var match = matches[0];
            var action = match.Can(PcActions.Invoke) ? PcElementAction.Invoke : match.Can(PcActions.Toggle) ? PcElementAction.Toggle : PcElementAction.Expand;
            return One(request, match, new PcElementCommand(action), "accelerator");
        }
        if (chord.Modifiers.Count == 0 && chord.Key is KeyChordParser.Return or KeyChordParser.Escape)
        {
            // Enter and Esc on a dialog: its OK (IDOK, automation id 1) or Cancel (IDCANCEL, 2) button, when there is exactly one.
            var id = chord.Key == KeyChordParser.Return ? "1" : "2";
            var buttons = tree.Elements.Values.Where(n => n.AutomationId == id && n.Can(PcActions.Invoke) && !n.Has(PcStates.Disabled)).ToList();
            if (buttons.Count == 1) return One(request, buttons[0], new PcElementCommand(PcElementAction.Invoke), "dialog_button");
        }
        return PcRungOffer.None;
    }

    private PcRungOffer One(PcInputRequest request, PcUiNode element, PcElementCommand command, string method)
    {
        var window = request.Window.Handle;
        var times = request.Kind == PcInputKind.Key ? Math.Max(1, request.Repeat) : 1;
        async Task<PcUiaOutcome> Run(CancellationToken cancellationToken)
        {
            var outcome = PcUiaOutcome.Ok;
            for (var i = 0; i < times && outcome.Done; i++)
                outcome = await reader.Perform(window, element.RuntimeId, command, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }
        return new PcRungOffer([new PcRungPlan(PcRung.R1, method, Run)
        {
            Command = command,
            Target = element,
            Detail = Verb(command) + " " + PcVerify.Describe(element),
            Key = new PcCapabilityKey(request.Window.ExeName, request.Window.Version, PcRung.R1, method, element.Role),
        }]);
    }

    private static bool Same(string? shown, KeyChord chord) =>
        shown is { Length: > 0 } && KeyChordParser.Parse(shown).Chord is { } parsed && parsed.Key == chord.Key && parsed.Modifiers.Order().SequenceEqual(chord.Modifiers.Order());

    private static string Show(PcInputRequest request) => PcWindowLine.Quote(request.KeysText ?? "the keys");

    private static string Verb(PcElementCommand command) => command.Action switch
    {
        PcElementAction.Invoke => "pressed",
        PcElementAction.Toggle => "toggled",
        PcElementAction.Select => "selected",
        PcElementAction.Expand => "expanded",
        PcElementAction.Collapse => "collapsed",
        PcElementAction.SetValue => "set the value of",
        PcElementAction.Scroll => "scrolled",
        PcElementAction.ScrollIntoView => "scrolled into view",
        PcElementAction.Close => "closed",
        _ => command.Action.ToString().ToLowerInvariant(),
    };
}

/// <summary>Rung R2: one message to one child window, where the framework gate allows it.</summary>
public sealed class PcMessageRung(IPcMessageSink sink) : IPcInputRung
{
    public PcRung Rung => PcRung.R2;

    /// <summary>Stock edit controls: EM_REPLACESEL inserts at their caret.</summary>
    public static bool IsEdit(string? className) => className is { } name
        && (name.Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase) || name.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".RichEdit", StringComparison.OrdinalIgnoreCase));

    /// <summary>Stock push buttons: BM_CLICK presses them.</summary>
    public static bool IsButton(string? className) => className is { } name
        && (name.Equals("Button", StringComparison.OrdinalIgnoreCase) || name.Contains(".BUTTON.", StringComparison.OrdinalIgnoreCase));

    public PcRungOffer Offer(PcInputRequest request, PcFramework framework)
    {
        var element = request.Element;
        var window = request.Window;
        // Text never goes into a password, disabled or read-only field by a message either.
        if (element is not null && (request.Kind == PcInputKind.Type || request.Command?.Action == PcElementAction.SetValue))
        {
            if (element.Has(PcStates.Password)) return PcRungOffer.Refuse(PcErrorCode.PasswordField, "this is a password field");
            if (element.Has(PcStates.Disabled)) return PcRungOffer.Refuse(PcErrorCode.Disabled, PcVerify.Describe(element) + " is disabled");
            if (element.Has(PcStates.ReadOnly)) return PcRungOffer.Refuse(PcErrorCode.NotSupported, PcVerify.Describe(element) + " is read-only");
        }
        switch (request.Kind)
        {
            case PcInputKind.Click:
            {
                var gate = PcFrameworkGate.Messages(framework, PcMessageKind.Click);
                if (gate == PcGate.No || request.Modifiers.Count > 0) return PcRungOffer.None;
                var plans = new List<PcRungPlan>();
                if (element is { NativeWindow: not 0 } button && IsButton(button.ClassName) && request.Button == PcMouseButton.Left && request.Count == 1)
                    plans.Add(Plan(request, "bm_click", gate, button, _ => Task.FromResult(Result(sink.ButtonClick(button.NativeWindow), "the button did not take the click")), "pressed " + PcVerify.Describe(button)));
                if (request.Point is { } point)
                {
                    plans.Add(Plan(request, "post_mouse", gate, element, _ =>
                    {
                        var child = sink.ChildAt(window.Handle, point);
                        return Task.FromResult(Result(child != 0 && sink.Mouse(child, point, request.Button, request.Count), "the window did not take the click"));
                    }, "clicked at the point"));
                }
                return new PcRungOffer(plans);
            }
            case PcInputKind.Type:
            {
                var gate = PcFrameworkGate.Messages(framework, PcMessageKind.Text);
                if (gate == PcGate.No) return PcRungOffer.None;
                var text = request.Text ?? "";
                var plans = new List<PcRungPlan>();
                if (element is { NativeWindow: not 0 } edit && IsEdit(edit.ClassName))
                {
                    var all = !request.Insert;
                    plans.Add(Plan(request, all ? "em_replaceall" : "em_replacesel", gate, edit,
                        _ => Task.FromResult(Result(sink.ReplaceSelection(edit.NativeWindow, text, all), "the field did not take the text")), "typed into " + PcVerify.Describe(edit)) with { Writes = true });
                }
                // WM_CHAR: insert only, Unicode windows only (Hindi and Urdu would break in an ANSI window), never while a
                // modifier key is held, and always a probe until it passed its self-check for this exe (critique C31).
                if (request.Insert && !request.ModifiersDown)
                {
                    var control = element?.NativeWindow is { } own && own != 0 ? own : element is null ? sink.FocusControl(window.Handle) : 0;
                    if (control != 0 && sink.IsUnicode(control))
                    {
                        plans.Add(Plan(request, "wm_char", PcGate.Probe, element, token => Chars(control, text, request.Boundary, token),
                            "typed " + PcArgsCount(text.Length) + " characters" + (element is null ? " into the window's focused control" : " into " + PcVerify.Describe(element))) with { Writes = true });
                    }
                }
                return new PcRungOffer(plans);
            }
            case PcInputKind.Key:
            {
                if (request.Chord is not { } chord) return PcRungOffer.None;
                if (chord.Modifiers.Count > 0)
                {
                    // A chord becomes the menu command that shows it, after the menu-state check (critique C7); never a posted chord.
                    var gate = PcFrameworkGate.Messages(framework, PcMessageKind.Click);
                    if (gate == PcGate.No || sink.MenuItem(window.Handle, chord) is not { } item) return PcRungOffer.None;
                    var label = MenuLabel(item.Text);
                    if (item.Disabled) return PcRungOffer.Refuse(PcErrorCode.Disabled, "the menu item " + PcWindowLine.Quote(label) + " is disabled");
                    return new PcRungOffer([Plan(request, "wm_command", gate, null, _ => Task.FromResult(Result(sink.Command(window.Handle, item.Id), "the window did not take the command")),
                        "ran the menu command " + PcWindowLine.Quote(label)) with { MenuText = label }]);
                }
                var keys = PcFrameworkGate.Messages(framework, PcMessageKind.Keys);
                if (keys == PcGate.No || request.ModifiersDown) return PcRungOffer.None;
                var target = element?.NativeWindow is { } own && own != 0 ? own : sink.FocusControl(window.Handle);
                if (target == 0) return PcRungOffer.None;
                return new PcRungOffer([Plan(request, "post_key", keys, element, _ => Task.FromResult(Result(sink.Key(target, chord.Key, KeyChordParser.IsExtended(chord.Key), Math.Max(1, request.Repeat)), "the window did not take the key")),
                    "sent " + PcWindowLine.Quote(request.KeysText ?? "the key") + (element is null ? " to the window's focused control" : " to " + PcVerify.Describe(element))) with { Writes = element is null }]);
            }
            case PcInputKind.Drag:
            {
                var gate = PcFrameworkGate.Messages(framework, PcMessageKind.Click);
                if (gate == PcGate.No || request.Path.Count < 2 || request.Modifiers.Count > 0) return PcRungOffer.None;
                return new PcRungOffer([Plan(request, "post_drag", gate, null, _ =>
                {
                    var child = sink.ChildAt(window.Handle, request.Path[0]);
                    return Task.FromResult(Result(child != 0 && sink.Drag(child, request.Path), "the window did not take the drag"));
                }, "dragged along the path")]);
            }
            case PcInputKind.Command when request.Command is { } command:
            {
                if (command.Action is PcElementAction.Restore or PcElementAction.Minimize)
                {
                    var minimize = command.Action == PcElementAction.Minimize;
                    return new PcRungOffer([new PcRungPlan(PcRung.R2, minimize ? "sw_showminnoactive" : "sw_shownoactivate",
                        _ => Task.FromResult(Result(sink.ShowNoActivate(window.Handle, minimize), "Windows did not change the window")))
                    {
                        Command = command,
                        Detail = (minimize ? "minimized" : "restored") + " the window without bringing it to the front",
                    }]);
                }
                if (command.Action == PcElementAction.SetValue && element is { NativeWindow: not 0 } field && IsEdit(field.ClassName)
                    && PcFrameworkGate.Messages(framework, PcMessageKind.Text) is var gate && gate != PcGate.No)
                {
                    var value = command.Value ?? "";
                    return new PcRungOffer([Plan(request, "em_replaceall", gate, field, _ => Task.FromResult(Result(sink.ReplaceSelection(field.NativeWindow, value, true), "the field did not take the text")),
                        "set the value of " + PcVerify.Describe(field)) with { Command = command, Writes = true }]);
                }
                return PcRungOffer.None;
            }
            default:
                return PcRungOffer.None;
        }
    }

    // "&Save\tCtrl+S" is shown as "Save".
    private static string MenuLabel(string text)
    {
        var tab = text.IndexOf('\t', StringComparison.Ordinal);
        return (tab >= 0 ? text[..tab] : text).Replace("&&", "\u0001", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal).Replace('\u0001', '&').Trim();
    }

    private static PcRungPlan Plan(PcInputRequest request, string method, PcGate gate, PcUiNode? target, Func<CancellationToken, Task<PcUiaOutcome>> run, string detail) =>
        new(PcRung.R2, method, run)
        {
            Target = target,
            Detail = detail,
            Probe = gate == PcGate.Probe,
            Key = new PcCapabilityKey(request.Window.ExeName, request.Window.Version, PcRung.R2, method, target?.Role ?? "window"),
        };

    private static PcUiaOutcome Result(bool done, string reason) => done ? PcUiaOutcome.Ok : PcUiaOutcome.Fail(PcErrorCode.NotSupported, reason);

    /// <summary>The longest WM_CHAR run, one atomic unit: a pause holds the typing only between runs (DESIGN.md 10.1).</summary>
    public const int CharRun = 64;

    // A WM_CHAR stream in runs of up to 64 characters (a surrogate pair is never split). Between runs the boundary may hold
    // the typing for a pause and end it when the field changed meanwhile.
    private async Task<PcUiaOutcome> Chars(nint control, string text, Func<int, int, CancellationToken, Task<string?>>? boundary, CancellationToken cancellationToken)
    {
        var at = 0;
        do
        {
            var length = Math.Min(CharRun, text.Length - at);
            if (at + length < text.Length && char.IsHighSurrogate(text[at + length - 1])) length--;
            if (!sink.Chars(control, text.Substring(at, length)))
                return Result(false, at == 0 ? "the window did not take the text" : "the window stopped taking the text after " + PcArgsCount(at) + " of " + PcArgsCount(text.Length) + " characters");
            at += length;
            if (at < text.Length && boundary is not null && await boundary(at, text.Length, cancellationToken).ConfigureAwait(false) is { } changed)
                return PcUiaOutcome.Fail(PcErrorCode.ExpectFailed, changed);
        }
        while (at < text.Length);
        return PcUiaOutcome.Ok;
    }

    private static string PcArgsCount(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Rung R4, the borrow of the real pointer or focus (DESIGN.md 2.7). It is built in unit P3 with the physical-input gate
/// (P2); until then every request that reaches it gets needs_real_input with its reason, and nothing is ever injected.
/// </summary>
public sealed class PcBorrowRung(Func<PcSettings> settings) : IPcInputRung
{
    public PcRung Rung => PcRung.R4;

    public PcRungOffer Offer(PcInputRequest request, PcFramework framework) =>
        PcRungOffer.Refuse(PcErrorCode.NeedsRealInput, settings().RealInput ? PcRealInputReason.ActivationUnproven : PcRealInputReason.Off);
}

/// <summary>The clipboard chords and commands Tactus refuses (DESIGN.md 2.8, critique C6): the clipboard is the user's.</summary>
public static class PcClipboard
{
    private const ushort C = 0x43;
    private const ushort V = 0x56;
    private const ushort X = 0x58;
    private const ushort Insert = 0x2D;

    public static bool IsClipboardChord(KeyChord chord)
    {
        var ctrl = chord.Modifiers.Count == 1 && chord.Modifiers[0] == KeyChordParser.Control;
        var shift = chord.Modifiers.Count == 1 && chord.Modifiers[0] == KeyChordParser.Shift;
        return (ctrl && chord.Key is C or V or X or Insert) || (shift && chord.Key is Insert or KeyChordParser.Delete);
    }

    /// <summary>A command whose name is Copy, Cut or Paste (a menu item or a button).</summary>
    public static bool IsClipboardCommand(string? name) =>
        PcElementMatch.Norm(name).ToLowerInvariant().TrimEnd('.') is "copy" or "cut" or "paste" or "paste special" or "&copy" or "cu&t" or "&paste";

    public const string Refusal = "copy, cut and paste use the user's clipboard; read text with pc_read and type it with pc_type or pc_set_value";
}
