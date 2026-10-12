// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.4 "The reviewer before Done"; unit AS-5; ED-NEW-dextra-as5-8). No Swift twin.
// The reviewer's verdict as nth reads it from the reviewer chat's final answer, and the rule for what a Fail does: it
// goes back to the lead as a redirect at most twice, and a third Fail makes the mission Failed. Pure.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>Pass, or Fail with reasons.</summary>
public sealed partial record ReviewVerdict(bool Passed, string Reasons)
{
    /// <summary>How many Fails go back to the lead as a redirect (3.4: "at most twice").</summary>
    public const int Redirects = 2;

    /// <summary>The line the reviewer's answer must end with.</summary>
    public const string Instruction =
        "End your answer with one line that says exactly VERDICT: PASS, or VERDICT: FAIL followed by the reasons.";

    /// <summary>
    /// The verdict in a reviewer's answer: its last line that starts with "VERDICT:" (any case, after optional Markdown
    /// emphasis). PASS passes only when nothing but punctuation follows it; FAIL takes what follows, and the lines after
    /// it, as the reasons. An answer with no verdict line, or one that says anything else, fails (fail closed).
    /// </summary>
    public static ReviewVerdict Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return new(false, "The reviewer gave no answer.");
        var lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var match = VerdictLine().Match(lines[i]);
            if (!match.Success) continue;
            var word = match.Groups["word"].Value.ToUpperInvariant();
            var rest = match.Groups["rest"].Value.Trim().Trim('*', '_').Trim();
            var after = string.Join("\n", lines[(i + 1)..]).Trim();
            if (word == "PASS")
                return rest.Trim('.', '!', ' ').Length == 0 && after.Length == 0
                    ? new(true, "")
                    : new(false, "The reviewer's verdict was not a plain PASS: " + Clip(lines[i].Trim()));
            var reasons = string.Join("\n", new[] { rest.TrimStart(':', '-', ' ', '—'), after }.Where(p => p.Length > 0));
            return new(false, reasons.Length > 0 ? Clip(reasons) : "The reviewer failed it without reasons.");
        }
        return new(false, "The reviewer gave no verdict line.");
    }

    /// <summary>What a Fail does after <paramref name="failsSoFar"/> earlier Fails: redirect the lead, or fail the mission.</summary>
    public static bool FailsMission(int failsSoFar) => failsSoFar >= Redirects;

    private static string Clip(string text) => text.Length <= 4_000 ? text : text[..4_000] + "…";

    [GeneratedRegex(@"^\s*[*_>#\s]*VERDICT\s*:\s*[*_]*\s*(?<word>PASS|FAIL)\b(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerdictLine();
}
