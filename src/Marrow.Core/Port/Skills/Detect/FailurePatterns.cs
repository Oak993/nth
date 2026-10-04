// PORT-ONLY: first-release feature unit SK-10. The missing-program and missing-module patterns of ONDEMAND-PLAN.md 5.3, over the text of a finished command.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Skills.Detect;

internal static partial class FailurePatterns
{
    public const string PlaywrightMessage = "Executable doesn't exist at";

    [GeneratedRegex(@"(?<name>[^\s:]+):\s*(?:command not found|Befehl nicht gefunden)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex BashNotFound();

    // cmd's English sentence. The process exit code of cmd /c is 1 here, not 9009 (recorded on Windows 11 10.0.26200; ED-296 review addendum).
    [GeneratedRegex(@"'(?<name>[^'\r\n]+)' is not recognized as an internal or external command")] private static partial Regex CmdNotRecognized();

    // "+ CategoryInfo : ObjectNotFound: (frobnicate:String) [], CommandNotFoundException" (recorded, Windows PowerShell 5.1; the category and the type are identifiers).
    [GeneratedRegex(@"ObjectNotFound: \((?<name>[^\r\n]+?):String\) \[\], CommandNotFoundException")] private static partial Regex PowerShellCategory();

    [GeneratedRegex(@"No module named ['""]?(?<name>[A-Za-z0-9_][A-Za-z0-9_.]*)")] private static partial Regex PythonModule();

    [GeneratedRegex(@"Cannot find (?:module|package) ['""](?<name>[^'""]+)['""]")] private static partial Regex NodeModule();

    [GeneratedRegex(@"[^\p{L}\p{N}_.+\-]+")] private static partial Regex WordBreak();

    [GeneratedRegex(@"^(?:[A-Za-z]:[\\/]|\\\\|/|\.)")] private static partial Regex PathLike();

    /// <summary>The exit code says it failed, or, with no exit code, the provider's error flag does.</summary>
    public static bool Failed(CommandFinished finished) => finished.ExitCode is { } code ? code != 0 : finished.IsError;

    /// <summary>
    /// The programs a failed command reports as missing: cmd's exit code 9009 or PowerShell's CommandNotFoundException identifier (then every word of the output counts as
    /// named, whatever the language, plus python3 when the Store alias names Python), or bash's "command not found" with exit code 127 (or none), then only the name in front
    /// of that phrase, or cmd's English "is not recognized as an internal or external command" with any failing exit code (cmd /c exits 1, its 9009 stays inside), then
    /// only the quoted name. Names are normalised like a program. The detector keeps only the ones that are also tokens of the command (ED-296).
    /// </summary>
    public static HashSet<string> NamedMissingPrograms(CommandFinished finished)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        if (!Failed(finished)) return named;
        var output = finished.Output;
        // Windows PowerShell's error record names the program in its CategoryInfo and also echoes the source line, so when that line is there only its names count.
        var categories = PowerShellCategory().Matches(output);
        if (categories.Count > 0)
            foreach (Match match in categories) named.Add(CommandTokenizer.NormalizeProgram(match.Groups["name"].Value.Trim('\'', '"')));
        else if (finished.ExitCode == 9009 || output.Contains("CommandNotFoundException", StringComparison.Ordinal))
        {
            foreach (var word in WordBreak().Split(output))
            {
                var trimmed = word.Trim('.', '-', '+');
                if (trimmed.Length > 0) named.Add(CommandTokenizer.NormalizeProgram(trimmed));
            }
            if (finished.ExitCode == 9009 && named.Contains("python")) named.Add("python3");
        }
        foreach (Match match in CmdNotRecognized().Matches(output))
            named.Add(CommandTokenizer.NormalizeProgram(match.Groups["name"].Value.Trim('"')));
        if (finished.ExitCode is null or 127)
            foreach (Match match in BashNotFound().Matches(output))
                named.Add(CommandTokenizer.NormalizeProgram(match.Groups["name"].Value));
        return named;
    }

    /// <summary>The top-level Python module names of "No module named" lines, in order, once each.</summary>
    public static IReadOnlyList<string> PythonModules(string output)
    {
        var names = new List<string>();
        foreach (Match match in PythonModule().Matches(output))
        {
            var top = match.Groups["name"].Value.Split('.')[0];
            if (top.Length > 0 && !names.Contains(top, StringComparer.Ordinal)) names.Add(top);
        }
        return names;
    }

    /// <summary>The package names of Node's "Cannot find module/package" lines, when the error code MODULE_NOT_FOUND or ERR_MODULE_NOT_FOUND is in the output too.</summary>
    public static IReadOnlyList<string> NodeModules(string output)
    {
        var names = new List<string>();
        if (!output.Contains("MODULE_NOT_FOUND", StringComparison.Ordinal)) return names;
        foreach (Match match in NodeModule().Matches(output))
        {
            var specifier = match.Groups["name"].Value;
            if (PathLike().IsMatch(specifier) || specifier.StartsWith("node:", StringComparison.Ordinal)) continue;
            var parts = specifier.Split('/');
            var package = specifier.StartsWith('@') ? (parts.Length >= 2 ? parts[0] + "/" + parts[1] : specifier) : parts[0];
            if (!names.Contains(package, StringComparer.Ordinal)) names.Add(package);
        }
        return names;
    }

    public static bool PlaywrightMissingBrowser(string output) => output.Contains(PlaywrightMessage, StringComparison.Ordinal);
}
