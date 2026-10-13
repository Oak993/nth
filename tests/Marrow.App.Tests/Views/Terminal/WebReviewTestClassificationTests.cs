using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Marrow.App.Tests.Views.Terminal;

[Trait("Decision", "ED-NEW-web-review-19")]
public sealed class WebReviewTestClassificationTests
{
    [Fact]
    public void Real_shell_checks_are_excluded_from_managed_runs()
    {
        var folder = Path.GetDirectoryName(Source())!;
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            var source = File.ReadAllText(file);
            if (Regex.IsMatch(source, @"(?m)^// These are deliberately real Windows tests"))
                Assert.Contains("Native", Regex.Match(source, @"public sealed class (\w+)").Groups[1].Value, StringComparison.Ordinal);
            foreach (Match match in Regex.Matches(source, @"public void (\w+)\([^\r\n]*=>[^\r\n]*(?:RunWithRealShell|WithRealStore)\("))
                Assert.Contains("Native", match.Groups[1].Value, StringComparison.Ordinal);
        }
    }

    private static string Source([CallerFilePath] string path = "") => path;
}
