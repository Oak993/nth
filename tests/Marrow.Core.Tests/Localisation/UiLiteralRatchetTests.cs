using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22 "every new string in the resource files"; ED-NEW-web-w22-3). A ratchet: the user-interface source of the web
// surfaces holds no English-looking string literal except the ones listed as staying on purpose (stay-in-code.txt: CSS, ids, text for the model,
// protocol). A new word typed into a control instead of the resource files fails here, and the list cannot go stale: an entry whose literal is gone fails too.
[Trait("Decision", "ED-NEW-web-w22-3")]
public sealed class UiLiteralRatchetTests
{
    [Fact]
    public void TheScanSetNamesRealFilesAndFolders()
    {
        var root = UiTextCalls.SourceRoot();
        var missing = new List<string>();
        foreach (var raw in File.ReadAllLines(Path.Combine(UiLiterals.StringsFolder(), "scan-set.txt")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var path = Path.Combine(root, line.TrimEnd('/'));
            if (!(line.EndsWith('/') ? Directory.Exists(path) : File.Exists(path))) missing.Add(line);
        }
        Assert.Empty(missing);
        Assert.NotEmpty(UiLiterals.ScanSet());
    }

    [Fact]
    public void NoEnglishLookingLiteralIsLeftInTheWebUserInterfaceExceptTheOnesThatStayOnPurpose()
    {
        var allowed = UiLiterals.Allowed();

        var unlisted = UiLiterals.Scan(UiLiterals.ScanSet())
            .Where(literal => !allowed.Contains((literal.File, literal.Text)))
            .Select(literal => $"{literal.File}:{literal.Line} \"{literal.Text}\"")
            .ToList();

        Assert.Empty(unlisted);
    }

    [Fact]
    public void EveryLiteralListedAsStayingIsStillInTheSource()
    {
        var present = UiLiterals.Scan(UiLiterals.ScanSet()).Select(literal => (literal.File, literal.Text)).ToHashSet();

        var stale = UiLiterals.Allowed().Where(entry => !present.Contains(entry)).Select(entry => $"{entry.File} \"{entry.Text}\"").Order(StringComparer.Ordinal).ToList();

        Assert.Empty(stale);
    }

    [Fact]
    public void TheScannerSeesAWordInAControlAndSkipsAKeyAndAComment()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-literal-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllLines(Path.Combine(folder, "Sample.cs"),
            [
                "// \"A comment about Something\"",
                "var a = new TextBlock { Text = \"Open the file\" };",
                "var b = UiText.Get(\"visus.toolbar.back\");",
                "var c = \"css-class\";",
                "var d = UiText.Format(\"visus.x.y\", \"Hello World\");",
                "var e = \"Reload\";",
            ]);

            var found = UiLiterals.Scan(["Sample.cs"], folder).Select(literal => literal.Text).ToList();

            Assert.Equal(["Open the file", "Hello World", "Reload"], found);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
