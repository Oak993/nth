// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-5). Other apps let a config name a value instead of holding it: VS Code's ${input:token} and ${env:GITHUB_TOKEN}, Claude Code's and Cursor's
// ${GITHUB_TOKEN} and ${GITHUB_TOKEN:-fallback}, ${workspaceFolder}, ${userHome}. nth never saves such text as if it were the value. A placeholder is filled in from the environment (or from the
// project / home folder the config belongs to) when it can be; what cannot be filled in is reported by name, so the import can leave the value empty (a variable) or not import the server (an address or an argument).
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.MCP.Import;

public static partial class McpPlaceholders
{
    /// <summary>The text with every placeholder that could be filled in filled in, and the placeholders (as written, "${input:token}") that could not.</summary>
    public sealed record Expansion(string Text, IReadOnlyList<string> Unresolved);

    [GeneratedRegex(@"\$\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Braces();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.()\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    public static bool Has(string? text) => !string.IsNullOrEmpty(text) && Braces().IsMatch(text);

    /// <summary>
    /// Fills in the placeholders. <paramref name="lookup"/> gives a name's value, or null when it has none: it is asked for plain names only (<c>${NAME}</c>, <c>${env:NAME}</c>, <c>${NAME:-fallback}</c>,
    /// <c>${userHome}</c>); <c>${input:...}</c> is a prompt of the other app and a prefixed form such as <c>${config:x}</c> or <c>${command:x}</c> is its own, so neither can be filled in here.
    /// A name with no value, or an empty one under <c>:-</c>, takes the fallback when there is one.
    /// </summary>
    public static Expansion Expand(string text, Func<string, string?> lookup)
    {
        if (!Has(text)) return new Expansion(text, []);
        var unresolved = new List<string>();
        var built = new StringBuilder();
        var last = 0;
        foreach (Match match in Braces().Matches(text))
        {
            built.Append(text, last, match.Index - last);
            last = match.Index + match.Length;
            var body = match.Groups[1].Value.Trim();
            string? fallback = null;
            var cut = body.IndexOf(":-", StringComparison.Ordinal);
            if (cut >= 0) { fallback = body[(cut + 2)..]; body = body[..cut].Trim(); }
            if (body.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) body = body[4..].Trim();
            string? value = null;
            if (body == "/" || Name().IsMatch(body)) value = lookup(body);
            if (value is null || (fallback is not null && value.Length == 0)) value = fallback;
            if (value is null)
            {
                if (!unresolved.Contains(match.Value, StringComparer.Ordinal)) unresolved.Add(match.Value);
                built.Append(match.Value);
            }
            else built.Append(value);
        }
        built.Append(text, last, text.Length - last);
        return new Expansion(built.ToString(), unresolved);
    }

    /// <summary>A name's value in this PC's environment: the running process first, then the user's and the machine's settings (a variable set after nth started is still found).</summary>
    public static string? EnvironmentValue(string name)
    {
        if (!Name().IsMatch(name)) return null;
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            string? value;
            try { value = Environment.GetEnvironmentVariable(name, target); }
            catch (Exception e) when (e is System.Security.SecurityException or ArgumentException or PlatformNotSupportedException) { value = null; }
            if (value is not null) return value;
        }
        return null;
    }
}
