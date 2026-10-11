// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1; ED-NEW-web-W12-1; owner 2026-10-11 "Visus and Tabula approved").
// The eight artifact kinds, their wire names (what the tools and the fenced fallback say) and the file extension each
// version is stored under. No Swift counterpart.
namespace Marrow.Core.Models;

public enum ArtifactKind
{
    Html,
    React,
    Svg,
    Mermaid,
    Markdown,
    Code,
    NthUi,
    Project,
}

public static class ArtifactKinds
{
    /// <summary>The wire names, in the order the tools list them.</summary>
    public static IReadOnlyList<string> WireNames { get; } = ["html", "react", "svg", "mermaid", "markdown", "code", "nth-ui", "project"];

    public static string WireName(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.Html => "html",
        ArtifactKind.React => "react",
        ArtifactKind.Svg => "svg",
        ArtifactKind.Mermaid => "mermaid",
        ArtifactKind.Markdown => "markdown",
        ArtifactKind.Code => "code",
        ArtifactKind.NthUi => "nth-ui",
        ArtifactKind.Project => "project",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The kind chip's words in Tabula's header and on an artifact's chip in the answer ("HTML app", canvas section 4); a
    /// code artifact says its language when it has one.
    /// </summary>
    public static string DisplayName(ArtifactKind kind, string? language = null) => kind switch
    {
        ArtifactKind.Html => "HTML app",
        ArtifactKind.React => "React component",
        ArtifactKind.Svg => "SVG image",
        ArtifactKind.Mermaid => "Diagram",
        ArtifactKind.Markdown => "Document",
        ArtifactKind.Code => language is { Length: > 0 } name && CodeExtension(name) != "txt" ? LanguageName(name) + " code" : "Code",
        ArtifactKind.NthUi => "Live view",
        ArtifactKind.Project => "Project",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string LanguageName(string language)
    {
        var name = language.Trim();
        return name.ToLowerInvariant() switch
        {
            "js" or "javascript" or "node" => "JavaScript",
            "ts" or "typescript" => "TypeScript",
            "py" or "python" => "Python",
            "cs" or "c#" or "csharp" => "C#",
            "cpp" or "c++" or "cxx" => "C++",
            "sh" or "bash" or "shell" or "zsh" => "Shell",
            "ps1" or "powershell" or "pwsh" => "PowerShell",
            "sql" or "css" or "json" or "yaml" or "yml" or "toml" or "xml" => name.ToUpperInvariant(),
            _ => char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    /// <summary>
    /// A kind from its wire name, case-insensitive, with the few spellings small models use for the same thing
    /// (md, jsx, tsx, nthui); null for anything else.
    /// </summary>
    public static ArtifactKind? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "html" or "htm" => ArtifactKind.Html,
        "react" or "jsx" or "tsx" => ArtifactKind.React,
        "svg" => ArtifactKind.Svg,
        "mermaid" or "mmd" => ArtifactKind.Mermaid,
        "markdown" or "md" or "document" => ArtifactKind.Markdown,
        "code" => ArtifactKind.Code,
        "nth-ui" or "nthui" or "nth_ui" => ArtifactKind.NthUi,
        "project" => ArtifactKind.Project,
        _ => null,
    };

    /// <summary>The extension (no dot) of a single-file kind's version files; "" for a project, which is a folder.</summary>
    public static string Extension(ArtifactKind kind, string? language) => kind switch
    {
        ArtifactKind.Html => "html",
        ArtifactKind.React => IsTypeScript(language) ? "tsx" : "jsx",
        ArtifactKind.Svg => "svg",
        ArtifactKind.Mermaid => "mmd",
        ArtifactKind.Markdown => "md",
        ArtifactKind.Code => CodeExtension(language),
        ArtifactKind.NthUi => "nthui",
        ArtifactKind.Project => "",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsTypeScript(string? language) => language?.Trim().ToLowerInvariant() is "tsx" or "ts" or "typescript";

    /// <summary>A code artifact's extension from its language name; "txt" when the language is missing or unusable.</summary>
    public static string CodeExtension(string? language)
    {
        var name = language?.Trim().ToLowerInvariant() ?? "";
        var known = name switch
        {
            "python" or "py" => "py",
            "javascript" or "js" or "node" => "js",
            "typescript" or "ts" => "ts",
            "csharp" or "c#" or "cs" => "cs",
            "c++" or "cpp" or "cxx" => "cpp",
            "rust" or "rs" => "rs",
            "ruby" or "rb" => "rb",
            "kotlin" or "kt" => "kt",
            "bash" or "sh" or "shell" or "zsh" => "sh",
            "powershell" or "pwsh" or "ps1" => "ps1",
            "yaml" or "yml" => "yml",
            "haskell" or "hs" => "hs",
            "golang" or "go" => "go",
            "plaintext" or "plain" or "text" or "txt" or "" => "txt",
            _ => null,
        };
        if (known is not null) return known;
        // Any other short plain token is its own extension (java, swift, sql, lua, css, json, toml ...).
        return name.Length <= 8 && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9') ? name : "txt";
    }
}
