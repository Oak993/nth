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

    public WingetUpdater(string id, string title, Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, IReadOnlyList<string>? query = null)
    {
        ValidateId(id);
        packageId = id; Title = title; this.run = run ?? DevToolProcess.Refuse;
        this.query = query?.ToArray() ?? ["--id", id, "-e"];
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

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var before = await Installed(cancellation);
        if (before is null) return new(false, null, false, false, "The tool is no longer installed.");
        if (before.Source != "winget") return new(false, before.Version, false, false, "This installed tool is not matched to the winget source.");
        if (!HasTrial(packageId)) return new(false, before.Version, false, false, "This installed tool's upgrade has not been supervised and recorded.");
        progress.Report(null);
        var result = await run(UpgradeCommand(packageId), cancellation);
        WingetPackage? after;
        try { after = await Installed(cancellation); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            return new(false, null, false, false, Failure(result) + "\nInstalled version could not be verified: " + error.Message);
        }
        if (after is null) return new(false, null, false, false, Failure(result) + "\nInstalled version could not be verified.");
        var changed = before.Version != after.Version;
        var accepted = result.ExitCode is 0 or NoApplicableUpdate;
        var stillDue = after.Available is not null && !VersionText.Same(after.Version, after.Available);
        var reachedExpected = before.Available is not null ? VersionText.Same(after.Version, before.Available) : result.ExitCode == NoApplicableUpdate;
        var success = accepted && after.Source == "winget" && !stillDue && reachedExpected;
        return new(success, after.Version, success && changed, false,
            success ? result.Stdout.Trim() : Failure(result) + (stillDue ? "\nThe installed version is still out of date." : "")
                + (!reachedExpected ? "\nThe expected installed version was not verified." : ""));
    }
}
