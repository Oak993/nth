// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 2.5 size ladder "Notifications: the needs-you toast", "Reduce motion"; ED-NEW-dextra-as9-7). No Swift twin.
// Armilla's needs-you pose as the picture of a decision toast: the live mark's own engine, snapped to its still pose and drawn once for each theme,
// written beside nth's other notification picture (and only when its bytes differ), and named by its absolute path. A toast cannot host a live
// mark, so it shows the still pose; there is nothing to animate and nothing for Reduce motion to turn off. A toast without it is only the text.
using Marrow.App.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant;

public sealed class DecisionToastLogo(IToastIconFiles files, string folder, Func<bool> dark)
{
    /// <summary>96 px: the 48 px of the size ladder at 200 % scale (Windows scales it down for 100 %).</summary>
    public const int Pixels = 96;

    private readonly Dictionary<(bool Dark, bool News), string> installed = [];
    private readonly object gate = new();

    /// <summary>The needs-you picture for the theme the toast will be shown in now, or null when it cannot be written (the toast is then text alone).</summary>
    public string? Path() => Install(news: false);

    /// <summary>The resting picture of a duty's finding, which asks nothing (13.1 "Duty opt-in"); the same rules as <see cref="Path"/>.</summary>
    public string? NewsPath() => Install(news: true);

    private string? Install(bool news)
    {
        try
        {
            var isDark = dark();
            lock (gate)
            {
                if (installed.TryGetValue((isDark, news), out var known)) return known;
                var name = (news ? "dextra-news-" : "dextra-needs-you-") + (isDark ? "dark" : "light") + ".png";
                var look = news ? ArmillaStill.NewsLook(isDark) : ArmillaStill.NeedsYouLook(isDark);
                var shown = ToastIcon.Install(files, System.IO.Path.Combine(folder, name), ArmillaStill.Png(look, Pixels));
                installed[(isDark, news)] = shown;
                return shown;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The app's: beside nth's notification icon, in the settings folder, following the shell's own theme.</summary>
    public static DecisionToastLogo Production() => new(new WindowsToastIconFiles(),
        System.IO.Path.GetDirectoryName(SettingsStore.ProductionPath(DevDataRoot.LocalApplicationData()))!, ShellIsDark);

    // The shell draws a toast in the system theme (Settings > Personalization > Colors > Windows mode); the apps' theme is a different choice.
    private static bool ShellIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
