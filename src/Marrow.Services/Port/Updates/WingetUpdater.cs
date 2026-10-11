// PORT-ONLY: UE-3, OD-0100; installed-package parsing and exact upgrade argv from supervised trial H.
using Marrow.Core.Port.Updates;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Updates;

public sealed class WingetUpdater : IUpdateItem
{
    public const int NoInstalledPackage = -1978335212; // 0x8A150014, fixture 12.
    public const int NoApplicableUpdate = -1978335189; // 0x8A15002B, fixture 19.
    private static readonly Regex PackageId = new(@"\A(?:Git\.Git|GitHub\.cli|OpenJS\.NodeJS(?:\.[A-Za-z0-9]+)?|Python\.Python\.3\.[0-9]+)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Row = new(@"\A(?<name>.*?)\s+(?<id>Git\.Git|GitHub\.cli|OpenJS\.NodeJS(?:\.[A-Za-z0-9]+)?|Python\.Python\.3\.[0-9]+)\s+(?<version>[0-9]+(?:\.[0-9A-Za-z]+)+)(?:\s+(?<available>[0-9]+(?:\.[0-9A-Za-z]+)+))?\s+(?<source>\S+)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
    private static readonly Regex KnownId = new(@"(?:\A|\s)(?:Git\.Git|GitHub\.cli|OpenJS\.NodeJS(?:\.[A-Za-z0-9]+)?|Python\.Python\.3\.[0-9]+)(?:\s|\z)", RegexOptions.CultureInvariant);
    private readonly string packageId;
    private readonly Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>> run;
    private readonly IReadOnlyList<string> query;
    private readonly Func<CancellationToken, Task<IReadOnlyList<GitLockingProcess>>>? gitLocks;
    private readonly Func<TimeSpan, CancellationToken, Task> pause;

    /// <summary>How long the update waits before its second look at what holds Git (ED-NEW-providersmerge-git-2): a git.exe or bash.exe that runs for a moment (nth's own source control,
    /// an agent's Bash command) has exited by then, as it would have before the installer reached its own check.</summary>
    public static readonly TimeSpan SecondLook = TimeSpan.FromMilliseconds(1750);

    /// <param name="gitLocks">Git only (ED-NEW-providers-git-1): what holds Git for Windows right now. When given, an update (and so its Retry) looks first and, while the same processes
    /// still hold Git at a second look <see cref="SecondLook"/> later, says what without starting winget; after the installer itself refuses, it names what is running. Null: no look (the
    /// installer's own list still names the processes).</param>
    /// <param name="pause">The wait between the two looks (Task.Delay; a test passes its own).</param>
    public WingetUpdater(string id, string title, Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, IReadOnlyList<string>? query = null,
        Func<CancellationToken, Task<IReadOnlyList<GitLockingProcess>>>? gitLocks = null, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        ValidateId(id);
        packageId = id; Title = title; this.run = run ?? DevToolProcess.Refuse;
        this.query = query?.ToArray() ?? ["--id", id, "-e"];
        this.gitLocks = id == "Git.Git" ? gitLocks : null;
        this.pause = pause ?? Task.Delay;
    }
    public string Id => "dev:" + packageId;
    public string Title { get; }
    public UpdateItemKind Kind => UpdateItemKind.DevTool;

    public static IReadOnlyList<WingetPackage> ParseList(DevToolCommandResult result)
    {
        if (result.ExitCode == NoInstalledPackage) return [];
        if (result.ExitCode != 0) throw new InvalidOperationException(Failure(result));
        var text = Ansi.Replace(result.Stdout, "").Replace('\r', '\n');
        if (!text.Contains("-----", StringComparison.Ordinal)) throw new InvalidDataException("The installed winget inventory is not a complete table.");
        var rows = new List<WingetPackage>();
        foreach (var line in text.Split('\n'))
        {
            var match = Row.Match(line.Trim());
            if (!match.Success)
            {
                if (KnownId.IsMatch(line)) throw new InvalidDataException("A known developer package has an unreadable installed winget row.");
                continue;
            }
            var available = match.Groups["available"];
            rows.Add(new(match.Groups["id"].Value, match.Groups["name"].Value.Trim(), match.Groups["version"].Value,
                available.Success ? available.Value : null, match.Groups["source"].Value));
        }
        if (rows.GroupBy(r => r.Id, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new InvalidDataException("The installed winget inventory has ambiguous package matches.");
        return rows;
    }

    private static void ValidateId(string id)
    {
        if (!PackageId.IsMatch(id)) throw new ArgumentException("This package is not a standalone developer tool.", nameof(id));
    }

    // Other IDs can be detected, but no untrialled recipe is allowed to install or upgrade them.
    internal static bool HasTrial(string id) => id is "Git.Git" or "OpenJS.NodeJS.22" or "Python.Python.3.13" or "Python.Python.3.14";

    public static DevToolCommand UpgradeCommand(string id)
    {
        ValidateId(id);
        return new("winget", ["upgrade", "--id", id, "-e", "--source", "winget", "--silent", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"]);
    }

    internal static DevToolCommand ListCommand(IReadOnlyList<string> query) => new("winget", new[] { "list" }.Concat(query).Concat(["--accept-source-agreements", "--disable-interactivity"]).ToArray());

    private async Task<WingetPackage?> Installed(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return ParseList(await run(ListCommand(query), cancellation)).SingleOrDefault(p => p.Id == packageId);
    }

    public async Task<string?> InstalledVersion(CancellationToken cancellation) => (await Installed(cancellation))?.Version;
    public async Task<string?> LatestVersion(CancellationToken cancellation)
    {
        var p = await Installed(cancellation);
        return p?.Source == "winget" ? p.Available : null; // Missing Available is not a public current-version listing.
    }

    internal static string Failure(DevToolCommandResult result) => $"Updater exit {result.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"}: {(result.Stdout + result.Stderr).Trim()}";

    private async Task<IReadOnlyList<GitLockingProcess>> Holding(CancellationToken cancellation)
    {
        try { return await gitLocks!(cancellation); }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return []; } // an unreadable look never blocks the update
    }

    // Only what holds Git at both looks: a process that came and went in between, or one first seen at the second look, would not have stopped the installer (ED-NEW-providersmerge-git-2).
    private async Task<IReadOnlyList<GitLockingProcess>> StillHolding(CancellationToken cancellation)
    {
        var first = await Holding(cancellation);
        if (first.Count == 0) return first;
        await pause(SecondLook, cancellation);
        var second = await Holding(cancellation);
        return second.Where(now => first.Any(before => before.Same(now))).ToList();
    }

    // What the installer listed, with the apps that started it when the live look can still see them.
    private async Task<string> InUseMessage(string log, CancellationToken cancellation)
    {
        var listed = GitInUse.NamesFromLog(log);
        if (gitLocks is not null && await Holding(cancellation) is { Count: > 0 } live) return GitInUse.Message(live);
        return GitInUse.Message(listed.Select(name => new GitLockingProcess(name)).ToList());
    }

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var before = await Installed(cancellation);
        if (before is null) return new(false, null, false, false, "The tool is no longer installed.");
        if (before.Source != "winget") return new(false, before.Version, false, false, "This installed tool is not matched to the winget source.");
        if (!HasTrial(packageId)) return new(false, before.Version, false, false, "This installed tool's upgrade has not been supervised and recorded.");
        if (gitLocks is not null && await StillHolding(cancellation) is { Count: > 0 } holding)
            return new(false, before.Version, false, false, GitInUse.Message(holding)); // the installer would refuse: nothing is started
        progress.Report(null);
        var result = await run(UpgradeCommand(packageId), cancellation);
        var inUse = packageId == "Git.Git" && result.ExitCode is not (null or 0)
            && result.InstallerLog is { } log && log.Contains("The following process(es) use Git for Windows", StringComparison.Ordinal)
            && log.Contains("Defaulting to Cancel", StringComparison.Ordinal) && log.Contains("aborting", StringComparison.Ordinal);
        var inUseMessage = inUse ? await InUseMessage(result.InstallerLog!, cancellation) : "";
        WingetPackage? after;
        try { after = await Installed(cancellation); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            return new(false, null, false, false, inUse ? inUseMessage : Failure(result) + "\nInstalled version could not be verified: " + error.Message);
        }
        if (after is null) return new(false, null, false, false, inUse ? inUseMessage : Failure(result) + "\nInstalled version could not be verified.");
        var changed = before.Version != after.Version;
        var accepted = result.ExitCode is 0 or NoApplicableUpdate;
        var stillDue = after.Available is not null && !VersionText.Same(after.Version, after.Available);
        var reachedExpected = before.Available is not null ? VersionText.Same(after.Version, before.Available) : result.ExitCode == NoApplicableUpdate;
        var success = accepted && after.Source == "winget" && !stillDue && reachedExpected;
        return new(success, after.Version, success && changed, false,
            inUse ? inUseMessage :
            success ? result.Stdout.Trim() : Failure(result) + (stillDue ? "\nThe installed version is still out of date." : "")
                + (!reachedExpected ? "\nThe expected installed version was not verified." : ""));
    }
}
