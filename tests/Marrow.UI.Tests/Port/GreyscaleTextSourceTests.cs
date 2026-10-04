using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Marrow.UI.Tests.Port;

// OD-0099 Q-TEXT A (ED-NEW-text-greyscale): text the port draws itself through SkiaSharp is greyscale too. No source file may
// ask Skia or Avalonia for subpixel (LCD) text, and every SKFont states its edging, so none falls to a platform default.
public sealed partial class GreyscaleTextSourceTests
{
    [GeneratedRegex(@"SubpixelAntialias|LcdRenderText|LcdText|SKFontEdging\.SubpixelAntialias|SKPixelGeometry\.(?:Rgb|Bgr)")] private static partial Regex Subpixel();

    private static IEnumerable<string> SourceFiles([CallerFilePath] string path = "")
    {
        var src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "src"));
        Assert.True(Directory.Exists(src), $"{src} is not the source tree");
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
    }

    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);

    [Fact]
    [Trait("Decision", "ED-107")]
    public void No_source_file_asks_for_subpixel_or_lcd_text()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles())
            foreach (Match match in Subpixel().Matches(WithoutComments(File.ReadAllText(file))))
                found.Add($"{Path.GetFileName(file)}: {match.Value}");
        Assert.Empty(found);
    }

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Every_SKFont_states_greyscale_edging()
    {
        var problems = new List<string>();
        var count = 0;
        foreach (var file in SourceFiles())
        {
            var text = WithoutComments(File.ReadAllText(file));
            for (var at = text.IndexOf("new SKFont", StringComparison.Ordinal); at >= 0; at = text.IndexOf("new SKFont", at + 1, StringComparison.Ordinal))
            {
                count++;
                // The initializer closes before the statement's semicolon, so read to the first semicolon after the brace block.
                var end = text.IndexOf(';', at);
                var brace = text.IndexOf('{', at);
                if (brace >= 0 && brace < end) end = text.IndexOf('}', brace);
                var statement = text[at..Math.Max(end, at)];
                if (!Regex.IsMatch(statement, @"Edging\s*=\s*SKFontEdging\.Antialias")) problems.Add($"{Path.GetFileName(file)}: {statement.Split('\n')[0].Trim()}");
            }
        }
        Assert.True(count > 0, "the scan found no SKFont, so it checks nothing");
        Assert.Empty(problems);
    }
}
