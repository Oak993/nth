// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4 "Start with Windows (hidden)" and "Wake the PC"; unit AS-10; ED-NEW-dextra-as10-14). No Swift twin.
// How nth reads its command line for the two background starts: "nth --hidden" (the Run entry at sign-in) loads without showing the
// window, and "nth --duty <id>" (the Task Scheduler wake) loads hidden and runs that one duty. Every other argument is somebody
// else's (toasts, relays) and is left alone. A duty id that is not a plain token is refused, so a command line cannot name
// anything but a duty.
namespace Marrow.Core.Port.Assistant;

public sealed record StartupMode(bool Hidden, string? DutyId, string? Problem)
{
    public static StartupMode Normal { get; } = new(false, null, null);

    /// <summary>nth was started by a wake for one duty and quits when that duty's run is over, unless the owner kept it working.</summary>
    public bool ForOneDuty => DutyId is not null;
}

public static class StartupArguments
{
    public const string HiddenFlag = "--hidden";

    public const string DutyFlag = "--duty";

    public static StartupMode Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return StartupMode.Normal;
        var hidden = false;
        string? duty = null;
        string? problem = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == HiddenFlag)
            {
                hidden = true;
                continue;
            }
            string? value = null;
            if (arg == DutyFlag) value = i + 1 < args.Count ? args[++i] : "";
            else if (arg.StartsWith(DutyFlag + "=", StringComparison.Ordinal)) value = arg[(DutyFlag.Length + 1)..];
            if (value is null) continue;
            if (!Duty.IdShape.IsMatch(value))
            {
                problem = "nth was asked to run a duty by an id it does not accept.";
                continue;
            }
            duty ??= value;
        }
        return new StartupMode(hidden || duty is not null, duty, problem);
    }
}
