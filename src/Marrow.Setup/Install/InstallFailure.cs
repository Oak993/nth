using System.Globalization;
using System.Text;
using Marrow.Setup.Text;

namespace Marrow.Setup.Install;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, "Error reasons" and "Copy details"; unit U3, ED-NEW-inst-u3-1). Why an install failed,
/// in Problem's words: the reason first (from the free-space check, Setup.exe's exit code, the log and the last step
/// reached), then whether anything was left. "Nothing was changed on your PC." is said only when it is true: nothing of
/// Velopack's had started, or the root holds the old install again with no rollback folder left beside it, or a fresh
/// root was left empty. Otherwise: "Some of nth's files may be left. Retry repairs them."
/// </summary>
/// <param name="Reason">Which sentence leads.</param>
/// <param name="NothingChanged">True when the second line may say nothing was changed.</param>
/// <param name="ExitCode">Setup.exe's exit code, or null when it never ran (or never ended).</param>
/// <param name="Step">The last step reached (for Copy details).</param>
/// <param name="Space">The free-space check behind <see cref="FailureReason.NoSpace"/>.</param>
/// <param name="Log">The log's last lines.</param>
public sealed record InstallFailure(FailureReason Reason, bool NothingChanged, int? ExitCode, InstallStep Step, FreeSpaceVerdict? Space, IReadOnlyList<string> Log)
{
    /// <summary>Problem's first sentence.</summary>
    public string Sentence => Reason switch
    {
        FailureReason.NoSpace when Space?.Message is { } message => message,
        FailureReason.NoSpace when Space is { } space => SetupStrings.ProblemSpace(space.Drive, space.ShownMegabytes, space.FreeMegabytes),
        FailureReason.InUse => SetupStrings.ProblemInUse,
        FailureReason.Blocked => SetupStrings.ProblemBlocked,
        _ => SetupStrings.ProblemOther,
    };

    /// <summary>Problem's second line.</summary>
    public string SecondLine => NothingChanged ? SetupStrings.ProblemSafe : SetupStrings.ProblemLeft;

    /// <summary>
    /// What Copy details copies: the setup version, the Windows build, the step, the exit code and the last 40 log lines,
    /// with <c>C:\Users\&lt;name&gt;</c> written as <c>%USERPROFILE%</c>.
    /// </summary>
    public string Details(string setupVersion, string windows, int build, string architecture, string? userProfile)
    {
        var text = new StringBuilder();
        text.AppendLine(SetupStrings.ProblemHeader(setupVersion, windows, build, architecture));
        AppendDetailLines(text);
        return Scrub(text.ToString().TrimEnd(), userProfile);
    }

    /// <summary>
    /// What <see cref="Details"/> holds below its header line (the step, the exit code, the reason and the log's last lines): the
    /// session hands this to Problem, whose Copy details writes the header line itself.
    /// </summary>
    public string DetailLines(string? userProfile)
    {
        var text = new StringBuilder();
        AppendDetailLines(text);
        return Scrub(text.ToString().TrimEnd(), userProfile);
    }

    private void AppendDetailLines(StringBuilder text)
    {
        text.Append("Step: ").AppendLine(Step.ToString());
        text.Append("Exit code: ").AppendLine(ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "none");
        text.Append("Reason: ").AppendLine(Reason.ToString());
        foreach (var line in Log) text.AppendLine(line);
    }

    /// <summary><paramref name="text"/> with the user's profile folder (any case) written as <c>%USERPROFILE%</c>.</summary>
    public static string Scrub(string text, string? userProfile)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(userProfile)) return text;
        var profile = Path.TrimEndingDirectorySeparator(userProfile);
        return profile.Length < 4 ? text : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether an install that failed after Setup.exe started left the PC as it was. SOURCE FACT (Velopack 1.2.161,
    /// install.rs:92-163): a root that was not empty is renamed to <c>&lt;root&gt;.&lt;16 random&gt;</c> first and renamed back on
    /// failure, with every error of that restore ignored and not logged, so it is read from the folders: the old version
    /// is readable in the root again and no rollback folder is beside it that was not there before the run (a rename keeps
    /// a folder's times, so the folders are compared by name with <paramref name="rollbackBefore"/>). A root that was empty
    /// (a fresh install) has nothing to restore; Velopack leaves whatever it wrote, so it is unchanged only if still empty.
    /// </summary>
    public static bool Unchanged(string root, string? versionBefore, IReadOnlyCollection<string> rollbackBefore, Func<string, string?> readVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(rollbackBefore);
        ArgumentNullException.ThrowIfNull(readVersion);
        try
        {
            if (RollbackFolders(root).Any(folder => !rollbackBefore.Contains(folder, StringComparer.OrdinalIgnoreCase))) return false;
            if (versionBefore is not null) return string.Equals(readVersion(root), versionBefore, StringComparison.Ordinal);
            return !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The folders beside <paramref name="root"/> named as Velopack names its rollback copy (<c>with_extension(random_string(16))</c>).</summary>
    public static IReadOnlyList<string> RollbackFolders(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent)) return [];
        var stem = Path.GetFileNameWithoutExtension(full) + ".";
        var found = new List<string>();
        try
        {
            foreach (var folder in new DirectoryInfo(parent).EnumerateDirectories(stem + "*"))
            {
                // Windows matches "nth.*" against "nth" too (an empty extension), so the name is checked by hand.
                if (folder.Name.Length != stem.Length + 16 || !folder.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
                var tail = folder.Name[stem.Length..];
                if (tail.Length == 16 && tail.All(char.IsAsciiLetterOrDigit) && !string.Equals(folder.FullName, full, StringComparison.OrdinalIgnoreCase)) found.Add(folder.FullName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return found;
    }
}
