using System.Runtime.InteropServices;
using Avalonia.Input;
using Marrow.Core.Port;

namespace Marrow.App.Port;

// PORT-ONLY (ED-270, Q-ZOOM). A macOS menu item with a literal key equivalent, such as Zoom In's "+" (AppCommands.swift:53-54),
// is matched by the character the pressed key types on the active keyboard layout, not by where the key sits. Avalonia
// reports a key's position (PhysicalKey) and a Win32 key symbol that ToUnicodeEx fills in under the modifiers actually held
// (Avalonia.Win32 KeyInterop.GetKeySymbol), which is no character at all for most Ctrl chords. This is the port's own read
// of "what does this key type": the character the physical key produces on the active layout with only Shift applied,
// Ctrl and Alt left out (a chord's modifiers are matched separately, as a menu does).

/// <summary>What a physical key types on the active keyboard layout (ED-270).</summary>
public interface IKeyboardLayoutText
{
    /// <summary>
    /// The single character <paramref name="key"/> types on the active layout, with Shift held or not, or null when it
    /// types no character (a dead key, a function key, a key the layout does not map).
    /// </summary>
    string? Typed(PhysicalKey key, bool shift);
}

/// <summary>The active layout, replaceable for a test's own flow (ED-065 ambient seam).</summary>
public static class KeyboardLayoutText
{
    public static Ambient<IKeyboardLayoutText> Current { get; } = new(() => new WindowsKeyboardLayoutText());
}

/// <summary>
/// The Windows reader: the key's Set-1 scan code (the table MenuKeyEquivalents already uses for ED-099), mapped to the
/// active layout's virtual key, translated by ToUnicodeEx with a keyboard state that holds Shift only and with the flag
/// that leaves the real keyboard state, and so any pending dead key, untouched.
/// </summary>
internal sealed class WindowsKeyboardLayoutText : IKeyboardLayoutText
{
    private const uint MapVirtualKeyScanToVirtualKeyEx = 3; // MAPVK_VSC_TO_VK_EX
    private const uint DoNotChangeKeyboardState = 4; // ToUnicodeEx wFlags bit 2 (Windows 10 version 1607 and later)
    private const int VirtualKeyShift = 0x10;

    public string? Typed(PhysicalKey key, bool shift)
    {
        if (!OperatingSystem.IsWindows() || MenuKeyEquivalents.ScanCodeOf(key) is not { } scan || scan >= 0xE000) return null;
        var layout = GetKeyboardLayout(0);
        var virtualKey = MapVirtualKeyExW((uint)scan, MapVirtualKeyScanToVirtualKeyEx, layout);
        if (virtualKey == 0) return null;
        var state = new byte[256];
        if (shift) state[VirtualKeyShift] = 0x80;
        var buffer = new char[8];
        var count = ToUnicodeEx(virtualKey, (uint)scan, state, buffer, buffer.Length, DoNotChangeKeyboardState, layout);
        return count == 1 && !char.IsControl(buffer[0]) ? buffer[0].ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out] char[] buffer,
        int bufferLength, uint flags, IntPtr layout);
}
