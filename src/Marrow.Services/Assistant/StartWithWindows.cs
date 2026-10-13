// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4 "Start with Windows (hidden)" and "Wake the PC"; unit AS-10; ED-NEW-dextra-as10-14, -15).
// No Swift twin. The second switch of the first duty's card: a per-user Run entry that starts "nth --hidden" at sign-in, off until
// the owner taps it; and the question the wake option needs answered: does the power plan allow wake timers. Both are behind
// interfaces so the tests touch neither the registry nor powercfg; the Native tests do.
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Marrow.Services.Assistant;

/// <summary>The per-user Run key: a value name to a command line.</summary>
public interface IRunKey
{
    string? Get(string name);

    void Set(string name, string command);

    void Delete(string name);
}

public sealed class RegistryRunKey : IRunKey
{
    public const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>"Start with Windows, hidden": one Run value that starts nth with --hidden. Off until the owner turns it on.</summary>
public sealed class StartWithWindows(IRunKey key, string valueName = StartWithWindows.DefaultValueName)
{
    /// <summary>ED-NEW-secfinal-24: Dextra's own Run value ("nth Dextra"); the "Open at sign-in" switch keeps the value "nth".</summary>
    public const string DefaultValueName = Marrow.Core.Support.UninstallEntry.DextraStartValueName;

    public const string HiddenArgument = Marrow.Core.Port.Assistant.StartupArguments.HiddenFlag;

    /// <summary>The command the Run value holds for an executable: its path in quotes, then --hidden.</summary>
    public static string CommandFor(string exePath) => "\"" + exePath + "\" " + HiddenArgument;

    /// <summary>Whether the entry exists (whatever program it starts; a changed path shows as off so the owner can turn it on again).</summary>
    public bool IsOn(string exePath)
    {
        try { return string.Equals(key.Get(valueName), CommandFor(exePath), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException) { return false; }
    }

    /// <summary>Turns it on or off. Returns the reason it could not be, or null.</summary>
    public string? Turn(bool on, string exePath)
    {
        try
        {
            if (!on)
            {
                key.Delete(valueName);
                return null;
            }
            if (string.IsNullOrWhiteSpace(exePath) || !System.IO.Path.IsPathFullyQualified(exePath) || exePath.Contains('"', StringComparison.Ordinal))
                return "nth cannot tell Windows where it is installed.";
            key.Set(valueName, CommandFor(exePath));
            return null;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return "Windows would not change the start-up list: " + error.Message;
        }
    }
}

/// <summary>Whether the power plan lets Task Scheduler wake the PC (7.4: "Waking needs the PC to allow wake timers").</summary>
public sealed record WakeTimerState(bool? Allowed, string? Words)
{
    public static WakeTimerState Unknown { get; } = new(null, null);
}

public static partial class WakeTimerPolicy
{
    /// <summary>The power setting "Allow wake timers" under Sleep.</summary>
    public const string SettingGuid = "BD3B718A-0680-4D9D-8AB2-E1D2B4AC806D";

    [GeneratedRegex(@"Current (?<kind>AC|DC) Power Setting Index:\s*0x(?<value>[0-9a-fA-F]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Index();

    /// <summary>Reads powercfg's text. 0 is off, 1 is on, 2 is "important wake timers only" (Task Scheduler's count).</summary>
    public static WakeTimerState Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return WakeTimerState.Unknown;
        int? ac = null;
        int? dc = null;
        foreach (Match match in Index().Matches(output))
        {
            var value = int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (match.Groups["kind"].Value == "AC") ac = value;
            else dc = value;
        }
        if (ac is null && dc is null) return WakeTimerState.Unknown;
        const string where = "Power Options, Change plan settings, Advanced settings, Sleep, Allow wake timers";
        if (ac == 0 && (dc is null or 0)) return new WakeTimerState(false, $"The power plan does not allow wake timers, so the PC will not wake for this duty. Turn them on in {where}.");
        if (ac == 0) return new WakeTimerState(true, $"The PC wakes only on battery: wake timers are off when it is plugged in ({where}).");
        if (dc == 0) return new WakeTimerState(true, $"The PC wakes only when plugged in: wake timers are off on battery ({where}).");
        return new WakeTimerState(true, null);
    }

    public static async Task<WakeTimerState> Read(IProcessRunner runner, string? system32, CancellationToken cancellationToken)
    {
        try
        {
            var exe = System.IO.Path.Combine(system32 ?? Environment.SystemDirectory, "powercfg.exe");
            var result = await runner.Run(exe, ["/qh", "SCHEME_CURRENT", "SUB_SLEEP", SettingGuid], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? Parse(result.Output) : WakeTimerState.Unknown;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return WakeTimerState.Unknown;
        }
    }
}
