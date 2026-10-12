// PORT-ONLY: UE-3, BUILD-PLAN 2.4; returns installed tools only, for UE-2's item model.
using Marrow.Core.Port.Updates;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Updates;

public static class DevToolUpdateCatalog
{
    private static readonly Regex Version = new(@"\bv?(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:\.windows\.[0-9]+)?)\b", RegexOptions.CultureInvariant);
    private static readonly Regex PythonRegistration = new(@"\A\s*-V:(?<series>3\.[0-9]+)\s+\*?\s*(?<path>.+?python\.exe)\s*\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PythonDirectory = new(@"(?:\A|\\)Python(?:[0-9]+)?\\python\.exe\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static async Task<IReadOnlyList<IUpdateItem>> Discover(Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, Func<CancellationToken, Task<OfficeInstallation?>>? office = null, CancellationToken cancellation = default, Action<string, Exception>? inventoryFailure = null,
        Func<CancellationToken, Task<IReadOnlyList<GitLockingProcess>>>? gitLocks = null)
    {
        run ??= DevToolProcess.Refuse;
        office ??= OfficeC2RUpdater.RefuseRegistry;
        var items = new List<IUpdateItem>();
        await Tool("Git.Git", "Git", "git", ["--id", "Git.Git", "-e"]);
        await Tool("GitHub.cli", "GitHub CLI", "gh", ["--id", "GitHub.cli", "-e"]);
        await Tool(null, "Node.js", "node", ["--name", "Node.js"]);
        string[] pythonQuery = ["--name", "Python"];
        var python = await Packages("Python", pythonQuery);
        var matchedSeries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in python.Where(p => p.Id.StartsWith("Python.Python.", StringComparison.Ordinal)))
        {
            var series = p.Id["Python.Python.".Length..]; matchedSeries.Add(series);
            items.Add(new WingetUpdater(p.Id, "Python " + series, run, pythonQuery));
        }
        // Always inspect registrations: one winget match must not hide another installed minor.
        var registered = await Probe(new("py", ["-0p"]), "Python");
        var registrations = registered.ExitCode == 0 ? registered.Stdout.Split('\n').Select(line => PythonRegistration.Match(line.Trim())).Where(m => m.Success).ToArray() : [];
        foreach (var m in registrations)
        {
            if (matchedSeries.Contains(m.Groups["series"].Value)) continue;
            // Direct registered binary: py's newer install manager can auto-install a selector's missing runtime.
            await PythonProbe(m.Groups["path"].Value);
        }
        if (registrations.Length == 0)
        {
            var paths = await Probe(new("where.exe", ["python"]), "Python");
            if (paths.ExitCode == 0)
                foreach (var path in paths.Stdout.Split('\n').Select(line => line.Trim()).Where(path => PythonDirectory.IsMatch(path))) await PythonProbe(path);
        }
        OfficeInstallation? installedOffice;
        try { installedOffice = await office(cancellation); }
        catch (Exception error) when (inventoryFailure is not null && error is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            inventoryFailure("Office", error); installedOffice = null;
        }
        if (installedOffice is not null) items.Add(new OfficeC2RUpdater(office, run));
        return items;

        async Task Tool(string? exactId, string title, string executable, IReadOnlyList<string> query)
        {
            cancellation.ThrowIfCancellationRequested();
            var packages = await Packages(title, query);
            var matches = packages.Where(p => exactId is null ? p.Id.StartsWith("OpenJS.NodeJS", StringComparison.Ordinal) : p.Id == exactId).ToArray();
            foreach (var p in matches) items.Add(new WingetUpdater(p.Id, title, run, query, gitLocks)); // only Git's takes the look (ED-NEW-providers-git-1)
            if (matches.Length > 0) return;
            var probe = await Probe(new(executable, ["--version"]), title);
            var version = ProbeVersion(probe);
            if (version is not null) items.Add(new UnmatchedDevToolUpdater(executable, title, version));
        }

        async Task<IReadOnlyList<WingetPackage>> Packages(string title, IReadOnlyList<string> query)
        {
            try { return WingetUpdater.ParseList(await run(WingetUpdater.ListCommand(query), cancellation)); }
            catch (Exception error) when (inventoryFailure is not null && error is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Explicit production recovery: report an incomplete inventory, then inspect independent local binaries.
                // The default seam remains strict; a failure never becomes an unexplained empty table.
                inventoryFailure(title, error); return [];
            }
        }

        async Task PythonProbe(string path)
        {
            var version = ProbeVersion(await Probe(new(path, ["--version"]), "Python"));
            if (version is null) return;
            var parts = version.Split('.'); var series = parts[0] + "." + parts[1];
            if (!matchedSeries.Add(series)) return;
            items.Add(new UnmatchedDevToolUpdater("python:" + series, "Python " + series, version));
        }

        async Task<DevToolCommandResult> Probe(DevToolCommand command, string title)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return await run(command, cancellation); }
            catch (Exception error) when (inventoryFailure is not null && error is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                inventoryFailure(title, error); return new(null, "", error.Message);
            }
        }
    }

    private static string? ProbeVersion(DevToolCommandResult result)
    {
        if (result.ExitCode != 0) return null;
        var match = Version.Match(result.Stdout + result.Stderr);
        return match.Success ? match.Groups["version"].Value : null;
    }
}
