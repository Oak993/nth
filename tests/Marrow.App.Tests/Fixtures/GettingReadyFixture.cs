using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// GR-2: the measured fixture of the locked look I "Sheet" (OD-0096, the OD-0098 pick "Look I, Words W1, Download D3"):
/// tests/fixtures/getting-ready/sheet.json, recorded by measure.cjs in headless Chrome from
/// E:/Droppy/work/design/decisions-2026-10-03/getting-ready/getting-ready.html (window 960 x 680, sidebar 250; every number is
/// in CSS pixels of that window, read at rest with CSS transitions off). The sheet's x is in window pixels; the detail pane's
/// own x is that minus the sidebar.
/// </summary>
public static class GettingReadyFixture
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(StrikeFixtures.FixturePath("getting-ready", "sheet.json"))));

    public static JsonElement Root => Document.Value.RootElement;

    public static double Sidebar => Root.GetProperty("window").GetProperty("sidebar").GetDouble();

    public static JsonElement Theme(bool dark) => Root.GetProperty(dark ? "dark" : "light");

    public static JsonElement Css(bool dark = true) => Theme(dark).GetProperty("css");

    public static JsonElement Frame(string name, bool dark = true) => Theme(dark).GetProperty("frames").GetProperty(name);

    /// <summary>The 21 hand-over instants, the page's frame function at handStart + 1.1 s * i / 20.</summary>
    public static IReadOnlyList<JsonElement> HandFrames(bool dark = true) => [.. Frame("hand", dark).EnumerateArray()];

    public static double Number(JsonElement element, string name) => element.GetProperty(name).GetDouble();

    public static double Px(JsonElement element, string name) =>
        double.Parse(element.GetProperty(name).GetString()!.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    /// <summary>The y translation of "matrix(1, 0, 0, 1, 0, y)".</summary>
    public static double TranslateY(string matrix) =>
        double.Parse(matrix.Trim().TrimEnd(')').Split(',')[^1], CultureInfo.InvariantCulture);

    /// <summary>The blur of a computed filter "blur(8.48571px) saturate(0.62)" in pixels.</summary>
    public static double BlurPx(string filter) =>
        double.Parse(Regex.Match(filter, @"blur\(([0-9.]+)px\)").Groups[1].Value, CultureInfo.InvariantCulture);

    public static double Saturate(string filter) =>
        double.Parse(Regex.Match(filter, @"saturate\(([0-9.]+)\)").Groups[1].Value, CultureInfo.InvariantCulture);

    public static string[] Words => [.. Root.GetProperty("words").GetProperty("W1").GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).Take(6)];

    public static string Title => Root.GetProperty("words").GetProperty("W1").GetProperty("title").GetString()!;

    public static string Line => Root.GetProperty("words").GetProperty("W1").GetProperty("sub").GetString()!;

    public static string Done => Root.GetProperty("words").GetProperty("W1").GetProperty("done").GetString()!;
}
