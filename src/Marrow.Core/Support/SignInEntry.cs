namespace Marrow.Core.Support;

/// <summary>Where nth's "Open at sign-in" stands in the user's registry (PLAN.md 8, unit U8).</summary>
public enum SignInStatus
{
    /// <summary>There is no <c>Run</c> value named <c>nth</c>.</summary>
    Off,

    /// <summary>The value starts a program inside this install's root, and Windows lets it run.</summary>
    On,

    /// <summary>The value is nth's, but the user turned it off in Windows (Settings > Apps > Startup, or Task Manager), so Windows will not run it.</summary>
    DisabledInWindows,

    /// <summary>A value named <c>nth</c> starts something outside this root (a dev build, another install) or is not a command at all: not nth's to change.</summary>
    Foreign,
}

/// <summary>The sign-in entry as read, with the command it holds when it holds one.</summary>
public sealed record SignInState(SignInStatus Status, string? Command = null)
{
    /// <summary>What the Settings switch shows: only an entry that will really run counts as on.</summary>
    public bool IsOn => Status == SignInStatus.On;
}

/// <summary>What turning the entry on or off did.</summary>
public enum SignInChangeOutcome
{
    /// <summary>The registry was changed.</summary>
    Changed,

    /// <summary>It was already as asked; nothing was written.</summary>
    Unchanged,

    /// <summary>A <c>nth</c> value that is not nth's is there; it was left alone.</summary>
    Foreign,

    /// <summary>The registry refused (see the HRESULT).</summary>
    Failed,
}

/// <summary>The outcome of <see cref="SignInEntry.Enable"/> or <see cref="SignInEntry.Disable"/>, with the Windows error when it failed.</summary>
public sealed record SignInChangeResult(SignInChangeOutcome Outcome, int? HResult = null);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, unit U8, ED-NEW-inst-u8-1). nth's "Open at sign-in", which the setup
/// never offers: a per-user <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> value named <c>nth</c> whose command is
/// the launch target (<see cref="DesktopShortcut.CurrentLaunchTarget"/>: Start's, else <c>current\nth.exe</c>, never the stub)
/// plus <see cref="Argument"/>. Windows lists such a value under Settings > Apps > Startup with the program's own name,
/// icon and publisher, so there is nothing else to register.
/// <list type="bullet">
/// <item><b>Whose it is:</b> a value is nth's when the program it starts is inside the install root (the same rule the
/// uninstall hooks use, <see cref="UninstallEntry.RemoveSignInEntry"/>). Anything else named <c>nth</c> is left alone and
/// reported, never overwritten or removed.</item>
/// <item><b>Windows can say no:</b> the Startup apps page and Task Manager keep their own switch in
/// <c>Explorer\StartupApproved\Run</c> (a <c>REG_BINARY</c> whose first byte is odd when the entry is turned off). A value
/// that Windows will not run reads as off, so the switch never claims what will not happen; turning it on clears that flag.</item>
/// <item><b>Never the real hive in a test:</b> everything goes through <see cref="IUserRegistry"/>.</item>
/// </list>
/// </summary>
public static class SignInEntry
{
    /// <summary>The argument the entry passes. nth has no tray, so it opens like any start, with one difference: it never reopens a running nth.</summary>
    public const string Argument = "--at-sign-in";

    /// <summary>The Startup apps page's own on/off record for <c>Run</c> values.</summary>
    public const string ApprovedKeyPath = UninstallEntry.StartupApprovedRunKeyPath;

    /// <summary>The command the <c>Run</c> value holds: the quoted program, the launch target's own arguments (Start's, normally none), then <see cref="Argument"/>.</summary>
    public static string Command(LaunchTarget launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var arguments = string.IsNullOrWhiteSpace(launch.Arguments) ? "" : " " + launch.Arguments.Trim();
        return "\"" + launch.Target + "\"" + arguments + " " + Argument;
    }

    /// <summary>Whether this process was started by the sign-in entry (the argument is anywhere on the command line).</summary>
    public static bool IsAtSignInLaunch(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        for (var i = 0; i < arguments.Count; i++)
            if (string.Equals(arguments[i], Argument, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Reads the entry for this root. Reads only; a missing key or value is <see cref="SignInStatus.Off"/>.
    /// </summary>
    public static SignInState Read(IUserRegistry registry, string root)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var value = registry.GetValue(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName);
        if (value is null) return new SignInState(SignInStatus.Off);
        if (value is not string command) return new SignInState(SignInStatus.Foreign);
        if (!IsOurs(command, root)) return new SignInState(SignInStatus.Foreign, command);
        return new SignInState(WindowsTurnedOff(registry) ? SignInStatus.DisabledInWindows : SignInStatus.On, command);
    }

    /// <summary>
    /// Turns the entry on: creates the <c>Run</c> key if the profile has none, writes the command when it differs, and clears
    /// Windows' "turned off" flag if the user had set it. A <c>nth</c> value that is not nth's is left and reported
    /// <see cref="SignInChangeOutcome.Foreign"/>. Never throws for the registry.
    /// </summary>
    public static SignInChangeResult Enable(IUserRegistry registry, string root, LaunchTarget launch)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(launch);
        try
        {
            var state = Read(registry, root);
            if (state.Status == SignInStatus.Foreign) return new SignInChangeResult(SignInChangeOutcome.Foreign);
            var command = Command(launch);
            var changed = false;
            if (!string.Equals(state.Command, command, StringComparison.Ordinal))
            {
                registry.CreateKey(UninstallEntry.RunKeyPath);
                registry.SetString(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName, command);
                changed = true;
            }
            // C4b U8-1, ED-NEW-inst-cx-10: an absent Run value can still have a stale disabled approval from an old uninstall.
            if (WindowsTurnedOff(registry))
            {
                registry.DeleteValue(ApprovedKeyPath, UninstallEntry.SignInValueName);
                changed = true;
            }
            return new SignInChangeResult(changed ? SignInChangeOutcome.Changed : SignInChangeOutcome.Unchanged);
        }
        catch (Exception error) when (IsRegistryFailure(error))
        {
            return new SignInChangeResult(SignInChangeOutcome.Failed, DesktopShortcut.HResultOf(error));
        }
    }

    /// <summary>
    /// Turns the entry off: deletes nth's <c>Run</c> value and Windows' flag for it, so a later "on" starts clean. A value that
    /// is not nth's is left and reported <see cref="SignInChangeOutcome.Foreign"/>; no value is <see cref="SignInChangeOutcome.Unchanged"/>.
    /// </summary>
    public static SignInChangeResult Disable(IUserRegistry registry, string root)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        try
        {
            switch (Read(registry, root).Status)
            {
                case SignInStatus.Foreign:
                    return new SignInChangeResult(SignInChangeOutcome.Foreign);
                case SignInStatus.Off:
                    return new SignInChangeResult(SignInChangeOutcome.Unchanged);
                default:
                    registry.DeleteValue(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName);
                    registry.DeleteValue(ApprovedKeyPath, UninstallEntry.SignInValueName);
                    return new SignInChangeResult(SignInChangeOutcome.Changed);
            }
        }
        catch (Exception error) when (IsRegistryFailure(error))
        {
            return new SignInChangeResult(SignInChangeOutcome.Failed, DesktopShortcut.HResultOf(error));
        }
    }

    private static bool IsOurs(string command, string root) =>
        UninstallEntry.CommandExecutable(command) is { } program && InstallLayout.IsInside(root, Environment.ExpandEnvironmentVariables(program));

    /// <summary>The first byte of the Startup apps record is odd when the user turned the entry off (02 and 06 run it; 03 and 07 do not).</summary>
    private static bool WindowsTurnedOff(IUserRegistry registry) =>
        registry.GetValue(ApprovedKeyPath, UninstallEntry.SignInValueName) is byte[] { Length: > 0 } record && (record[0] & 1) == 1;

    private static bool IsRegistryFailure(Exception error) =>
        DesktopShortcut.IsShellFailure(error) || error is System.Security.SecurityException;
}
