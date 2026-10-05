using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: the D-VN page's own data (porting/first-release/design-src/voice-notes/voice-notes.html, the JSON block id="vndata"), read from the
// repository so the strings the tests compare to are the page's bytes, not a copy of them.
internal static class VoiceNotesPage
{
    internal static string PagePath() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "..", "porting", "first-release", "design-src", "voice-notes", "voice-notes.html"));

    /// <summary>The page's own script (vn-page.js): the look-A row's strings are drawn there.</summary>
    internal static string Script() =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "..", "porting", "first-release", "design-src", "voice-notes", "src", "vn-page.js")));

    private static string ThisFile([CallerFilePath] string here = "") => here;

    private static readonly Lazy<JsonDocument> Data = new(Load);

    private static JsonDocument Load()
    {
        var html = File.ReadAllText(PagePath());
        var open = html.IndexOf("id=\"vndata\"", StringComparison.Ordinal);
        if (open < 0) throw new InvalidOperationException("the page has no vndata block");
        var start = html.IndexOf('>', open) + 1;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end]);
    }

    internal static JsonElement Root => Data.Value.RootElement;

    /// <summary>The page's case ids in its order (blocked first).</summary>
    internal static IReadOnlyList<string> CaseIds => [.. Root.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("id").GetString()!)];

    /// <summary>Set A's note for a case id: the words before the link, the link's words, the destination key.</summary>
    internal static (string Before, string Link, string Destination) SetA(string id)
    {
        var n = Root.GetProperty("sets").GetProperty("A").GetProperty("notes").GetProperty(id);
        return (n[0].GetString()!, n[1].GetString()!, n[2].GetString()!);
    }

    internal static (string Before, string Link, string Destination) Locked
    {
        get
        {
            var n = Root.GetProperty("locked");
            return (n[0].GetString()!, n[1].GetString()!, n[2].GetString()!);
        }
    }
}
