using Avalonia.Media;
using Microsoft.Win32;

namespace Marrow.Setup.Platform;

/// <summary>Windows' own colours for a contrast theme (GetSysColor), which the window uses instead of its glass.</summary>
public readonly record struct SystemColorSet(
    Color Window,
    Color WindowText,
    Color Highlight,
    Color HighlightText,
    Color HotLight,
    Color GrayText,
    Color ButtonFace,
    Color ButtonText)
{
    /// <summary>Windows 10's "High Contrast Black", the values the canvas shows for Contrast › High.</summary>
    public static SystemColorSet HighContrastBlack { get; } = new(
        Colors.Black, Colors.White, Color.FromRgb(0x1A, 0xEB, 0xFF), Colors.Black,
        Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x3F, 0xF2, 0x3F), Colors.Black, Colors.White);
}

/// <summary>
/// The Windows settings the setup window follows (nth installer PLAN.md 3, "Window details"; unit U1, ED-NEW-inst-u1-1):
/// Animation effects (<c>SPI_GETCLIENTAREAANIMATION</c>; off is the canvas's Calm), a contrast theme
/// (<c>SPI_GETHIGHCONTRAST</c>) and its colours, light or dark apps, Text size (100-225 %) and the Windows build.
/// </summary>
public sealed record SystemLook(bool Animations, bool HighContrast, bool Dark, double TextScale, int WindowsBuild, SystemColorSet Colors)
{
    /// <summary>Animation effects off, or reduced motion: everything fades in 0.2 s, nothing moves, no drift, no light in the bar.</summary>
    public bool Calm => !Animations;

    /// <summary>Windows 11 (build 22000 and later): Mica, 8 px corners. Windows 10 gets a solid tint and square corners.</summary>
    public bool IsWindows11 => WindowsBuild >= 22000;

    /// <summary>The look headless tests and the canvas start from: dark, animations on, 100 % text, Windows 11.</summary>
    public static SystemLook Default { get; } = new(true, false, true, 1.0, 26100, SystemColorSet.HighContrastBlack);
}

/// <summary>Where the window reads <see cref="SystemLook"/> from: Windows in the app, a fake in tests.</summary>
public interface ISystemLookSource
{
    SystemLook Read();
}

/// <summary>Reads Windows' settings as they are now. Every read is cheap; nothing is cached.</summary>
public sealed class WindowsSystemLookSource : ISystemLookSource
{
    /// <summary>HKCU\Software\Microsoft\Accessibility: TextScaleFactor, Settings › Accessibility › Text size (100-225).</summary>
    public const string AccessibilityKey = @"Software\Microsoft\Accessibility";

    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public SystemLook Read()
    {
        if (!OperatingSystem.IsWindows()) return SystemLook.Default;
        return new SystemLook(ReadAnimations(), ReadHighContrast(), ReadDark(), ReadTextScale(), Environment.OSVersion.Version.Build, ReadColors());
    }

    private static bool ReadAnimations() =>
        !Native.SystemParametersInfoBool(Native.SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0) || enabled != 0;

    private static unsafe bool ReadHighContrast()
    {
        var contrast = new Native.HighContrast { Size = (uint)sizeof(Native.HighContrast) };
        return Native.SystemParametersInfoHighContrast(Native.SPI_GETHIGHCONTRAST, contrast.Size, ref contrast, 0) &&
            (contrast.Flags & Native.HCF_HIGHCONTRASTON) != 0;
    }

    private static bool ReadDark() => Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0;

    /// <summary>The Text size factor, 1.0 to 2.25; a missing or odd value is 1.0.</summary>
    public static double ReadTextScale() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\" + AccessibilityKey, "TextScaleFactor", 100) is int percent ? TextScale(percent) : 1.0;

    /// <summary>Windows stores Text size as a percentage, 100 to 225.</summary>
    public static double TextScale(int percent) => Math.Clamp(percent, 100, 225) / 100.0;

    private static SystemColorSet ReadColors() => new(
        Read(Native.COLOR_WINDOW), Read(Native.COLOR_WINDOWTEXT), Read(Native.COLOR_HIGHLIGHT), Read(Native.COLOR_HIGHLIGHTTEXT),
        Read(Native.COLOR_HOTLIGHT), Read(Native.COLOR_GRAYTEXT), Read(Native.COLOR_BTNFACE), Read(Native.COLOR_BTNTEXT));

    /// <summary>A COLORREF is 0x00BBGGRR.</summary>
    public static Color FromColorRef(uint value) => Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    private static Color Read(int index) => FromColorRef(Native.GetSysColor(index));
}

/// <summary>
/// The settings as the window sees them, kept live (PLAN.md 3: the build reacts while open, where the canvas read them only
/// at load). <see cref="Refresh"/> reads again and raises <see cref="Changed"/> only when something differs; the window
/// calls it on WM_SETTINGCHANGE, WM_SYSCOLORCHANGE and WM_THEMECHANGED, and a registry watcher calls it when Text size
/// changes.
/// </summary>
public sealed class SystemSettings
{
    private readonly ISystemLookSource source;

    public SystemSettings(ISystemLookSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        Current = source.Read();
    }

    public SystemLook Current { get; private set; }

    public event Action<SystemLook>? Changed;

    public void Refresh()
    {
        var next = source.Read();
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(next);
    }

    /// <summary>The window messages after which the settings are read again.</summary>
    public static bool IsSettingMessage(uint message) =>
        message is Native.WM_SETTINGCHANGE or Native.WM_SYSCOLORCHANGE or Native.WM_THEMECHANGED or Native.WM_DWMCOMPOSITIONCHANGED;
}

/// <summary>
/// Calls back when a value under an HKCU key changes (RegNotifyChangeKeyValue, thread-agnostic, re-armed after each
/// change). Text size is stored in the registry and Windows does not promise a broadcast for it, so the window watches it.
/// The callback runs on a pool thread; the window posts it to its own thread.
/// </summary>
public sealed class RegistryWatcher : IDisposable
{
    private readonly RegistryKey? key;
    private readonly AutoResetEvent changed = new(false);
    private readonly Action callback;
    private RegisteredWaitHandle? wait;
    private bool disposed;

    public RegistryWatcher(string currentUserSubKey, Action callback)
    {
        this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
        if (!OperatingSystem.IsWindows()) return;
        key = Registry.CurrentUser.OpenSubKey(currentUserSubKey, writable: false);
        if (key is null) return;
        if (!Arm()) return;
        wait = ThreadPool.RegisterWaitForSingleObject(changed, (_, _) => OnChanged(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private bool Arm() =>
        key is not null && !disposed &&
        Native.RegNotifyChangeKeyValue(key.Handle, false, Native.REG_NOTIFY_CHANGE_LAST_SET | Native.REG_NOTIFY_THREAD_AGNOSTIC, changed.SafeWaitHandle, true) == 0;

    private void OnChanged()
    {
        lock (changed)
        {
            if (disposed) return;
            Arm();
        }
        callback();
    }

    public void Dispose()
    {
        lock (changed)
        {
            if (disposed) return;
            disposed = true;
        }
        wait?.Unregister(null);
        key?.Dispose();
        changed.Dispose();
    }
}
