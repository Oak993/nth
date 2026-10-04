using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The production Windows reader (Port/KeyboardLayoutText.cs, WindowsKeyboardLayoutText) bound to a layout the test names
/// (ED-270): the layout is loaded into this process by its identifier (US 00000409, German 00000407, French 0000040C) and
/// never activated, so no test depends on the keyboard layout of the PC it runs on and none changes it. Helper code,
/// never in a test class (R-TEST-3).
/// </summary>
public static class WindowsLayoutReaders
{
    private const uint LoadWithoutTellingTheShell = 0x80; // KLF_NOTELLSHELL; no KLF_ACTIVATE, so the active layout stays

    public static IKeyboardLayoutText Us => For("00000409");

    public static IKeyboardLayoutText German => For("00000407");

    public static IKeyboardLayoutText French => For("0000040C");

    private static IKeyboardLayoutText For(string layoutId)
    {
        var handle = LoadKeyboardLayoutW(layoutId, LoadWithoutTellingTheShell);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Windows could not load the keyboard layout " + layoutId);
        var type = typeof(MenuKeyEquivalents).Assembly.GetType("Marrow.App.Port.WindowsKeyboardLayoutText")!;
        return (IKeyboardLayoutText)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [(Func<IntPtr>)(() => handle)], null)!;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayoutW(string layoutId, uint flags);
}
