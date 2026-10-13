using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Setup.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 9 item 7 and 3, the canvas's Update "What's new in 1.1.0"; ED-NEW-inst-fix-1). The lines of
/// Update's "What's new" card, from the release notes the packaging script validated and embedded (<c>ReleaseNotes.md</c>,
/// Directory.Build.targets), read with the app's own parser (ReleaseNotesParser, the About digest's rules: the category
/// headers, the bullets and the inline markdown stripped). New features first, then refinements, then fixes; at most
/// <see cref="MaxLines"/>, the canvas's three, so the card stays a glance and the window does not grow for a long release.
/// A build without notes, or notes with no bullet under a known header, gives none, which hides the card.
/// </summary>
public static class SetupReleaseNotes
{
    public const string ResourceName = "ReleaseNotes.md";

    /// <summary>The canvas's card holds three lines.</summary>
    public const int MaxLines = 3;

    /// <summary>The lines from the notes embedded in this setup, or none.</summary>
    public static IReadOnlyList<string> Embedded()
    {
        try
        {
            using var stream = typeof(SetupReleaseNotes).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return [];
            using var reader = new StreamReader(stream);
            return Lines(reader.ReadToEnd());
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return []; // unreadable notes hide the card; they never stop the update
        }
    }

    /// <summary>The card's lines from release notes in the app's format.</summary>
    public static IReadOnlyList<string> Lines(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var parsed = ReleaseNotesParser.Parse(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        var lines = new List<string>(MaxLines);
        foreach (var category in (ReleaseNotesCategory[])[ReleaseNotesCategory.NewFeatures, ReleaseNotesCategory.Refinements, ReleaseNotesCategory.BugFixes])
        {
            if (!parsed.TryGetValue(category, out var items)) continue;
            foreach (var item in items)
            {
                if (lines.Count == MaxLines) return lines;
                lines.Add(item);
            }
        }
        return lines;
    }
}
