// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 16.2; unit AS-19; ED-NEW-dextra-as19-2, -9). No Swift twin.
// What is under a click and what is in focus, asked of UI Automation directly (the fallback of 16.2 until Tactus's element lookup lands). The
// app and the window title come from Windows itself (the window under the point, its top window, its process), so they are there even when an
// app does not answer UI Automation; the control's name and type and the IsPassword flag come from UI Automation. A call that takes more than
// 1.5 s (an app that hangs) is given up on and answers nothing, so a hung app can slow a step but never the recording. A text is read only
// from a control that does not hold a password, and only when asked. Never used by automated tests; TeachProbeNativeTests is the lead's to run.
using System.Runtime.InteropServices;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Teach;

public sealed class UiaElementProbe : IElementProbe
{
    /// <summary>How long one question to UI Automation may take.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromMilliseconds(1500);

    private const int ControlTypeProperty = 30003;
    private const int NameProperty = 30005;
    private const int IsPasswordProperty = 30019;
    private const int ValueProperty = 30045;
    private const int ValueChars = 4000;

    private readonly object gate = new();
    private IUIAutomation? automation;

    // The English names of the UI Automation control types, as a draft says them ("Click “Save” (button)").
    private static readonly Dictionary<int, string> ControlNames = new()
    {
        [50000] = "button", [50001] = "calendar", [50002] = "check box", [50003] = "combo box", [50004] = "edit", [50005] = "link", [50006] = "image",
        [50007] = "list item", [50008] = "list", [50009] = "menu", [50010] = "menu bar", [50011] = "menu item", [50012] = "progress bar",
        [50013] = "radio button", [50014] = "scroll bar", [50015] = "slider", [50016] = "spinner", [50017] = "status bar", [50018] = "tab",
        [50019] = "tab item", [50020] = "text", [50021] = "tool bar", [50022] = "tool tip", [50023] = "tree", [50024] = "tree item", [50025] = "custom",
        [50026] = "group", [50027] = "thumb", [50028] = "data grid", [50029] = "data item", [50030] = "document", [50031] = "split button",
        [50032] = "window", [50033] = "pane", [50034] = "header", [50035] = "header item", [50036] = "table", [50037] = "title bar", [50038] = "separator",
    };

    public TeachElement? At(int x, int y)
    {
        var (app, title) = TeachNative.WindowOf(TeachNative.WindowFromPoint(new TeachNative.Point { X = x, Y = y }));
        var found = Limited(() => Describe(Automation().ElementFromPoint(new TeachNative.Point { X = x, Y = y }, out var element) == 0 ? element : null, release: true).Facts);
        return Build(app, title, found);
    }

    public TeachFocus? Focused()
    {
        var (app, title) = TeachNative.WindowOf(TeachNative.GetForegroundWindow());
        var described = Limited(() => Describe(Automation().GetFocusedElement(out var element) == 0 ? element : null, release: false));
        if (described is null) return null;
        var element = Build(app, title, described.Facts);
        if (element is null) return null;
        var control = described.Control;
        // The text is read later, from this same control, and never from one that holds a password.
        return new TeachFocus(element, element.IsPassword || control is null ? () => null : () => Limited(() => ReadValue(control)));
    }

    public TeachElement? Foreground()
    {
        var (app, title) = TeachNative.WindowOf(TeachNative.GetForegroundWindow());
        return app.Length == 0 && title.Length == 0 ? null : Build(app, title, null);
    }

    // ---- UI Automation ----

    private sealed record Facts(string Type, string Name, bool IsPassword);

    private sealed record Described(Facts? Facts, IUIAutomationElement? Control);

    private static TeachElement? Build(string app, string title, Facts? facts)
    {
        var window = TeachDraft.Clean(title, TeachLimits.NameChars);
        return new TeachElement(TeachDraft.Clean(app, 60), window, facts?.Type ?? "", TeachDraft.Clean(facts?.Name, TeachLimits.NameChars))
        {
            IsPassword = facts?.IsPassword == true,
        };
    }

    private IUIAutomation Automation()
    {
        lock (gate)
        {
            return automation ??= (IUIAutomation)new CUIAutomation();
        }
    }

    // The facts about one control. For a click the control is let go at once; for the focus it is kept so its text can be read later.
    private static Described Describe(IUIAutomationElement? element, bool release)
    {
        if (element is null) return new Described(null, null);
        try
        {
            var type = element.GetCurrentPropertyValue(ControlTypeProperty, out var kind) == 0 && kind is int code && ControlNames.TryGetValue(code, out var name) ? name : "";
            var label = element.GetCurrentPropertyValue(NameProperty, out var text) == 0 && text is string given ? given : "";
            var password = element.GetCurrentPropertyValue(IsPasswordProperty, out var flag) == 0 && flag is true;
            var facts = new Facts(type, password ? "" : label, password);
            if (release)
            {
                Marshal.ReleaseComObject(element);
                return new Described(facts, null);
            }
            return new Described(facts, element);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException)
        {
            if (release) Marshal.ReleaseComObject(element);
            return new Described(null, null);
        }
    }

    private static string? ReadValue(IUIAutomationElement control)
    {
        try
        {
            if (control.GetCurrentPropertyValue(IsPasswordProperty, out var flag) == 0 && flag is true) return null;
            if (control.GetCurrentPropertyValue(ValueProperty, out var value) != 0 || value is not string text) return null;
            return text.Length <= ValueChars ? text : text[..ValueChars];
        }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException)
        {
            return null;
        }
    }

    // Gives up on a call that takes too long (an app that hangs) and says nothing.
    private static T? Limited<T>(Func<T?> call)
    {
        var running = Task.Run(() =>
        {
            try { return call(); }
            catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException or UnauthorizedAccessException) { return default; }
        });
        return running.Wait(CallLimit) ? running.Result : default;
    }

    // ---- the COM interfaces: only the slots used are named; the others only hold the place, as the header orders them ----

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation
    {
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();

        [PreserveSig]
        int ElementFromPoint(TeachNative.Point point, out IUIAutomationElement element);

        [PreserveSig]
        int GetFocusedElement(out IUIAutomationElement element);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();

        [PreserveSig]
        int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
    }
}
