// PORT-ONLY, owner-ruled addition (OD-0097; first-release voice typing, VT-13): Windows' display language for the language default C (voice-SPEC V-7.14 rule 3, V-16.3.1).
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The display language Windows is shown in, as a culture name such as "hi-IN": <c>GetUserDefaultUILanguage</c> mapped through <see cref="CultureInfo"/> (V-7.14). The list lookup
/// (<c>VoiceLanguage.WindowsLanguageCode</c>) takes its two-letter part. It reads one number and touches no device. A test replaces <see cref="Source"/> for its own flow.
/// </summary>
internal static class WindowsDisplayLanguage
{
    /// <summary>The reader; the default asks Windows.</summary>
    public static Ambient<Func<string?>> Source { get; } = new((Func<string?>)ReadSystem);

    /// <summary>The display language now, or null when the reader fails.</summary>
    public static string? Current()
    {
        try { return Source.Current(); }
        catch (Exception) { return null; }
    }

    /// <summary>Asks Windows (the real reader).</summary>
    public static string? ReadSystem() => OperatingSystem.IsWindows() ? NameOf(GetUserDefaultUILanguage()) : null;

    /// <summary>The culture name of a Windows language id, or null when the runtime does not know the id.</summary>
    public static string? NameOf(ushort languageId)
    {
        try
        {
            var name = new CultureInfo(languageId).Name;
            return name.Length == 0 ? null : name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
