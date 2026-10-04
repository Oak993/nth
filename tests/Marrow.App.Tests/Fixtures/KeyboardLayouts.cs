using System.Runtime.CompilerServices;
using Avalonia.Input;
using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A keyboard layout as a table (ED-270): what each physical key types plain and with Shift, so a test names the layout it
/// presses on instead of reading the machine's. Keys not in the table type nothing. Helper code, never in a test class (R-TEST-3).
/// </summary>
public sealed class KeyboardLayouts(Dictionary<PhysicalKey, (string Plain, string Shifted)> keys) : IKeyboardLayoutText
{
    /// <summary>The keys the table names.</summary>
    public IEnumerable<PhysicalKey> Keys => keys.Keys;

    public string? Typed(PhysicalKey key, bool shift) =>
        keys.TryGetValue(key, out var typed) && (shift ? typed.Shifted : typed.Plain) is { Length: > 0 } text ? text : null;

    // ANSI US: the number row and the punctuation keys the zoom chords can sit on.
    public static KeyboardLayouts Us { get; } = new(new()
    {
        [PhysicalKey.Digit1] = ("1", "!"), [PhysicalKey.Digit2] = ("2", "@"), [PhysicalKey.Digit3] = ("3", "#"),
        [PhysicalKey.Digit4] = ("4", "$"), [PhysicalKey.Digit5] = ("5", "%"), [PhysicalKey.Digit6] = ("6", "^"),
        [PhysicalKey.Digit7] = ("7", "&"), [PhysicalKey.Digit8] = ("8", "*"), [PhysicalKey.Digit9] = ("9", "("),
        [PhysicalKey.Digit0] = ("0", ")"), [PhysicalKey.Minus] = ("-", "_"), [PhysicalKey.Equal] = ("=", "+"),
        [PhysicalKey.BracketLeft] = ("[", "{"), [PhysicalKey.BracketRight] = ("]", "}"), [PhysicalKey.Slash] = ("/", "?"),
        [PhysicalKey.K] = ("k", "K"),
    });

    // German (Germany) QWERTZ: "+" and "-" have their own keys, at the US "]" and "/" positions; the key at the US "-"
    // position types "ß", and the one at the US "=" position is a dead acute accent (no character).
    public static KeyboardLayouts German { get; } = new(new()
    {
        [PhysicalKey.Digit1] = ("1", "!"), [PhysicalKey.Digit2] = ("2", "\""), [PhysicalKey.Digit3] = ("3", "§"),
        [PhysicalKey.Digit4] = ("4", "$"), [PhysicalKey.Digit5] = ("5", "%"), [PhysicalKey.Digit6] = ("6", "&"),
        [PhysicalKey.Digit7] = ("7", "/"), [PhysicalKey.Digit8] = ("8", "("), [PhysicalKey.Digit9] = ("9", ")"),
        [PhysicalKey.Digit0] = ("0", "="), [PhysicalKey.Minus] = ("ß", "?"), [PhysicalKey.Equal] = ("", ""),
        [PhysicalKey.BracketLeft] = ("ü", "Ü"), [PhysicalKey.BracketRight] = ("+", "*"), [PhysicalKey.Slash] = ("-", "_"),
        [PhysicalKey.K] = ("k", "K"),
    });

    // French (France) AZERTY: the number row types symbols plain and digits with Shift; "=" is at the US "=" position,
    // "-" at the US "6" position, and "+" is Shift with "=".
    public static KeyboardLayouts French { get; } = new(new()
    {
        [PhysicalKey.Digit1] = ("&", "1"), [PhysicalKey.Digit2] = ("é", "2"), [PhysicalKey.Digit3] = ("\"", "3"),
        [PhysicalKey.Digit4] = ("'", "4"), [PhysicalKey.Digit5] = ("(", "5"), [PhysicalKey.Digit6] = ("-", "6"),
        [PhysicalKey.Digit7] = ("è", "7"), [PhysicalKey.Digit8] = ("_", "8"), [PhysicalKey.Digit9] = ("ç", "9"),
        [PhysicalKey.Digit0] = ("à", "0"), [PhysicalKey.Minus] = (")", "°"), [PhysicalKey.Equal] = ("=", "+"),
        [PhysicalKey.K] = ("k", "K"),
    });
}

/// <summary>
/// The headless tests' process-wide keyboard layout is ANSI US, never the machine's (ED-270): a chord that reaches a zoom
/// item by the character typed would otherwise depend on whichever layout the PC running the tests has active.
/// </summary>
internal static class UsKeyboardLayoutByDefault
{
    [ModuleInitializer]
    internal static void Install() => KeyboardLayoutText.Current.SetProcessDefault(KeyboardLayouts.Us);
}
