using System.IO;
using System.Linq;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalFont.swift (B2 1fbf81e), :1-139.
//
// ED-175: NSFont is F.SwiftFont (Marrow.UI.Port), which this unit extended with Named, FamilyName, PointSize,
// Covers and AvailableFontFamilies for exactly what this file needs (SwiftFont.cs, ED-175 (1)). Ghostty's reader
// (:33-66) is ported literally, the parser exactly per PR-TERM-127, over an injected home folder (ED-175 (2)) so
// a test never touches the real one. iTerm2's and Terminal.app's readers (:68-90) are inert on Windows: their
// macOS preference domains (UserDefaults(suiteName:)) and, for Terminal.app, the NSKeyedUnarchiver of its
// archived Font, have no Windows counterpart (a named platform adaptation, ED-175 (2); no ruling adds a Windows
// terminal to read instead). NSWorkspace.runningApplications (:26) has no Windows counterpart either and is
// always empty, so configuredFonts() keeps the Swift's written reader order: Ghostty, iTerm2, Terminal.app
// (ED-175 (2)).
public static class TerminalFont
{
    // :7-16 preferred(fallbackSize:)
    /// <summary>The configured terminal font that carries the icons, or the closest thing to it.</summary>
    public static SwiftFont Preferred(double fallbackSize = 12) => Preferred(fallbackSize, LoginEnvironment.HomeDirectory);

    /// <summary>The tests' seam (ED-175 (2)): <paramref name="home"/> stands in for the Ghostty reader's home
    /// folder (FileManager.default.homeDirectoryForCurrentUser) all the way through configuredFonts(), so a test
    /// of the whole preferred() chain never touches the real profile; the public overload passes
    /// LoginEnvironment.HomeDirectory.</summary>
    internal static SwiftFont Preferred(double fallbackSize, string home)
    {
        var configured = ConfiguredFonts(home); // :9
        var match = configured.FirstOrDefault(HasNerdGlyphs); // :10
        if (match is not null) return match;
        if (configured.Count > 0) // :11
        {
            var first = configured[0];
            return NerdSibling(first) ?? FirstInstalledNerdFont(first.PointSize) ?? first; // :12
        }
        return FirstInstalledNerdFont(fallbackSize) // :14
            ?? SwiftFont.MonospacedSystemFont(fallbackSize, SwiftFont.Weight.Regular); // :15 (Cascadia Mono, OD-0009)
    }

    // :18-29 configuredFonts(). ED-175 (2): the running-applications partition (:26-27) is a platform no-op on
    // Windows (NSWorkspace.runningApplications does not exist there), so this calls the readers directly in the
    // written order rather than reproducing a partition that would always be the identity.
    private static IReadOnlyList<SwiftFont> ConfiguredFonts(string home)
    {
        var readers = new Func<SwiftFont?>[] { () => GhosttyFont(home), ITermFont, AppleTerminalFont }; // :22-24
        var fonts = new List<SwiftFont>();
        foreach (var reader in readers)
            if (reader() is { } font) fonts.Add(font); // :28
        return fonts;
    }

    // ---- Terminals ----

    // :33-58 ghosttyFont(). The tests' seam (ED-175 (2)): <paramref name="home"/> stands in for
    // FileManager.default.homeDirectoryForCurrentUser; the public chain passes Shell.HomeDirectory.
    internal static SwiftFont? GhosttyFont(string home)
    {
        foreach (var path in GhosttyConfigPaths(home)) // :35
        {
            // :36 `try? String(contentsOf: url, encoding: .utf8)`: a missing file and a decode failure both
            // fall through to the next config path (R-STR-8, R-ERR-1).
            var bytes = SwiftTry.Optional(() => File.ReadAllBytes(path));
            if (bytes is null) continue;
            var text = SwiftText.DecodeUtf8OrNil(bytes);
            if (text is null) continue;

            string? name = null; // :37
            var size = 12.0; // :38
            foreach (var rawLine in SwiftString.ComponentsSeparatedByNewlines(text)) // :39
            {
                var line = FoundationText.Trim(rawLine); // :40 .whitespaces
                if (SwiftString.HasPrefix(line, "#")) continue; // :41
                var equal = line.IndexOf('=', StringComparison.Ordinal);
                if (equal < 0) continue; // :41
                var key = FoundationText.Trim(line[..equal]); // :42
                var value = FoundationText.Trim(line[(equal + 1)..]); // :43
                switch (key) // :44
                {
                    case "font-family" when name is null: // :45
                        // :46-47 a family list is allowed; the first name is the primary one.
                        var parts = SwiftString.Split(value, ",");
                        var first = parts.Count > 0 ? parts[0] : value;
                        name = TrimCharacterSet(first, " \""); // :48
                        break;
                    case "font-size": // :49
                        if (SwiftNumber.ParseDouble(value) is { } points && points > 0) size = points; // :50
                        break;
                }
            }
            if (name is { Length: > 0 } && Resolve(name, size) is { } font) return font; // :55
        }
        return null; // :57
    }

    // :60-66 ghosttyConfigURLs. ED-175 (2): the two paths under the home folder, joined with Path.Join (the
    // decision's own wording), rather than the port's usual URL unit (R-PATH-1), which has no analog for this.
    internal static IReadOnlyList<string> GhosttyConfigPaths(string home) => new[]
    {
        Path.Join(home, "Library", "Application Support", "com.mitchellh.ghostty", "config"), // :63
        Path.Join(home, ".config", "ghostty", "config"), // :64
    };

    // :68-76 terminalFont(). Renamed: a member cannot share the identifier of its enclosing type in C#
    // (TerminalFont the type vs. terminalFont() the member). ED-175 (2): Terminal.app's preference domain and
    // its archived Font's NSKeyedUnarchiver have no Windows counterpart; inert (a named platform adaptation).
    private static SwiftFont? AppleTerminalFont() => null; // Swift: terminalFont()

    // :78-90 iTermFont(). ED-175 (2): iTerm2's preference domain (UserDefaults(suiteName: "com.googlecode.iterm2"))
    // has no Windows counterpart; inert (a named platform adaptation). Its "Normal Font" also names a PostScript
    // font, which ED-175 (1) says does not resolve on Windows even were this reader ported.
    private static SwiftFont? ITermFont() => null;

    // ---- Glyphs ----

    // :94-96 nerdProbes: a powerline cap, two Font Awesome marks and a Material one from the supplementary plane.
    private static readonly int[] NerdProbes = { 0xE0B0, 0xE0B6, 0xF015, 0xF0035 };

    // :98-101 hasNerdGlyphs(_:)
    private static bool HasNerdGlyphs(SwiftFont font) => NerdProbes.All(font.Covers);

    // :103-112 nerdSibling(of:)
    private static SwiftFont? NerdSibling(SwiftFont font)
    {
        if (font.FamilyName is not { } family) return null; // :106
        var target = Normalized(family) + "nf"; // :107
        var match = SwiftFont.AvailableFontFamilies.FirstOrDefault(candidate => Normalized(candidate) == target); // :108-110
        return match is null ? null : SwiftFont.Named(match, font.PointSize); // :111
    }

    // :114-119 firstInstalledNerdFont(size:)
    private static SwiftFont? FirstInstalledNerdFont(double size)
    {
        var family = SwiftFont.AvailableFontFamilies.FirstOrDefault(
            candidate => SwiftString.LocalizedCaseInsensitiveContains(candidate, "nerd font")); // :115-117 R-STR-6
        return family is null ? null : SwiftFont.Named(family, size); // :118
    }

    // :121-130 resolve(_:size:)
    private static SwiftFont? Resolve(string name, double size)
    {
        if (SwiftFont.Named(name, size) is { } named) return named; // :124
        var target = Normalized(name); // :125
        var match = SwiftFont.AvailableFontFamilies.FirstOrDefault(candidate => Normalized(candidate) == target); // :126-128
        return match is null ? null : SwiftFont.Named(match, size); // :129
    }

    // :132-137 normalized(_:). R-STR-4 (ED-009): lowercased() is SwiftString.Lowercased, never ToLowerInvariant.
    private static string Normalized(string name) =>
        SwiftString.Lowercased(name)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("nerdfont", "nf", StringComparison.Ordinal);

    // :48 trimmingCharacters(in: CharacterSet(charactersIn:)): trims exactly the given character set from both
    // ends (same approach as UI/Markdown/MarkdownParser.cs:490's TrimCharacterSet; R-STR-7 forbids string.Trim()
    // for this).
    private static string TrimCharacterSet(string text, string charset)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && charset.IndexOf(text[start], StringComparison.Ordinal) >= 0) start++;
        while (end > start && charset.IndexOf(text[end - 1], StringComparison.Ordinal) >= 0) end--;
        return text[start..end];
    }
}
