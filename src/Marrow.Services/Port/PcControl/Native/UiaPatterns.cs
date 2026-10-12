// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1; DESIGN.md 2.2, 3.1). Element actions through UIA
// patterns: Invoke, Toggle, SelectionItem, ExpandCollapse, Value and RangeValue, Scroll, ScrollItem, Window and focus; and
// an element's whole text (TextPattern, else its value, else its children's names). Password and read-only fields refuse
// values here too, not only in the engine. Runs only on UiaReader's worker thread; no automated test calls it, while the
// HRESULT mapping (Outcome) is pure and tested.
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

    internal static PcUiaOutcome Perform(IUIAutomationElement element, PcUiNode node, PcElementCommand command)
    {
        switch (command.Action)
        {
            case PcElementAction.Focus:
                return Outcome(element.SetFocus(), "focus");
            case PcElementAction.Invoke:
                return With<IUIAutomationInvokePattern>(element, UiaIds.InvokePattern, "press", p => p.Invoke());
            case PcElementAction.Toggle:
                return With<IUIAutomationTogglePattern>(element, UiaIds.TogglePattern, "toggle", p => p.Toggle());
            case PcElementAction.Select:
                return With<IUIAutomationSelectionItemPattern>(element, UiaIds.SelectionItemPattern, "select", p => p.Select());
            case PcElementAction.Expand:
                return With<IUIAutomationExpandCollapsePattern>(element, UiaIds.ExpandCollapsePattern, "expand", p => p.Expand());
            case PcElementAction.Collapse:
                return With<IUIAutomationExpandCollapsePattern>(element, UiaIds.ExpandCollapsePattern, "collapse", p => p.Collapse());
            case PcElementAction.ScrollIntoView:
                return With<IUIAutomationScrollItemPattern>(element, UiaIds.ScrollItemPattern, "scroll into view", p => p.ScrollIntoView());
            case PcElementAction.Minimize:
                return With<IUIAutomationWindowPattern>(element, UiaIds.WindowPattern, "minimize", p => p.SetWindowVisualState(UiaIds.VisualMinimized));
            case PcElementAction.Maximize:
                return With<IUIAutomationWindowPattern>(element, UiaIds.WindowPattern, "maximize", p => p.SetWindowVisualState(UiaIds.VisualMaximized));
            case PcElementAction.Restore:
                return With<IUIAutomationWindowPattern>(element, UiaIds.WindowPattern, "restore", p => p.SetWindowVisualState(UiaIds.VisualNormal));
            case PcElementAction.Close:
                return With<IUIAutomationWindowPattern>(element, UiaIds.WindowPattern, "close", p => p.Close());
            case PcElementAction.SetValue:
                return SetValue(element, node, command.Value ?? "");
            case PcElementAction.Scroll:
                return Scroll(element, command);
        }
        return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "unknown action");
    }

    private static PcUiaOutcome SetValue(IUIAutomationElement element, PcUiNode node, string value)
    {
        if (node.Has(PcStates.Password)) return PcUiaOutcome.Fail(PcErrorCode.PasswordField, "this is a password field");
        if (node.Can(PcActions.Value))
        {
            if (node.Has(PcStates.ReadOnly)) return PcUiaOutcome.Fail(PcErrorCode.Disabled, "this field is read-only");
            return With<IUIAutomationValuePattern>(element, UiaIds.ValuePattern, "set value", p => p.SetValue(value));
        }
        if (node.Can(PcActions.RangeValue))
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                return PcUiaOutcome.Fail(PcErrorCode.BadArgs, "this element takes a number");
            return With<IUIAutomationRangeValuePattern>(element, UiaIds.RangeValuePattern, "set value", p =>
            {
                if (p.get_CurrentIsReadOnly(out var readOnly) == 0 && readOnly != 0) return ElementNotEnabled;
                if (p.get_CurrentMinimum(out var min) == 0 && p.get_CurrentMaximum(out var max) == 0 && (number < min || number > max))
                    return unchecked((int)0x80070057);
                return p.SetValue(number);
            });
        }
        return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "this element has no value to set");
    }

    private static PcUiaOutcome Scroll(IUIAutomationElement element, PcElementCommand command)
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
        return With<IUIAutomationScrollPattern>(element, UiaIds.ScrollPattern, "scroll", p =>
        {
            for (var i = 0; i < Math.Clamp(command.Count, 1, 50); i++)
            {
                var result = p.Scroll(horizontal, vertical);
                if (result != 0) return i == 0 ? result : 0;
            }
            return 0;
        });
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

    private static PcUiaOutcome With<TPattern>(IUIAutomationElement element, int patternId, string what, Func<TPattern, int> call) where TPattern : class
    {
        var hresult = element.GetCurrentPattern(patternId, out var pattern);
        if (hresult != 0) return Outcome(hresult, what);
        if (pattern is not TPattern typed) return PcUiaOutcome.Fail(PcErrorCode.NotSupported, "the element cannot " + what);
        try { return Outcome(call(typed), what); }
        catch (COMException error) { return Outcome(error.HResult, what); }
    }
}
