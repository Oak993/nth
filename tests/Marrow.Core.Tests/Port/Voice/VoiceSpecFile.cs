using System.Runtime.CompilerServices;

namespace Marrow.Core.Tests.Port.Voice;

// Test support for the VT-1 unit: reads the first-release voice specification that sits in the repository, so an
// expectation is transcribed from the spec's own tables (R-TEST-4) and not from the port's code.
internal static class VoiceSpecFile
{
    internal static string Text() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "porting", "first-release", "voice-SPEC.md"));

    /// <summary>The cells of every table row between the line starting <paramref name="from"/> and the line starting <paramref name="to"/>.</summary>
    internal static List<string[]> TableRows(string from, string to)
    {
        var lines = Text().Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith(from, StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith(to, StringComparison.Ordinal));
        if (start < 0 || end < 0) throw new InvalidOperationException("spec section not found: " + from);
        var rows = new List<string[]>();
        for (var i = start; i < end; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (!line.StartsWith('|')) continue;
            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':'))) continue;
            rows.Add(cells);
        }
        return rows;
    }

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".."));
}
