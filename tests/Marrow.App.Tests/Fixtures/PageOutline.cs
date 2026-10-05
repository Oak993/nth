using Marrow.App.Port.Settings;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The outline of a Customization page section as text, one line per section, row and nested card: the title, whether it is shown or folded shut, and whether its divider is shown. The locked design's
/// own outline (tests/fixtures/customization/page.json, `sections`) is written in the same form by <see cref="OfDesign"/>, so the page is held to the design line by line (CZ-T16, CZ-T39).</summary>
internal static class PageOutline
{
    internal static List<string> Of(IEnumerable<CzSection> sections)
    {
        var lines = new List<string>();
        foreach (var section in sections)
        {
            lines.Add($"S {section.Title ?? ""} {(section.Open ? "shown" : "folded")}");
            Rows(section.Rows, 1, lines);
        }
        return lines;
    }

    private static void Rows(CzRowStack stack, int depth, List<string> lines)
    {
        foreach (var slot in stack.Slots)
        {
            var shown = slot.Open ? "shown" : "folded";
            var divider = slot.Divider.Shown ? "dv" : "nodv";
            if (slot.Row is CzRowStack nest)
            {
                lines.Add($"N {depth} {shown} {divider}");
                Rows(nest, depth + 1, lines);
            }
            else lines.Add($"R {depth} {TitleOf(slot.Row)} {shown} {divider}");
        }
    }

    internal static string TitleOf(Avalonia.Controls.Control row) => row switch
    {
        CzRow r => r.Title.Text ?? "",
        CzCaptionRow c => c.Text.Text ?? "",
        CzNoteRow n => n.Text.Text ?? "",
        _ => throw new ArgumentException("Not a row: " + row.GetType().Name),
    };

    /// <summary>The design's `sections` (one style's) in the same form; the sections the page does not carry (Voice orb, Working dots) were left out by the fixture.</summary>
    internal static List<string> OfDesign(System.Text.Json.JsonElement sections)
    {
        var lines = new List<string>();
        foreach (var section in sections.EnumerateArray())
        {
            lines.Add($"S {section.GetProperty("title").GetString()} {(section.GetProperty("shown").GetBoolean() ? "shown" : "folded")}");
            DesignRows(section.GetProperty("rows"), 1, lines);
        }
        return lines;
    }

    private static void DesignRows(System.Text.Json.JsonElement rows, int depth, List<string> lines)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("nest", out _))
            {
                lines.Add($"N {depth} {(row.GetProperty("shown").GetBoolean() ? "shown" : "folded")} dv");
                DesignRows(row.GetProperty("rows"), depth + 1, lines);
            }
            else lines.Add($"R {depth} {row.GetProperty("t").GetString()} {(row.GetProperty("fold").GetBoolean() ? "folded" : "shown")} {(row.GetProperty("dv").GetBoolean() ? "dv" : "nodv")}");
        }
    }
}
