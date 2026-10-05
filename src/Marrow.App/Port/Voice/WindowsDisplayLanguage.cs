// PORT-ONLY, owner-ruled addition (OD-0097; first-release voice typing, VT-13): Windows' display language for the language default C (voice-SPEC V-7.14 rule 3, V-16.3.1).
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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

    /// <summary>
    /// Raised by <see cref="Notify"/> when Windows' display language may have changed (on the UI thread). VOICE ENGINEERING DECISION (CZ-8): the Windows hook that calls it is not built; the reader and every page already follow it.
    /// </summary>
    public static event Action? Changed;

    /// <summary>Tells the pages that show the display language to read it again.</summary>
    public static void Notify() => Changed?.Invoke();

    /// <summary>The change source: how a page listens for <see cref="Notify"/>; the default listens to <see cref="Changed"/>. A test replaces it for its own flow.</summary>
    public static Ambient<Func<Action, IDisposable>> Changes { get; } = new((Func<Action, IDisposable>)Listen);

    private static IDisposable Listen(Action callback)
    {
        Changed += callback;
        return new Unlisten(() => Changed -= callback);
    }

    /// <summary>Calls <paramref name="refresh"/> when the display language changes, while <paramref name="page"/> is in a window.</summary>
    public static void Watch(Control page, Action refresh)
    {
        IDisposable? listening = null;
        void Start() => listening ??= Changes.Current(refresh);
        void Stop() { listening?.Dispose(); listening = null; }
        page.AttachedToVisualTree += (_, _) => Start();
        page.DetachedFromVisualTree += (_, _) => Stop();
        if (TopLevel.GetTopLevel(page) is not null) Start();
    }

    private sealed class Unlisten(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

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
