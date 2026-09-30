// PORT-ONLY: Avalonia key events translated to the legacy input bytes of the pinned
// SwiftTerm 1.20.0 (5d14406844143538cd8f8851d2d8a67c1fe443e5) used by TerminalStore.swift:21-23.
// Kitty enhancement negotiation and application-keypad input remain outside this slice.
using System.Globalization;
using System.Text;
using Avalonia.Input;

namespace Marrow.App.Views.Terminal.Port;

internal static class TerminalInput
{
    public static byte[]? EncodeKey(Key key, KeyModifiers modifiers, string? symbol,
        bool applicationCursor, bool optionAsMetaKey)
    {
        if (modifiers.HasFlag(KeyModifiers.Meta)) return null;
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (alt && !optionAsMetaKey) return null;

        // KittyKeyboardEncoder.swift:613-695. The Mac view uses this legacy encoder
        // for functional keys even when no Kitty keyboard enhancements were requested.
        var modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (control ? 4 : 0);
        var navigation = key switch
        {
            Key.Up => "A", Key.Down => "B", Key.Right => "C", Key.Left => "D",
            Key.Home => "H", Key.End => "F", _ => null,
        };
        if (navigation is not null)
            return Bytes(modifier == 1 ? (applicationCursor ? "\U0000001bO" : "\U0000001b[") + navigation
                : "\U0000001b[1;" + Number(modifier) + navigation);
        var tilde = key switch
        {
            Key.Insert => 2, Key.Delete => 3, Key.PageUp => 5, Key.PageDown => 6, _ => 0,
        };
        if (tilde != 0) return Tilde(tilde, modifier);

        // KittyKeyboardEncoder.swift:628-645,740-767: F13-F24 are shifted F1-F12.
        if (key is >= Key.F1 and <= Key.F24)
        {
            var number = (int)key - (int)Key.F1 + 1;
            if (number > 12) { number -= 12; modifier = 2; }
            if (number <= 4)
            {
                var letter = (char)('P' + number - 1);
                return Bytes(modifier == 1 ? "\U0000001bO" + letter : "\U0000001b[1;" + Number(modifier) + letter);
            }
            int[] numbers = [15, 17, 18, 19, 20, 21, 23, 24];
            return Tilde(numbers[number - 5], modifier);
        }

        // KittyKeyboardEncoder.swift:572-598. Escape itself stays with TerminalHost's
        // owner-approved prompt/child routing, so it is intentionally absent here.
        var special = key switch
        {
            Key.Enter => "\r",
            Key.Back => control ? "\b" : "\U0000007f",
            Key.Tab => shift ? "\U0000001b[Z" : "\t",
            _ => null,
        };
        if (special is not null) return Bytes((alt ? "\U0000001b" : "") + special);

        // MacTerminalView.swift:1647-1682 applies Meta before Control for text keys.
        // Prefer the text supplied by the layout; preserve the previous letter fallback
        // when the platform omits the symbol for an Alt key event.
        if (alt)
        {
            if (string.IsNullOrEmpty(symbol) && key is >= Key.A and <= Key.Z)
                symbol = ((char)((shift ? 'A' : 'a') + (int)key - (int)Key.A)).ToString();
            return string.IsNullOrEmpty(symbol) ? null : Bytes("\U0000001b" + symbol);
        }
        if (!control) return null; // TextInput owns Unicode and IME commits.

        // AppleTerminalView.swift:2570-2600. Avalonia may supply a control character
        // instead of charactersIgnoringModifiers; letters and Space have stable Key ids.
        if (key is >= Key.A and <= Key.Z) return [(byte)((int)key - (int)Key.A + 1)];
        if (key == Key.Space) return [0];
        if (key == Key.D6) return [0x1e];
        return symbol switch
        {
            "\\" or "\U0000001c" => [0x1c],
            "_" or "\U0000001f" => [0x1f],
            "]" or "\U0000001d" => [0x1d],
            "[" or "\U0000001b" => [0x1b],
            "^" or "6" or "\U0000001e" => [0x1e],
            " " or "\0" => [0],
            _ => null,
        };
    }

    // MacTerminalView.swift:1925-1931: paste preserves the clipboard text, including
    // newlines and Unicode; mode 2004 adds just the two bracket delimiters.
    public static byte[] EncodePaste(string text, bool bracketed) =>
        Bytes(bracketed ? "\U0000001b[200~" + text + "\U0000001b[201~" : text);

    private static byte[] Tilde(int number, int modifier) =>
        Bytes("\U0000001b[" + Number(number) + (modifier == 1 ? "" : ";" + Number(modifier)) + "~");

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
