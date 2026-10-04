// PORT-ONLY: first-release feature unit SK-7. The small shape checks state.json uses on ids, hashes and paths.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Skills.State;

internal static partial class StateRules
{
    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256();

    [GeneratedRegex(@"^(?:[A-Za-z]:[\\/]|\\\\|/)")] private static partial Regex Rooted();

    public static bool IsId(string? text) => !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsWhiteSpace);

    public static bool IsSha256(string? text) => text is not null && Sha256().IsMatch(text);

    /// <summary>
    /// A Windows drive path (either slash), a UNC path or a rooted path, with no control character. A path the probe returns is stored as the probe wrote it;
    /// a relative path, a drive-relative one (<c>C:python.exe</c>) and an unexpanded variable are refused (ED-295).
    /// </summary>
    public static bool IsAbsolutePath(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Rooted().IsMatch(text) && !text.Any(char.IsControl) && !text.Contains('%', StringComparison.Ordinal);
}
