namespace Marrow.Core.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md section 7, unit U7; ED-NEW-inst-u7-1). The running app's own executable. The published exe is
/// <c>nth.exe</c> (scripts/package-windows.ps1 renames the apphost), a Debug or Release build keeps <c>Marrow.App.exe</c>, so
/// nothing may build the file name by hand: the process's own path is the only name that is always right. The three child
/// processes that re-launch the app (the Cursor skills relay, the advisor relay and the ACP skills relay) take it from here.
/// </summary>
public static class AppExecutable
{
    /// <summary>The file name of the published app (the apphost, renamed by the packaging script).</summary>
    public const string PublishedName = "nth.exe";

    /// <summary>
    /// The path of the running app: <see cref="Environment.ProcessPath"/>, unless that is the .NET host (a run through
    /// <c>dotnet Marrow.App.dll</c>), in which case the published name beside the app's own files.
    /// </summary>
    public static string Path => Resolve(Environment.ProcessPath, AppContext.BaseDirectory);

    /// <summary>The rule behind <see cref="Path"/>, with its two inputs given.</summary>
    public static string Resolve(string? processPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath) && !IsDotnetHost(processPath)) return processPath;
        return System.IO.Path.Combine(baseDirectory, PublishedName);
    }

    private static bool IsDotnetHost(string processPath) =>
        string.Equals(System.IO.Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
}
