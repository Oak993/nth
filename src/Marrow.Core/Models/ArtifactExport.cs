// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1 export, 6.3 Save to project and Export; ED-NEW-web-w12-1). The names
// and paths an artifact leaves nth under: Save to project's default path ("artifacts/<title>.<ext>", canvas section 4:
// "Saved to my-app/artifacts/pricing-calculator.html"), each kind's export formats in the menu's order, and each export's
// file name. Writing the files (standalone HTML with libraries inlined, the zip, PDF, PNG, docx) is the runtime's and the
// pane's (W13, W14). No Swift counterpart.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Models;

public enum ArtifactExportFormat
{
    /// <summary>A standalone page with everything inlined.</summary>
    Html,
    Markdown,
    Pdf,
    Docx,
    Png,
    Svg,
    /// <summary>A project's folder as a .zip.</summary>
    Zip,
    /// <summary>The version's own file as it is (a code file, a .jsx/.tsx component, a .mmd diagram, a .nthui view).</summary>
    Source,
}

public static class ArtifactExport
{
    private const int MaxSlugLength = 60;

    /// <summary>The export menu of a kind, in order (6.3: html, md, pdf, docx, png and svg; 6.1: the zip beside the standalone HTML).</summary>
    public static IReadOnlyList<ArtifactExportFormat> Formats(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Html => [ArtifactExportFormat.Html, ArtifactExportFormat.Pdf, ArtifactExportFormat.Png],
        ArtifactKind.React => [ArtifactExportFormat.Html, ArtifactExportFormat.Source, ArtifactExportFormat.Pdf, ArtifactExportFormat.Png],
        ArtifactKind.Svg => [ArtifactExportFormat.Svg, ArtifactExportFormat.Png],
        ArtifactKind.Mermaid => [ArtifactExportFormat.Svg, ArtifactExportFormat.Png, ArtifactExportFormat.Source],
        ArtifactKind.Markdown => [ArtifactExportFormat.Markdown, ArtifactExportFormat.Docx, ArtifactExportFormat.Pdf, ArtifactExportFormat.Html],
        ArtifactKind.Code => [ArtifactExportFormat.Source],
        ArtifactKind.NthUi => [ArtifactExportFormat.Png, ArtifactExportFormat.Source],
        ArtifactKind.Project => [ArtifactExportFormat.Zip, ArtifactExportFormat.Html, ArtifactExportFormat.Pdf, ArtifactExportFormat.Png],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The title as a file name: lower case, letters and digits kept (any script), every run of anything else one hyphen,
    /// at most 60 characters; "artifact" when nothing is left, and a Windows device name gets "-artifact" after it.
    /// </summary>
    public static string Slug(string title)
    {
        var builder = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxSlugLength) slug = slug[..MaxSlugLength].TrimEnd('-');
        if (slug.Length == 0) return "artifact";
        var upper = slug.ToUpperInvariant();
        var reserved = upper is "CON" or "PRN" or "AUX" or "NUL"
            || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] is >= '0' and <= '9');
        return reserved ? slug + "-artifact" : slug;
    }

    /// <summary>
    /// Save to project's default, relative to the project with forward slashes: "artifacts/&lt;slug&gt;.&lt;ext&gt;" for one
    /// file, "artifacts/&lt;slug&gt;" (a folder) for a project.
    /// </summary>
    public static string SaveToProjectPath(Artifact artifact) =>
        artifact.Kind == ArtifactKind.Project ? "artifacts/" + Slug(artifact.Title) : "artifacts/" + Slug(artifact.Title) + "." + ArtifactKinds.Extension(artifact.Kind, artifact.Language);

    /// <summary>An export's file name: the slug, "-v&lt;n&gt;" when the version is not the latest, and the format's extension.</summary>
    public static string FileName(Artifact artifact, ArtifactExportFormat format, int? version = null)
    {
        var suffix = version is { } number && number != artifact.Current ? "-v" + number.ToString(CultureInfo.InvariantCulture) : "";
        return Slug(artifact.Title) + suffix + "." + Extension(artifact, format);
    }

    public static string Extension(Artifact artifact, ArtifactExportFormat format) => format switch
    {
        ArtifactExportFormat.Html => "html",
        ArtifactExportFormat.Markdown => "md",
        ArtifactExportFormat.Pdf => "pdf",
        ArtifactExportFormat.Docx => "docx",
        ArtifactExportFormat.Png => "png",
        ArtifactExportFormat.Svg => "svg",
        ArtifactExportFormat.Zip => "zip",
        ArtifactExportFormat.Source => artifact.Kind == ArtifactKind.Project ? "zip" : ArtifactKinds.Extension(artifact.Kind, artifact.Language),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
