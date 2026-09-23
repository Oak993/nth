using System.Text.RegularExpressions;

namespace Marrow.Testing;

/// <summary>
/// R-TEST-9's scratch-folder variable (MARROW_PORT_DATA_DIR), normalized so a value an agent set Unix-style from
/// Git Bash ("/c/Users/x/Temp/y") works exactly like its Windows form ("C:\Users\x\Temp\y"). Git Bash's own MSYS
/// path conversion only rewrites arguments handed to a spawned child process, not a plain shell "export", so the
/// environment block a test host inherits can still carry the raw MSYS spelling. Downstream, an MSYS-style root
/// breaks anything that treats the path as a literal Windows path or passes it to a Windows-only tool (e.g.
/// "mklink /J" reading a leading "/c" as a switch), so every test fixture reads the variable through here instead
/// of Environment.GetEnvironmentVariable directly.
/// </summary>
public static class TestDataDirectory
{
    public const string VariableName = "MARROW_PORT_DATA_DIR";

    // Matches a leading MSYS/Git-Bash drive component: "/c", "/c/...". The drive letter is group 1; anything
    // from the following slash onward (if any) is left in the tail so it can be reattached with backslashes.
    private static readonly Regex MsysDrive = new(@"^/([A-Za-z])(/|$)", RegexOptions.Compiled);

    /// <summary>The normalized value of MARROW_PORT_DATA_DIR, or null when it is unset (same as today).</summary>
    public static string? Read() => Normalize(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>
    /// An MSYS/Git-Bash drive path becomes its Windows form ("/c/Users/x" -&gt; "C:\Users\x"; "/c" -&gt; "C:\");
    /// any other non-empty value (already a Windows path, or a relative one) is made absolute and canonical with
    /// Path.GetFullPath, exactly as an unconverted value would have been used before. Null or empty passes through
    /// unchanged, so callers keep their existing "not set" handling.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var match = MsysDrive.Match(value);
        if (match.Success)
        {
            var drive = match.Groups[1].Value.ToUpperInvariant();
            value = drive + ":\\" + value[match.Length..];
        }
        return Path.GetFullPath(value.Replace('/', '\\'));
    }
}
