// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1; DESIGN.md 2.2, 3.1). Element actions through UIA
// patterns: Invoke, Toggle, SelectionItem, ExpandCollapse, Value and RangeValue, Scroll, ScrollItem, Window and focus; and
// an element's whole text (TextPattern, else its value, else its children's names). Password and read-only fields refuse
// values here too, not only in the engine. Native calls run on UiaReader workers; managed resolver delegates exercise
// the same dispatch in fake-only tests. ED-NEW-pcctl-cx-9, DESIGN.md 10.2: check cancellation after resolution and
// immediately before each mutation, including each Scroll and RangeValue after its provider property reads.
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public static class UiaPatterns
{
    public const int ElementNotEnabled = unchecked((int)0x80040200);
    public const int ElementNotAvailable = unchecked((int)0x80040201);
    public const int NoClickablePoint = unchecked((int)0x80040202);
    public const int Timeout = unchecked((int)0x80131505);

    /// <summary>The outcome of a pattern call's HRESULT.</summary>
    public static PcUiaOutcome Outcome(int hresult, string what) => hresult switch
    {
        0 => PcUiaOutcome.Ok,
        ElementNotEnabled => PcUiaOutcome.Fail(PcErrorCode.Disabled, "the element is disabled"),
        ElementNotAvailable => PcUiaOutcome.Fail(PcErrorCode.StaleElement, "the element is gone"),
        Timeout => PcUiaOutcome.Fail(PcErrorCode.Timeout, "the app did not answer in time"),
        _ => PcUiaOutcome.Fail(PcErrorCode.NotSupported, what + " failed (0x" + hresult.ToString("x8", CultureInfo.InvariantCulture) + ")"),
    };

    internal static PcUiaOutcome Perform(IUIAutomationElement element, PcUiNode node, PcElementCommand command,
        CancellationToken cancellationToken) => Perform(id =>
        {
            var hresult = element.GetCurrentPattern(id, out var pattern);
            return (hresult, pattern);
        }, element.SetFocus, node, command, cancellationToken);

    // Production dispatch with managed resolver/focus delegates for fake-only provider tests.
    internal static PcUiaOutcome Perform(Func<int, (int HResult, object? Pattern)> resolve, Func<int> focus,
        PcUiNode node, PcElementCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (command.Action)
        {
            case PcElementAction.Focus:
                return Outcome(Mutate(focus, cancellationToken), "focus");
            case PcElementAction.Invoke:
                return With<IUIAutomationInvokePattern>(resolve, UiaIds.InvokePattern, "press", p => Mutate(() => p.Invoke(), cancellationToken), cancellationToken);
            case PcElementAction.Toggle:
                return With<IUIAutomationTogglePattern>(resolve, UiaIds.TogglePattern, "toggle", p => Mutate(() => p.Toggle(), cancellationToken), cancellationToken);
            case PcElementAction.Select:
                return With<IUIAutomationSelectionItemPattern>(resolve, UiaIds.SelectionItemPattern, "select", p => Mutate(() => p.Select(), cancellationToken), cancellationToken);
            case PcElementAction.Expand:
                return With<IUIAutomationExpandCollapsePattern>(resolve, UiaIds.ExpandCollapsePattern, "expand", p => Mutate(() => p.Expand(), cancellationToken), cancellationToken);
            case PcElementAction.Collapse:
                return With<IUIAutomationExpandCollapsePattern>(resolve, UiaIds.ExpandCollapsePattern, "collapse", p => Mutate(() => p.Collapse(), cancellationToken), cancellationToken);
            case PcElementAction.ScrollIntoView:
                return With<IUIAutomationScrollItemPattern>(resolve, UiaIds.ScrollItemPattern, "scroll into view", p => Mutate(() => p.ScrollIntoView(), cancellationToken), cancellationToken);
            case PcElementAction.Minimize:
                return With<IUIAutomationWindowPattern>(resolve, UiaIds.WindowPattern, "minimize", p => Mutate(() => p.SetWindowVisualState(UiaIds.VisualMinimized), cancellationToken), cancellationToken);
            case PcElementAction.Maximize:
                return With<IUIAutomationWindowPattern>(resolve, UiaIds.WindowPattern, "maximize", p => Mutate(() => p.SetWindowVisualState(UiaIds.VisualMaximized), cancellationToken), cancellationToken);
            case PcElementAction.Restore:
                return With<IUIAutomationWindowPattern>(resolve, UiaIds.WindowPattern, "restore", p => Mutate(() => p.SetWindowVisualState(UiaIds.VisualNormal), cancellationToken), cancellationToken);
            case PcElementAction.Close:
                return With<IUIAutomationWindowPattern>(resolve, UiaIds.WindowPattern, "close", p => Mutate(() => p.Close(), cancellationToken), cancellationToken);
            case PcElementAction.SetValue:
                return SetValue(resolve, node, command.Value ?? "", cancellationToken);
            case PcElementAction.Scroll:
                return Scroll(resolve, command, cancellationToken);
        }
        return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "unknown action");
    }

    private static PcUiaOutcome SetValue(Func<int, (int HResult, object? Pattern)> resolve, PcUiNode node, string value, CancellationToken cancellationToken)
    {
        if (node.Has(PcStates.Password)) return PcUiaOutcome.Fail(PcErrorCode.PasswordField, "this is a password field");
        if (node.Can(PcActions.Value))
        {
            if (node.Has(PcStates.ReadOnly)) return PcUiaOutcome.Fail(PcErrorCode.Disabled, "this field is read-only");
            return With<IUIAutomationValuePattern>(resolve, UiaIds.ValuePattern, "set value", p => Mutate(() => p.SetValue(value), cancellationToken), cancellationToken);
        }
        if (node.Can(PcActions.RangeValue))
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                return PcUiaOutcome.Fail(PcErrorCode.BadArgs, "this element takes a number");
            return With<IUIAutomationRangeValuePattern>(resolve, UiaIds.RangeValuePattern, "set value", p =>
            {
                if (p.get_CurrentIsReadOnly(out var readOnly) == 0 && readOnly != 0) return ElementNotEnabled;
                if (p.get_CurrentMinimum(out var min) == 0 && p.get_CurrentMaximum(out var max) == 0 && (number < min || number > max))
                    return unchecked((int)0x80070057);
                return Mutate(() => p.SetValue(number), cancellationToken);
            }, cancellationToken);
        }
        return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "this element has no value to set");
    }

    private static PcUiaOutcome Scroll(Func<int, (int HResult, object? Pattern)> resolve, PcElementCommand command, CancellationToken cancellationToken)
    {
        var increment = command.Unit == PcScrollUnit.Page ? UiaIds.LargeIncrement : UiaIds.SmallIncrement;
        var decrement = command.Unit == PcScrollUnit.Page ? UiaIds.LargeDecrement : UiaIds.SmallDecrement;
        var (horizontal, vertical) = command.Direction switch
        {
            PcScrollDirection.Down => (UiaIds.NoAmount, increment),
            PcScrollDirection.Up => (UiaIds.NoAmount, decrement),
            PcScrollDirection.Right => (increment, UiaIds.NoAmount),
            PcScrollDirection.Left => (decrement, UiaIds.NoAmount),
        };
        return With<IUIAutomationScrollPattern>(resolve, UiaIds.ScrollPattern, "scroll", p =>
        {
            for (var i = 0; i < Math.Clamp(command.Count, 1, 50); i++)
            {
                var result = Mutate(() => p.Scroll(horizontal, vertical), cancellationToken);
                if (result != 0) return i == 0 ? result : 0;
            }
            return 0;
        }, cancellationToken);
    }

    internal static string? ReadText(IUIAutomationElement element, PcUiNode node)
    {
        if (node.Can(PcActions.Text) && element.GetCurrentPattern(UiaIds.TextPattern, out var pattern) == 0 && pattern is IUIAutomationTextPattern text
            && text.get_DocumentRange(out var range) == 0 && range is not null && range.GetText(-1, out var all) == 0 && all is not null)
            return all;
        if (node.Can(PcActions.Value) && !node.Has(PcStates.Password)
            && element.GetCurrentPattern(UiaIds.ValuePattern, out var valuePattern) == 0 && valuePattern is IUIAutomationValuePattern value
            && value.get_CurrentValue(out var current) == 0)
            return current ?? "";
        if (node.Children.Count > 0) return string.Join('\n', node.Children.Select(c => c.Name).Where(n => n.Length > 0));
        return node.Has(PcStates.Password) ? null : node.Value ?? node.Name;
    }

    private static int Mutate(Func<int> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return call();
    }

    private static PcUiaOutcome With<TPattern>(Func<int, (int HResult, object? Pattern)> resolve, int patternId, string what, Func<TPattern, int> call, CancellationToken cancellationToken) where TPattern : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (hresult, pattern) = resolve(patternId);
        cancellationToken.ThrowIfCancellationRequested();
        if (hresult != 0) return Outcome(hresult, what);
        if (pattern is not TPattern typed) return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "the element cannot " + what);
        try { return Outcome(call(typed), what); }
        catch (COMException error) { return Outcome(error.HResult, what); }
    }
}
