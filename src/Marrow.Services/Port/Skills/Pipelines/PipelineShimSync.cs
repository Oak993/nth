using System.Security.Cryptography;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;

/// <summary>
/// PORT-ONLY: RR-14 (ONDEMAND-PLAN.md 3.6, ED-NEW-dl-10). Keeps <c>addons\bin</c> equal to what the selected pins publish: one copy of the NativeAOT launcher and its routing sidecar per
/// name a selected route publishes and can resolve now (a link whose item is not installed is not published), and nothing else. A sidecar names only the state file and the name, so every run
/// resolves the pin state.json selects at that moment (SK-8); an update needs no rewrite. <c>bin\.launcher</c> records the SHA-256 of the launcher the copies were made from, so a new app
/// version's launcher replaces them once. A file a running shim holds is left and retried at the next sync. Without the launcher (a development run with no published shim) nothing is
/// written and the reason is returned.
/// </summary>
public static class PipelineShimSync
{
    /// <summary>The launcher the release ships beside the app (scripts/package-windows.ps1).</summary>
    public static string Launcher => Path.Combine(AppContext.BaseDirectory, "Marrow.Shim.exe");

    /// <summary>The names the selected pins publish that resolve now, in name order.</summary>
    public static IReadOnlyList<string> Published(StorePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var routes = PipelineRoutes.Read(paths.StateFile);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in routes.SelectMany(r => r.Shims.Keys.Concat(r.Links?.Keys ?? Enumerable.Empty<string>())))
        {
            try { _ = PipelineRoutingReader.Resolve(routes, name, paths.Root); names.Add(name); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException) { /* not runnable now: not published */ }
        }
        return names.ToArray();
    }

    public static string? Sync(StorePaths paths, string? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        launcher ??= Launcher;
        var bin = Path.Combine(paths.Addons, "bin");
        StorePaths.EnsureAncestors(bin);
        var names = Published(paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? problem = null;
        if (names.Count > 0 && !File.Exists(launcher)) problem = "The shim launcher is not beside the app: " + launcher;
        else if (names.Count > 0)
        {
            Directory.CreateDirectory(bin);
            var stamp = Path.Combine(bin, ".launcher");
            string digest;
            using (var input = File.OpenRead(launcher)) digest = Convert.ToHexStringLower(SHA256.HashData(input));
            var current = File.Exists(stamp) && File.ReadAllText(stamp).Trim() == digest;
            var complete = true;
            foreach (var name in names)
            {
                var batch = Path.GetExtension(name).Equals(".cmd", StringComparison.OrdinalIgnoreCase);
                var executable = batch ? Path.Combine(bin, ".launchers", Path.ChangeExtension(name, ".exe")) : Path.Combine(bin, name);
                var sidecar = Path.ChangeExtension(executable, ".shim");
                try
                {
                    if (current && File.Exists(executable) && File.Exists(sidecar) && (!batch || File.Exists(Path.Combine(bin, name))))
                    {
                        var written = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(sidecar), ShimJsonContext.Default.ShimDefinition);
                        if (written?.RoutingStateFile == paths.StateFile && written.RoutingName == name) continue;
                    }
                    PipelineShimFiles.Write(bin, name, launcher, paths.StateFile);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { complete = false; /* held by a running shim: the next sync */ }
            }
            if (complete && !current) File.WriteAllText(stamp, digest + "\n");
        }
        if (!Directory.Exists(bin)) return problem;
        var launchers = Path.Combine(bin, ".launchers");
        foreach (var entry in Directory.EnumerateFiles(bin).Concat(Directory.Exists(launchers) ? Directory.EnumerateFiles(launchers) : []))
        {
            var file = Path.GetFileName(entry);
            if (file == ".launcher" && names.Count > 0) continue;
            var published = Path.GetDirectoryName(entry) == launchers
                ? names.Contains(Path.ChangeExtension(file, ".cmd"))
                : names.Contains(file) || names.Contains(Path.ChangeExtension(file, ".exe"));
            if (published) continue;
            try { File.SetAttributes(entry, FileAttributes.Normal); File.Delete(entry); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* held by a running shim: the next sync */ }
        }
        return problem;
    }
}
