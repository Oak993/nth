// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-2, ED-NEW-pcws-6; DESIGN.md 2.13, 4.7). What the
// user does to set the workspace up, and the two commands that do it. nth never changes a Windows setting itself: the only
// code that turns child sessions on or off is `nth.exe --tactus-workspace-enable` and `--tactus-workspace-disable`, run by
// the user in a terminal they opened themselves, and both refuse to change anything unless that terminal runs as
// administrator (until NP-5 shows whether it must). The Set up panel shows the command, exactly what changes, the undo and
// the limits; its Copy button only copies the command.
namespace Marrow.Services.Port.PcControl.Workspace;

public static class WorkspaceSetup
{
    public const string AgentCommand = "--tactus-workspace-agent";
    public const string EnableCommand = "--tactus-workspace-enable";
    public const string DisableCommand = "--tactus-workspace-disable";

    /// <summary>Whether turning child sessions on needs an administrator terminal. Unknown until NP-5, so the stricter answer.</summary>
    public const bool NeedsAdmin = true;

    public const int ExitDone = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitNotElevated = 5;

    public const string Explain = "Tactus gets its own mouse and keyboard in a second desktop. Your open windows stay yours.";

    public const string WhatChanges =
        "One Windows setting: child sessions, a second desktop of your own account that only nth opens, inside your session. "
        + "Nothing else changes, and nth never changes it by itself.";

    public const string OpenTerminal = NeedsAdmin
        ? "1. Open Terminal as administrator yourself: right-click Start, then Terminal (Admin)."
        : "1. Open Terminal.";

    public const string PasteCommand = "2. Paste this command and press Enter:";

    public const string ComeBack = "3. Come back here. The status turns Ready.";

    public const string UndoLine = "To undo, run this in the same kind of terminal:";

    public const string RemoteDesktopNote =
        "If the status stays Not set up, Windows may also need Remote Desktop on (Settings, System, Remote Desktop). "
        + "That opens port 3389 on your network, so keep your network profile Private. You decide; nth only opens that page.";

    public const string RemoteDesktopSettings = "ms-settings:remotedesktop";

    public static readonly IReadOnlyList<string> Limits =
    [
        "Your windows that are already open stay on your desk. Apps in the workspace are fresh copies.",
        "One workspace at a time.",
        "It uses about 1 to 1.5 GB more memory with a few apps open.",
        "An app that allows only one copy per user opens on your desk instead. Tactus notices and says so.",
    ];

    /// <summary>
    /// The command as the user pastes it into Terminal (PowerShell): the quoted path of nth, the verb, and "| Out-Host", so
    /// PowerShell waits for nth (a windowed app) and shows its answer.
    /// </summary>
    public static string Command(string exe, string verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        return "& \"" + exe.Replace("\"", "", StringComparison.Ordinal) + "\" " + verb + " | Out-Host";
    }

    /// <summary>Whether the arguments are one of the two setting commands.</summary>
    public static bool IsSettingCommand(IReadOnlyList<string> args) =>
        args.Count >= 1 && args[0] is EnableCommand or DisableCommand;

    /// <summary>
    /// Runs `--tactus-workspace-enable` or `--tactus-workspace-disable` for the user. Without an administrator terminal it
    /// changes nothing and says why. Returns the process exit code.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, IChildSessionSwitch sessions, Func<bool> elevated, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(elevated);
        ArgumentNullException.ThrowIfNull(output);
        if (!IsSettingCommand(args) || args.Count != 1)
        {
            output.WriteLine("Use " + EnableCommand + " or " + DisableCommand + " with nothing after it.");
            return ExitUsage;
        }
        var enable = args[0] == EnableCommand;
        if (NeedsAdmin && !elevated())
        {
            output.WriteLine("This changes a Windows setting, so it runs only in a terminal you opened as administrator. Nothing was changed.");
            return ExitNotElevated;
        }
        if (!sessions.Set(enable, out var error))
        {
            output.WriteLine("Windows did not change the setting (error " + error.ToString(System.Globalization.CultureInfo.InvariantCulture) + "). Nothing else was changed.");
            return ExitFailed;
        }
        output.WriteLine(enable
            ? "Child sessions are on. In nth, Settings, Tactus, Separate workspace now says Ready. To undo: nth " + DisableCommand
            : "Child sessions are off. Tactus's separate workspace stays unavailable until you turn them on again.");
        return ExitDone;
    }
}
