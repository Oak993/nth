// P9, text metrics that feed layout (task packet T-M0-008).
//
// QUESTION: on Avalonia 12.1.2 / .NET 10, using Avalonia.Media.TextFormatting (TextLayout,
// TextFormatter, TextShaper, ShapedTextRun, TextLineBreak, TextBounds and the line metrics a
// TextLine carries), with the source's first-party type scale implemented, do
//   (A) the measured wrap points and line counts of a fixed fixture corpus match the visible wrap
//       points in the tour-*@2x.png oracle at the recorded sizes, and
//   (B) the 50 monospaced sites hold column alignment under Cascadia Mono with tabular figures?
//
// MEASUREMENT ONLY. Glyph rasterisation stays UNVERIFIED under OD-0001, and every number taken from
// the PNGs carries the OD-0004 approximate-oracle caveat.
//
// CONTROLS, each of which must FAIL, or a PASS cannot be trusted:
//   CA1  the same corpus laid out one step up the source type scale (body 13 -> title3 15, etc.)
//   CA2  the same corpus wrapped by a naive fixed-advance model with no shaping at all
//   CB1  the .monospaced rows laid out in the proportional system-font substitute
//   CB2  the .monospacedDigit rows laid out with proportional figures (pnum) forced on
//
// The Windows substitute for the macOS system font is NOT decided (UNK-0007, PA-0013). This probe
// measures the PA-0013 candidate, Segoe UI Variable, as the primary face and reports every other
// installed candidate alongside, so the verdict is about the measured faces, never an assumed one.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace P9;

// ---- SOURCE FACTS: the first-party type scale, DroppyCode/UI/Common/ChatZoom.swift (dd2b86ef) ----
static class TypeScale
{
    public enum Style { LargeTitle, Title, Title2, Title3, Headline, Body, Callout, Subheadline, Footnote, Caption, Caption2 }

    // ChatZoom.swift:10
    public static readonly int[] Percentages = { 80, 90, 95, 100, 110, 125, 135 };
    // ChatZoom.swift:13
    public const int DefaultIndex = 3;
    public static int StepCount => Percentages.Length;
    // ChatZoom.swift:16-18
    public static int Clamped(int index) => Math.Min(StepCount - 1, Math.Max(0, index));
    public static int Percentage(int index) => Percentages[Clamped(index)];
    // ChatZoom.swift:25-27
    public static double Scale(int index) => Percentage(index) / 100.0;

    // ChatZoom.swift:45-57
    public static double ChatBaseSize(Style style) => style switch
    {
        Style.LargeTitle => 26,
        Style.Title => 22,
        Style.Title2 => 17,
        Style.Title3 => 15,
        Style.Headline or Style.Body => 13,
        Style.Callout => 12,
        Style.Subheadline => 11,
        Style.Footnote or Style.Caption or Style.Caption2 => 10,
        _ => 13,
    };

    // ChatZoom.swift:62-70. Swift's CGFloat.rounded() is schoolbook rounding (to nearest, ties away).
    public static double ChatSize(Style style, double zoom)
        => zoom == 1 ? ChatBaseSize(style) : Math.Round(ChatBaseSize(style) * zoom, MidpointRounding.AwayFromZero);

    public static FontWeight ChatWeight(Style style, FontWeight? weight)
        => weight ?? (style == Style.Headline ? FontWeight.SemiBold : FontWeight.Normal);

    // MarkdownView.swift:1129 and ProseRunView.swift:18: (pointSize * zoom * 2).rounded() / 2
    public static double MarkdownPointSize(double pointSize, double zoom)
        => Math.Round(pointSize * zoom * 2, MidpointRounding.AwayFromZero) / 2;

    // LinkParagraphView.swift:117: NSFont.monospacedSystemFont(ofSize: max(9, pointSize - 1))
    public static double InlineCodeSize(double pointSize) => Math.Max(9, pointSize - 1);

    // The step above a size on the scale, for control CA1.
    public static double NextStepUp(double size) => size switch
    {
        10 => 11, 11 => 12, 12 => 13, 13 => 15, 15 => 17, 17 => 22, 22 => 26, _ => size + 2,
    };
}

static class Out
{
    static readonly StringBuilder Sb = new();
    public static void W(string s = "") { Console.WriteLine(s); Sb.AppendLine(s); }
    public static void Save(string path) => File.WriteAllText(path, Sb.ToString(), new UTF8Encoding(false));
}

// Loads a font file the process may read but that is not installed, without installing it.
sealed class FileFontCollection : FontCollectionBase
{
    public FileFontCollection(Uri key) { Key = key; }
    public override Uri Key { get; }
    public GlyphTypeface? AddFile(string path)
    {
        var ms = new MemoryStream(File.ReadAllBytes(path));
        return TryAddGlyphTypeface(ms, out var g) ? g : null;
    }
}

record Span(int Start, int Length, string Kind);

record Fixture(
    string Id, string Oracle, string Text, double Size, double Width, string WidthSource, bool Approx,
    double WidthUncertainty, string[] OracleLines, Span[] Spans, string Where);

sealed class Faces
{
    public required string Name;
    public required FontFamily Sans;
    public required FontFamily Mono;
}

static class Program
{
    const string CascadiaFileEnv = "P9_CASCADIA_FILE";

    static int Main(string[] args)
    {
        var dataDir = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(dataDir)) { Console.Error.WriteLine("MARROW_PORT_DATA_DIR is not set; refusing to run."); return 2; }
        var probeDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var oraclePath = Path.Combine(probeDir, "oracle_measurements.json");

        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        Out.W("P9 text metrics probe");
        Out.W($"utc start          : {DateTime.UtcNow:O}");
        Out.W($"runtime            : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Out.W($"os                 : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Out.W($"Avalonia.Base      : {typeof(TextLayout).Assembly.GetName().Version}");
        Out.W($"MARROW_PORT_DATA_DIR set: {dataDir}");
        Out.W($"TextFormatter impl : {TextFormatter.Current.GetType().FullName}");
        Out.W($"TextShaper impl    : {TextShaper.Current.GetType().FullName}");
        Out.W($"FontManager impl   : {FontManager.Current.GetType().FullName}, default family '{FontManager.Current.DefaultFontFamily.Name}'");

        PrintTypeScale();
        var cascadia = ResolveFonts();

        var fixtures = BuildFixtures(oraclePath);
        // "Segoe UI Variable" by that name resolves to classic Segoe UI on this machine (see the
        // resolution table); the variable face is reached through its instance name.
        const string primaryName = "Segoe UI Variable Text";
        var sansCandidates = new[] { "Segoe UI Variable Small", "Segoe UI Variable Display", "Segoe UI", "Inter" };
        var monoFamily = cascadia ?? new FontFamily("Cascadia Mono");

        var primary = new Faces { Name = primaryName, Sans = new FontFamily(primaryName), Mono = monoFamily };
        var (primaryOk, primaryMatched) = RunWrapSuite(fixtures, primary, $"PRIMARY face '{primaryName}' (PA-0013 candidate)", oraclePath, detail: true);

        var sensitivity = new List<(string, int, int)>();
        foreach (var name in sansCandidates)
        {
            var faces = new Faces { Name = name, Sans = new FontFamily(name), Mono = monoFamily };
            var (_, matched) = RunWrapSuite(fixtures, faces, $"SENSITIVITY face '{name}'", oraclePath, detail: name == "Inter");
            sensitivity.Add((name, matched, fixtures.Count));
        }

        // Controls CA1 and CA2, on the primary face.
        var ca1 = RunControlScaleStep(fixtures, primary);
        var ca2 = RunControlNaive(fixtures);

        TextFormatterCrossCheck(fixtures, primary);

        var (monoPrimaryOk, cb1Failed, cb2Failed, allCascadiaOk) = RunMonospaceSuite(cascadia, primary, verbose: true);
        var segoe = new Faces { Name = "Segoe UI", Sans = new FontFamily("Segoe UI"), Mono = monoFamily };
        var (monoSegoeOk, _, cb2SegoeFailed, _) = RunMonospaceSuite(cascadia, segoe, verbose: false);

        Out.W();
        Out.W("==== SUMMARY ====");
        Out.W($"(A) wrap points, primary face {primaryName}: {primaryMatched}/{fixtures.Count} -> {(primaryOk ? "ALL MATCH" : "MISMATCH")}");
        foreach (var (n, m, t) in sensitivity) Out.W($"    sensitivity face {n,-26}: {m}/{t} fixtures match");
        Out.W($"CA1 control (one type-scale step up) fails        : {ca1}");
        Out.W($"CA2 control (naive fixed advance) fails           : {ca2}");
        Out.W($"(B) 50 sites hold alignment, digits in {primaryName}: {(monoPrimaryOk ? "YES" : "NO")}");
        Out.W($"(B) 50 sites hold alignment, digits in Segoe UI   : {(monoSegoeOk ? "YES" : "NO")}");
        Out.W($"    all 50 under Cascadia Mono + tnum (alt read)  : {(allCascadiaOk ? "YES" : "NO")}");
        Out.W($"CB1 control (proportional face) fails everywhere  : {cb1Failed}");
        Out.W($"CB2 control (pnum forced) fails everywhere        : {cb2Failed} (Segoe UI run: {cb2SegoeFailed})");
        Out.W($"utc end            : {DateTime.UtcNow:O}");
        Out.Save(Path.Combine(probeDir, "p9_output.txt"));
        return 0;
    }

    // ---------------------------------------------------------------------------------------------
    static void PrintTypeScale()
    {
        Out.W();
        Out.W("==== TYPE SCALE (ChatZoom.swift:10-70, implemented above) ====");
        var head = new StringBuilder("style        ");
        for (int i = 0; i < TypeScale.StepCount; i++) head.Append($"{TypeScale.Percentage(i),6}%");
        Out.W(head.ToString());
        foreach (TypeScale.Style s in Enum.GetValues<TypeScale.Style>())
        {
            var row = new StringBuilder($"{s,-12} ");
            for (int i = 0; i < TypeScale.StepCount; i++) row.Append($"{TypeScale.ChatSize(s, TypeScale.Scale(i)),7:0.##}");
            row.Append($"   weight@default={TypeScale.ChatWeight(s, null)}");
            Out.W(row.ToString());
        }
        Out.W($"default index {TypeScale.DefaultIndex} -> {TypeScale.Percentage(TypeScale.DefaultIndex)}%; clamped(-4)={TypeScale.Clamped(-4)} clamped(99)={TypeScale.Clamped(99)}");
        Out.W($"markdown 13pt at zoom 1.25 -> {TypeScale.MarkdownPointSize(13, 1.25)}; inline code at 13 -> {TypeScale.InlineCodeSize(13)}; at 9.5 -> {TypeScale.InlineCodeSize(9.5)}");
    }

    // ---------------------------------------------------------------------------------------------
    static FontFamily? ResolveFonts()
    {
        Out.W();
        Out.W("==== FONT RESOLUTION (what Avalonia actually hands back) ====");
        foreach (var name in new[] { "SF Pro", "SF Pro Text", ".AppleSystemUIFont", "SF Mono", "Segoe UI Variable", "Segoe UI Variable Text",
                                     "Segoe UI Variable Small", "Segoe UI Variable Display", "Segoe UI", "Inter", "Cascadia Mono", "Cascadia Code", "Consolas" })
        {
            var ok = FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var g);
            // What a layout really uses: shape one string and read the ShapedTextRun's typeface.
            using var tl = new TextLayout("Ag0", new Typeface(name), 13, Brushes.Black);
            var used = UsedFaces(tl);
            Out.W($"request '{name}',{new string(' ', Math.Max(0, 26 - name.Length))} TryGetGlyphTypeface={ok,-5} -> {(g is null ? "(null)" : Describe(g))} | TextLayout used: {used}");
        }

        Out.W();
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI", "Inter" })
            foreach (var w in new[] { FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold })
            {
                var ok = FontManager.Current.TryGetGlyphTypeface(new Typeface(name, FontStyle.Normal, w), out var gw);
                Out.W($"weight request '{name}' {w,-8} ({(int)w}): {ok} -> {(gw is null ? "(null)" : Describe(gw))}");
            }

        var path = Environment.GetEnvironmentVariable(CascadiaFileEnv);
        Out.W();
        Out.W($"Cascadia Mono is not in C:\\Windows\\Fonts nor the per-user font folder (checked before the run).");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Out.W($"{CascadiaFileEnv} not set or missing: no private Cascadia Mono file to load.");
            return null;
        }
        var bytes = File.ReadAllBytes(path);
        Out.W($"private font file  : {path}");
        Out.W($"  bytes {bytes.Length}, sha256 {Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");
        var key = new Uri("fonts:P9Cascadia", UriKind.Absolute);
        var coll = new FileFontCollection(key);
        var g2 = coll.AddFile(path);
        if (g2 is null) { Out.W("  TryAddGlyphTypeface: FAILED"); return null; }
        FontManager.Current.AddFontCollection(coll);
        Out.W($"  loaded (not installed): {Describe(g2)}");
        var family = new FontFamily($"fonts:P9Cascadia#{g2.FamilyName}");
        foreach (var w in new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold })
        {
            var ok = FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, w), out var gw);
            Out.W($"  '{family}' weight {w,-8}: {ok} -> {(gw is null ? "(null)" : Describe(gw))}");
        }
        using (var tl = new TextLayout("Ag0", new Typeface(family), 13, Brushes.Black))
            Out.W($"  TextLayout used  : {UsedFaces(tl)}");
        return family;
    }

    static string Describe(GlyphTypeface g)
        => $"family='{g.FamilyName}' typo='{g.TypographicFamilyName}' weight={(int)g.Weight} sim={g.FontSimulations} fixedPitch={g.Metrics.IsFixedPitch} glyphs={g.GlyphCount}";

    static string UsedFaces(TextLayout tl)
    {
        var set = new SortedSet<string>();
        foreach (var line in tl.TextLines)
            foreach (var run in line.TextRuns)
                if (run is ShapedTextRun sr)
                    set.Add($"{sr.ShapedBuffer.GlyphTypeface.FamilyName}[w{(int)sr.ShapedBuffer.GlyphTypeface.Weight},{sr.ShapedBuffer.GlyphTypeface.FontSimulations}]@{sr.ShapedBuffer.FontRenderingEmSize:0.##}");
        return string.Join(" + ", set);
    }

    // ---------------------------------------------------------------------------------------------
    static List<Fixture> BuildFixtures(string oraclePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(oraclePath));
        var col = doc.RootElement.GetProperty("column_pt");
        double hydraCol = col.GetProperty("tour-hydra").GetDouble();
        double panelsCol = col.GetProperty("tour-panels").GetDouble();
        // +-1.5 px of divider antialiasing, over each image's source-derived scale.
        var scales = doc.RootElement.GetProperty("scales_px_per_pt");
        double uHydra = 1.5 / scales.GetProperty("tour-hydra").GetDouble();
        double uPanels = 1.5 / scales.GetProperty("tour-panels").GetDouble();

        // Strings: DroppyCode/App/Captures/WebsiteCaptures.swift and TourCaptures+Hydra.swift, as rendered
        // (markdown markers removed; `code` spans and **bold** spans become runs).
        const string pa = "In DraftAttachments, inside ComposerView.swift. The strip pads its leading edge with attachmentInset (14pt) but leaves the top on the stack's default spacing, which is why the photo hugs the top of the pill.";
        Span[] paSpans =
        {
            new(pa.IndexOf("DraftAttachments", StringComparison.Ordinal), "DraftAttachments".Length, "code"),
            new(pa.IndexOf("ComposerView.swift", StringComparison.Ordinal), "ComposerView.swift".Length, "bold"),
            new(pa.IndexOf("attachmentInset", StringComparison.Ordinal), "attachmentInset".Length, "code"),
        };
        const string three = "Three heads are out: one builds the tour window, one writes the pages and wires the Help menu, one photographs every page. I keep the design and the integration.";
        const double col820 = 820;                   // ThreadTimeline.swift:395
        const double bubbleInset = 96 + 14 + 14 + 4; // TimelineRows.swift:124-127,164 and UserBubble.tail (:1175)

        return new List<Fixture>
        {
            new("F1", "tour-welcome", pa, 13, col820, "ThreadTimeline.swift:395 maxWidth 820", false, 0,
                new[] { "In DraftAttachments, inside ComposerView.swift. The strip pads its leading edge with attachmentInset (14pt) but leaves the",
                        "top on the stack's default spacing, which is why the photo hugs the top of the pill." }, paSpans, "WebsiteCaptures.swift:217"),
            new("F2", "tour-welcome", "Want me to give the top the same 14pt?", 13, col820, "ThreadTimeline.swift:395", false, 0,
                new[] { "Want me to give the top the same 14pt?" }, Array.Empty<Span>(), "WebsiteCaptures.swift:217"),
            new("F3", "tour-welcome", "Done. The attachment strip now uses the same 14pt inset on both axes, so the draft photo sits as far from the top as from the left. The build is running to confirm nothing else moved.", 13, col820, "ThreadTimeline.swift:395", false, 0,
                new[] { "Done. The attachment strip now uses the same 14pt inset on both axes, so the draft photo sits as far from the top as from the left. The",
                        "build is running to confirm nothing else moved." }, Array.Empty<Span>(), "WebsiteCaptures.swift:396"),
            new("F4", "tour-welcome", "Where does the composer decide how far the draft photo sits from the edges?", 13, col820 - bubbleInset, "820 - 96 - 14 - (14+4)", false, 0,
                new[] { "Where does the composer decide how far the draft photo sits from the edges?" }, Array.Empty<Span>(), "WebsiteCaptures.swift:206"),
            new("F5", "tour-welcome", "The draft photo in the composer should sit as far from the top as it does from the left.", 13, col820 - bubbleInset, "820 - 96 - 14 - (14+4)", false, 0,
                new[] { "The draft photo in the composer should sit as far from the top as it does from the left." }, Array.Empty<Span>(), "WebsiteCaptures.swift:366"),
            new("F7", "tour-hydra", three, 13, hydraCol, "oracle divider / source scale", true, uHydra,
                new[] { "Three heads are out: one builds the tour window, one writes the pages and wires",
                        "the Help menu, one photographs every page. I keep the design and the integration." }, Array.Empty<Span>(), "TourCaptures+Hydra.swift:105"),
            new("F8", "tour-panels", three, 13, panelsCol, "oracle divider / source scale", true, uPanels,
                new[] { "Three heads are out: one builds the tour window, one writes the",
                        "pages and wires the Help menu, one photographs every page. I keep",
                        "the design and the integration." }, Array.Empty<Span>(), "TourCaptures+Hydra.swift:105"),
            new("F9", "tour-panels", "Use the same pictures on the website, zoomed in on the action.", 13, panelsCol - bubbleInset, "oracle column - 128", true, uPanels,
                new[] { "Use the same pictures on the website, zoomed", "in on the action." }, Array.Empty<Span>(), "TourCaptures+Hydra.swift:228"),
            new("F10", "tour-panels", "The site now shows only the new pictures, each cropped on what it is about. Uploading the set and deploying next.", 13, panelsCol, "oracle divider / source scale", true, uPanels,
                new[] { "The site now shows only the new pictures, each cropped on what it", "is about. Uploading the set and deploying next." }, Array.Empty<Span>(), "TourCaptures+Hydra.swift:240"),
            new("F11", "tour-recipes", "Pairs built from the models in your library: which one leads, at what effort, and which ones build. Add one and it is in the model picker.", 11, 460 - 32, "HydraCookbookPanel.swift:27,60 (460 - 2x16)", false, 0,
                new[] { "Pairs built from the models in your library: which one leads, at what effort, and", "which ones build. Add one and it is in the model picker." }, Array.Empty<Span>(), "HydraCookbookPanel.swift:22-23"),
        };
    }

    static TextLayout Layout(Fixture f, Faces faces, double width, double size, FontFeatureCollection? features = null)
    {
        var baseTf = new Typeface(faces.Sans, FontStyle.Normal, FontWeight.Normal);
        var overrides = new List<ValueSpan<TextRunProperties>>();
        foreach (var s in f.Spans)
        {
            TextRunProperties p = s.Kind switch
            {
                "bold" => new GenericTextRunProperties(new Typeface(faces.Sans, FontStyle.Normal, FontWeight.Bold), size, null, Brushes.Black, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, features),
                _ => new GenericTextRunProperties(new Typeface(faces.Mono), TypeScale.InlineCodeSize(size), null, Brushes.Black, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, features),
            };
            overrides.Add(new ValueSpan<TextRunProperties>(s.Start, s.Length, p));
        }
        return new TextLayout(f.Text, baseTf, size, Brushes.Black, TextAlignment.Left, TextWrapping.Wrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, width, double.PositiveInfinity, double.NaN, 0, 0, features, overrides);
    }

    static string[] LinesOf(TextLayout tl, string text)
        => tl.TextLines.Select(l => text.Substring(l.FirstTextSourceIndex, Math.Min(l.Length, text.Length - l.FirstTextSourceIndex)).TrimEnd()).ToArray();

    static bool Same(string[] a, string[] b) => a.Length == b.Length && a.Zip(b).All(p => p.First == p.Second);

    // The contiguous range of max widths (0.125 DIP steps) around w over which the layout reproduces
    // the oracle's breaks, or null when none does within +-150.
    static (double lo, double hi)? MatchInterval(Fixture f, Faces faces, double w, double size)
    {
        const double step = 0.125;
        var hits = new List<double>();
        for (double x = Math.Max(20, w - 150); x <= w + 150; x += step)
        {
            using var tl = Layout(f, faces, x, size);
            if (Same(LinesOf(tl, f.Text), f.OracleLines)) hits.Add(x);
        }
        if (hits.Count == 0) return null;
        // the run of consecutive hits nearest to w
        var runs = new List<(double lo, double hi)>();
        double lo = hits[0], prev = hits[0];
        foreach (var h in hits.Skip(1)) { if (h - prev > step * 1.5) { runs.Add((lo, prev)); lo = h; } prev = h; }
        runs.Add((lo, prev));
        return runs.OrderBy(r => w >= r.lo && w <= r.hi ? 0 : Math.Min(Math.Abs(w - r.lo), Math.Abs(w - r.hi))).First();
    }

    static (bool allOk, int matched) RunWrapSuite(List<Fixture> fixtures, Faces faces, string title, string oraclePath, bool detail)
    {
        Out.W();
        Out.W($"==== (A) WRAP POINTS: {title} ====");
        using var doc = JsonDocument.Parse(File.ReadAllText(oraclePath));
        var scales = doc.RootElement.GetProperty("scales_px_per_pt");
        var oLines = doc.RootElement.GetProperty("lines");
        int matched = 0;
        foreach (var f in fixtures)
        {
            using var tl = Layout(f, faces, f.Width, f.Size);
            var got = LinesOf(tl, f.Text);
            bool ok = Same(got, f.OracleLines);
            var iv = MatchInterval(f, faces, f.Width, f.Size);
            string robust = iv is null ? "no width in +-150 reproduces the oracle"
                : $"oracle breaks reproduced for W in [{iv.Value.lo:0.###}, {iv.Value.hi:0.###}]"
                  + (f.Approx ? $"; recorded W band [{f.Width - f.WidthUncertainty:0.##}, {f.Width + f.WidthUncertainty:0.##}] {(iv.Value.lo <= f.Width - f.WidthUncertainty && iv.Value.hi >= f.Width + f.WidthUncertainty ? "inside" : "NOT wholly inside")}" : "");
            if (ok) matched++;
            Out.W($"{f.Id,-4}{f.Oracle,-13} size {f.Size,4} W {f.Width,8:0.###} ({(f.Approx ? "APPROX, " : "")}{f.WidthSource})  oracle {f.OracleLines.Length} line(s), measured {got.Length}  -> {(ok ? "MATCH" : "MISMATCH")}");
            Out.W($"      {robust}");
            if (detail || !ok)
            {
                Out.W($"      faces used: {UsedFaces(tl)}");
                for (int i = 0; i < Math.Max(got.Length, f.OracleLines.Length); i++)
                {
                    var g = i < got.Length ? got[i] : "(none)";
                    var o = i < f.OracleLines.Length ? f.OracleLines[i] : "(none)";
                    var line = i < tl.TextLines.Count ? tl.TextLines[i] : null;
                    string m = line is null ? "" : $" [w={line.Width:0.###} wTrail={line.WidthIncludingTrailingWhitespace:0.###} h={line.Height:0.###} base={line.Baseline:0.###} brk={line.TextLineBreak?.TextEndOfLine?.GetType().Name ?? "null"}/split={line.TextLineBreak?.IsSplit}]";
                    Out.W($"      L{i + 1} measured: \"...{Tail(g)}\"{m}");
                    if (g != o) Out.W($"      L{i + 1} oracle  : \"...{Tail(o)}\"");
                }
                if (detail) OracleWidthComparison(f, faces, scales, oLines);
                if (detail) SelfConsistency(f, faces, tl);
            }
        }
        Out.W($"-> {matched}/{fixtures.Count} fixtures match the oracle ({title})");
        return (matched == fixtures.Count, matched);
    }

    static string Tail(string s) => s.Length <= 44 ? s : s[^44..];

    // Ink width of each ORACLE line (the oracle's own words), in points, against the advance width this
    // face measures for exactly those words laid out alone. Informational: ink extent and advance width
    // differ by the side bearings, around 1 pt, and the ratio is how much wider or narrower the face is.
    static void OracleWidthComparison(Fixture f, Faces faces, JsonElement scales, JsonElement oLines)
    {
        string key = f.Id switch
        {
            "F1" or "F2" => "F1+F2 welcome", "F3" => "F3 welcome", "F4" => "F4 welcome bubble", "F5" => "F5 welcome bubble",
            "F7" => "F7 hydra", "F8" => "F8 panels", "F9" => "F9 panels bubble", "F10" => "F10 panels", _ => "",
        };
        if (key == "" || !oLines.TryGetProperty(key, out var arr)) { Out.W("      (no oracle line widths: the page's scale is not source-derived)"); return; }
        double s = f.Oracle switch { "tour-hydra" => scales.GetProperty("tour-hydra").GetDouble(), _ => scales.GetProperty("tour-welcome").GetDouble() };
        var list = arr.EnumerateArray().ToList();
        int offset = f.Id == "F2" ? 2 : 0;
        int cursor = 0;
        for (int i = 0; i < f.OracleLines.Length && i + offset < list.Count; i++)
        {
            var x = list[i + offset].GetProperty("ink_x");
            double inkPt = (x[1].GetInt32() - x[0].GetInt32() + 1) / s;
            var words = f.OracleLines[i];
            int at = f.Text.IndexOf(words, cursor, StringComparison.Ordinal);
            if (at < 0) { Out.W($"      L{i + 1}: oracle words not found in the fixture text"); continue; }
            cursor = at + words.Length;
            var sub = f with
            {
                Text = words,
                Spans = f.Spans.Select(sp => sp with { Start = sp.Start - at }).Where(sp => sp.Start >= 0 && sp.Start + sp.Length <= words.Length).ToArray(),
            };
            using var one = Layout(sub, faces, double.PositiveInfinity, f.Size);
            double measured = one.Width;
            Out.W($"      oracle L{i + 1} words: oracle ink {inkPt,8:0.0} pt   this face's advance {measured,8:0.0} DIP   ratio {measured / inkPt:0.0000}");
        }
    }

    // The breaks are driven by measurement: every line fits, the next word would not, and the line's
    // width equals the sum of the glyph advances TextShaper returns for it.
    static void SelfConsistency(Fixture f, Faces faces, TextLayout tl)
    {
        for (int i = 0; i < tl.TextLines.Count; i++)
        {
            var line = tl.TextLines[i];
            double sumAdv = 0;
            foreach (var r in line.TextRuns)
                if (r is ShapedTextRun sr)
                    foreach (var gi in sr.ShapedBuffer) sumAdv += gi.GlyphAdvance;
            bool fits = line.Width <= f.Width + 1e-6;
            string nextInfo = "";
            if (i + 1 < tl.TextLines.Count)
            {
                var next = tl.TextLines[i + 1];
                var nextText = f.Text.Substring(next.FirstTextSourceIndex);
                int sp = nextText.IndexOf(' ');
                var firstWord = sp < 0 ? nextText : nextText[..sp];
                var withWord = f.Text.Substring(line.FirstTextSourceIndex, line.Length) + firstWord;
                var probe = f with { Text = withWord, Spans = f.Spans.Select(s => s with { Start = s.Start - line.FirstTextSourceIndex }).Where(s => s.Start >= 0 && s.Start + s.Length <= withWord.Length).ToArray() };
                using var one = Layout(probe, faces, double.PositiveInfinity, f.Size);
                nextInfo = $"; with next word '{firstWord}' = {one.Width:0.###} > W: {one.Width > f.Width}";
            }
            // independent TextShaper pass over the plain line text (single-style fixtures only)
            string shaperInfo = "";
            if (f.Spans.Length == 0 && FontManager.Current.TryGetGlyphTypeface(new Typeface(faces.Sans), out var gt))
            {
                var txt = f.Text.Substring(line.FirstTextSourceIndex, line.Length);
                using var buf = TextShaper.Current.ShapeText(txt, new TextShaperOptions(gt, f.Size, 0, CultureInfo.InvariantCulture, 0, 0, null));
                double a = 0; foreach (var gi in buf) a += gi.GlyphAdvance;
                shaperInfo = $"; TextShaper.ShapeText sum {a:0.###}";
            }
            Out.W($"      L{i + 1} check: fits={fits} runs' advance sum {sumAdv:0.###} vs wTrail {line.WidthIncludingTrailingWhitespace:0.###}{shaperInfo}{nextInfo}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    static bool RunControlScaleStep(List<Fixture> fixtures, Faces faces)
    {
        Out.W();
        Out.W("==== CA1 CONTROL: same corpus, one step up the type scale (13->15, 11->12) ====");
        int matched = 0;
        foreach (var f in fixtures)
        {
            double size = TypeScale.NextStepUp(f.Size);
            using var tl = Layout(f, faces, f.Width, size);
            var got = LinesOf(tl, f.Text);
            bool ok = Same(got, f.OracleLines);
            if (ok) matched++;
            Out.W($"{f.Id,-4} size {size,4}: measured {got.Length} line(s) vs oracle {f.OracleLines.Length} -> {(ok ? "match" : "MISMATCH")}");
        }
        Out.W($"-> CA1 {matched}/{fixtures.Count} match; control {(matched < fixtures.Count ? "FAILS as required" : "DID NOT FAIL (comparison insensitive)")}");
        return matched < fixtures.Count;
    }

    static bool RunControlNaive(List<Fixture> fixtures)
    {
        Out.W();
        Out.W("==== CA2 CONTROL: no shaping, fixed advance 0.5 em per character, greedy word wrap ====");
        int matched = 0;
        foreach (var f in fixtures)
        {
            double adv = 0.5 * f.Size;
            var words = f.Text.Split(' ');
            var lines = new List<string>();
            var cur = "";
            foreach (var w in words)
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (cand.Length * adv > f.Width && cur.Length > 0) { lines.Add(cur); cur = w; } else cur = cand;
            }
            if (cur.Length > 0) lines.Add(cur);
            bool ok = Same(lines.ToArray(), f.OracleLines);
            if (ok) matched++;
            Out.W($"{f.Id,-4}: naive {lines.Count} line(s) vs oracle {f.OracleLines.Length} -> {(ok ? "match" : "MISMATCH")}");
        }
        Out.W($"-> CA2 {matched}/{fixtures.Count} match; control {(matched < fixtures.Count ? "FAILS as required" : "DID NOT FAIL (comparison insensitive)")}");
        return matched < fixtures.Count;
    }

    // ---------------------------------------------------------------------------------------------
    sealed class FixtureSource : ITextSource
    {
        readonly string _text; readonly TextRunProperties _base; readonly List<(int s, int l, TextRunProperties p)> _runs;
        public FixtureSource(string text, TextRunProperties basep, List<(int, int, TextRunProperties)> runs) { _text = text; _base = basep; _runs = runs; }
        public TextRun GetTextRun(int i)
        {
            if (i >= _text.Length) return new TextEndOfParagraph();
            foreach (var (s, l, p) in _runs) if (i >= s && i < s + l) return new TextCharacters(_text.AsMemory(i, s + l - i), p);
            int end = _text.Length;
            foreach (var (s, _, _) in _runs) if (s > i && s < end) end = s;
            return new TextCharacters(_text.AsMemory(i, end - i), _base);
        }
    }

    static void TextFormatterCrossCheck(List<Fixture> fixtures, Faces faces)
    {
        Out.W();
        Out.W("==== TextFormatter.FormatLine cross-check (own ITextSource, previousLineBreak chained) ====");
        foreach (var f in fixtures)
        {
            var basep = new GenericTextRunProperties(new Typeface(faces.Sans), f.Size, null, Brushes.Black, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, null);
            var runs = f.Spans.Select(s => (s.Start, s.Length, (TextRunProperties)(s.Kind == "bold"
                ? new GenericTextRunProperties(new Typeface(faces.Sans, FontStyle.Normal, FontWeight.Bold), f.Size, null, Brushes.Black, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, null)
                : new GenericTextRunProperties(new Typeface(faces.Mono), TypeScale.InlineCodeSize(f.Size), null, Brushes.Black, null, BaselineAlignment.Baseline, CultureInfo.InvariantCulture, null)))).ToList();
            var src = new FixtureSource(f.Text, basep, runs);
            var para = new GenericTextParagraphProperties(basep, TextAlignment.Left, TextWrapping.Wrap, double.NaN, 0);
            var got = new List<string>();
            int idx = 0; TextLineBreak? prev = null; int guard = 0;
            var breaks = new List<string>();
            while (idx < f.Text.Length && guard++ < 50)
            {
                var line = TextFormatter.Current.FormatLine(src, idx, f.Width, para, prev);
                if (line is null) break;
                got.Add(f.Text.Substring(idx, Math.Min(line.Length, f.Text.Length - idx)).TrimEnd());
                var bounds = line.GetTextBounds(line.FirstTextSourceIndex, line.Length);
                breaks.Add($"{line.TextLineBreak?.TextEndOfLine?.GetType().Name ?? "null"}(bounds {bounds.Count}, x0={bounds.FirstOrDefault()?.Rectangle.X:0.##} w={bounds.Sum(b => b.Rectangle.Width):0.###})");
                idx += line.Length; prev = line.TextLineBreak;
                line.Dispose();
            }
            using var tl = Layout(f, faces, f.Width, f.Size);
            bool same = Same(got.ToArray(), LinesOf(tl, f.Text));
            Out.W($"{f.Id,-4}: FormatLine {got.Count} line(s), identical to TextLayout: {same}; line breaks: {string.Join(", ", breaks)}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The 50 monospaced sites: 22 `.monospaced` design sites and 28 `.monospacedDigit()` sites in the
    // pinned source (grep evidence in RESULT.md). Size and weight are the site's own, or the enclosing
    // .font the modifier applies to, read from the file at the line given.
    record Site(string Where, bool MonoDesign, double Size, FontWeight Weight, string SizeSource, bool DigitsInMonoFace = false);

    static List<Site> Sites() => new()
    {
        // .monospaced design (22)
        new("App/Views/Chat/ChatView.swift:1554", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/ChatView.swift:1687", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraMergePopover.swift:259", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraMergePopover.swift:289", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraReportsPopover.swift:584", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraReportsPopover.swift:615", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/TimelineRows.swift:2135", true, TypeScale.ChatSize(TypeScale.Style.Caption, 1), FontWeight.Normal, ".chat(.caption) at zoom 1"),
        new("App/Views/Chat/TimelineRows.swift:2164", true, TypeScale.ChatSize(TypeScale.Style.Caption, 1), FontWeight.Normal, ".chat(.caption) at zoom 1"),
        new("App/Views/Composer/RequestCards.swift:19", true, TypeScale.ChatBaseSize(TypeScale.Style.Callout), FontWeight.Normal, ".callout (ChatZoom table)"),
        new("App/Views/Diff/DiffInspector.swift:406", true, 11.5, FontWeight.Normal, "own"),
        new("App/Views/RootView.swift:444", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPKeyPanel.swift:199", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:215", true, 11, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:263", true, 10.5, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:730", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:744", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:756", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Settings/MCPSettingsPage.swift:1046", true, 10.5, FontWeight.Normal, "own"),
        new("App/Views/Settings/SettingsView.swift:697", true, 12, FontWeight.Normal, "own"),
        new("App/Views/Settings/SettingsView.swift:726", true, 12, FontWeight.Normal, "own"),
        new("UI/Common/AttachmentPreview.swift:317", true, TypeScale.ChatBaseSize(TypeScale.Style.Caption), FontWeight.Normal, ".caption (ChatZoom table)"),
        new("UI/Markdown/MarkdownView.swift:1301", true, TypeScale.ChatSize(TypeScale.Style.Callout, 1), FontWeight.Normal, ".chat(.callout) at zoom 1"),
        // .monospacedDigit() (28)
        new("App/Views/Chat/HydraButton.swift:630", false, 9, FontWeight.Bold, "own"),
        new("App/Views/Chat/HydraHeadPopovers.swift:25", false, 11, FontWeight.Medium, "enclosing .font :23"),
        new("App/Views/Chat/HydraMergePopover.swift:273", false, 17, FontWeight.SemiBold, "own"),
        new("App/Views/Chat/HydraPanel.swift:235", false, 12.5, FontWeight.SemiBold, "own"),
        new("App/Views/Chat/HydraPanel.swift:462", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraProgressBar.swift:559", false, 11, FontWeight.Normal, "enclosing .font :558"),
        new("App/Views/Chat/HydraReportsPopover.swift:309", false, 11, FontWeight.Normal, "enclosing .font :307"),
        new("App/Views/Chat/HydraReportsPopover.swift:336", false, 11, FontWeight.Normal, "enclosing .font :334"),
        new("App/Views/Chat/HydraReportsPopover.swift:341", false, 11, FontWeight.Normal, "enclosing .font :339"),
        new("App/Views/Chat/HydraReportsPopover.swift:549", false, 11, FontWeight.Normal, "enclosing .font :547"),
        new("App/Views/Chat/HydraReportsPopover.swift:612", false, 11, FontWeight.Normal, "enclosing .font :615 is .monospaced design", DigitsInMonoFace: true),
        new("App/Views/Chat/HydraRunMetricsView.swift:249", false, 12, FontWeight.Medium, "own"),
        new("App/Views/Chat/HydraRunMetricsView.swift:305", false, 12, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraRunMetricsView.swift:331", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/HydraRunMetricsView.swift:357", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Chat/TimelineRows.swift:1845", false, TypeScale.ChatSize(TypeScale.Style.Caption, 1), FontWeight.Normal, ".chat(.caption) at zoom 1"),
        new("App/Views/Composer/FollowUpQueueTab.swift:70", false, 12, FontWeight.Medium, "own"),
        new("App/Views/Composer/FollowUpQueueTab.swift:374", false, 11, FontWeight.Medium, "own"),
        new("App/Views/Composer/QuestionTab.swift:96", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Composer/ThreadChangesTab.swift:30", false, 12, FontWeight.Medium, "own"),
        new("App/Views/Sidebar/SidebarView.swift:1130", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Sidebar/SidebarView.swift:2387", false, 12, FontWeight.Normal, "enclosing .font :2386"),
        new("App/Views/Sidebar/SidebarView.swift:2692", false, 11, FontWeight.Normal, "own"),
        new("App/Views/Usage/UsagePanel.swift:41", false, 13, FontWeight.Normal, "enclosing .font :43"),
        new("App/Views/Usage/UsageViews.swift:114", false, 13, FontWeight.SemiBold, "enclosing .font :113"),
        new("App/Views/Usage/UsageViews.swift:161", false, 12, FontWeight.Normal, "enclosing .font :159"),
        new("UI/Common/Components.swift:54", false, TypeScale.ChatBaseSize(TypeScale.Style.Caption), FontWeight.Normal, ".caption (ChatZoom table)"),
        new("UI/Markdown/MarkdownView.swift:515", false, 13, FontWeight.Medium, "inherited markdown size, default 13 (MarkdownView.swift:839); .fontWeight(.medium) :516"),
    };

    // Columns hold when every row of equal length puts character i at the same x.
    static double MaxColumnSpread(string[] rows, Typeface tf, double size, FontFeatureCollection? features, out string used)
    {
        var xs = new List<double[]>();
        var usedSet = new SortedSet<string>();
        foreach (var r in rows)
        {
            using var tl = new TextLayout(r, tf, size, Brushes.Black, TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.None,
                null, FlowDirection.LeftToRight, double.PositiveInfinity, double.PositiveInfinity, double.NaN, 0, 0, features, null);
            usedSet.Add(UsedFaces(tl));
            var x = new double[r.Length + 1];
            for (int i = 0; i < r.Length; i++) x[i] = tl.HitTestTextPosition(i).X;
            x[r.Length] = tl.WidthIncludingTrailingWhitespace;
            xs.Add(x);
        }
        used = string.Join(" | ", usedSet);
        double spread = 0;
        for (int i = 0; i < xs[0].Length; i++)
        {
            double mn = xs.Min(a => a[i]), mx = xs.Max(a => a[i]);
            spread = Math.Max(spread, mx - mn);
        }
        return spread;
    }

    // Advance of every character in a set, through TextShaper, with the GlyphTypeface the layout resolves.
    static (double min, double max, int missing, string face, string perChar, string features) Advances(string chars, Typeface tf, double size, FontFeatureCollection? features)
    {
        if (!FontManager.Current.TryGetGlyphTypeface(tf, out var gt)) return (double.NaN, double.NaN, chars.Length, "(unresolved)", "", "");
        double mn = double.MaxValue, mx = double.MinValue; int missing = 0;
        var per = new StringBuilder();
        foreach (var ch in chars)
        {
            if (gt.CharacterToGlyphMap.GetGlyph(ch) == 0) { missing++; per.Append($"{ch}:missing "); continue; }
            using var buf = TextShaper.Current.ShapeText(ch.ToString(), new TextShaperOptions(gt, size, 0, CultureInfo.InvariantCulture, 0, 0, features?.ToList()));
            double a = 0; foreach (var gi in buf) a += gi.GlyphAdvance;
            mn = Math.Min(mn, a); mx = Math.Max(mx, a);
            per.Append($"{ch}:{a:0.###} ");
        }
        var feats = string.Join(",", gt.SupportedFeatures.Select(t => t.ToString()).Where(t => t is "tnum" or "pnum" or "lnum" or "onum"));
        return (mn, mx, missing, $"{gt.FamilyName}[w{(int)gt.Weight},{gt.FontSimulations}]", per.ToString().TrimEnd(), feats);
    }

    static (bool primaryOk, bool cb1Failed, bool cb2Failed, bool allCascadiaOk) RunMonospaceSuite(FontFamily? cascadia, Faces faces, bool verbose)
    {
        const double tol = 0.01; // DIP
        var tnum = FontFeatureCollection.Parse("+tnum");
        var pnum = FontFeatureCollection.Parse("+pnum");
        // ASCII printable plus the non-ASCII characters the monospaced sites put in their strings
        // (U+2212 minus at HydraReportsPopover.swift:611, U+00B7 middle dot, U+2026 ellipsis, U+2192 arrow).
        string ascii = new(Enumerable.Range(0x20, 0x5F).Select(c => (char)c).ToArray());
        string extras = "−·…→";
        string charset = ascii + extras;
        // Rows of equal length that differ in every column.
        string[] monoRows =
        {
            "src/App/RootView.swift:444  +12 −3  {id: 0x1F}",
            "MCPSettingsPage.swift:1046  +90 −8  [ok, ·…→]",
            "WWWWWWWWWWWWWWWWWWWWWWWWWW  iiiiiiiiiiiiiiiiiiiiiiii",
        };
        monoRows[2] = monoRows[2][..monoRows[0].Length];
        monoRows[1] = monoRows[1].PadRight(monoRows[0].Length)[..monoRows[0].Length];
        // Digit rows: one skeleton, every digit slot filled differently.
        string skel = "## of ## done · #m ##s · +### −## · ###.#K / ##K (##%)";
        string Fill(string s, Func<int, char> d) { var sb = new StringBuilder(); int k = 0; foreach (var c in s) sb.Append(c == '#' ? d(k++) : c); return sb.ToString(); }
        string[] digitRows = { Fill(skel, _ => '1'), Fill(skel, _ => '8'), Fill(skel, _ => '0'), Fill(skel, k => (char)('0' + (k * 7 + 3) % 10)) };

        Out.W();
        Out.W($"==== (B) 50 MONOSPACED SITES: .monospaced in Cascadia Mono, .monospacedDigit in '{faces.Name}' +tnum ====");
        Out.W($"tolerance {tol} DIP; mono rows length {monoRows[0].Length}; digit skeleton \"{skel}\"");
        var monoFamily = cascadia ?? new FontFamily("Cascadia Mono");
        bool allOk = true, cb1AllFail = true, cb2AllFail = true, allCascadia = true;
        int nMono = 0, nDigit = 0, nOk = 0, nWeightMissed = 0;
        var failures = new List<string>();
        foreach (var s in Sites())
        {
            if (s.MonoDesign)
            {
                nMono++;
                var tf = new Typeface(monoFamily, FontStyle.Normal, s.Weight);
                var (mn, mx, miss, face, _, _) = Advances(charset, tf, s.Size, null);
                double spread = MaxColumnSpread(monoRows, tf, s.Size, null, out var used);
                bool ok = miss == 0 && mx - mn < tol && spread < tol && used.StartsWith("Cascadia Mono", StringComparison.Ordinal) && !used.Contains(" + ");
                var ctf = new Typeface(faces.Sans, FontStyle.Normal, s.Weight);
                double cSpread = MaxColumnSpread(monoRows, ctf, s.Size, null, out var cUsed);
                bool cFails = cSpread >= tol;
                allOk &= ok; allCascadia &= ok; cb1AllFail &= cFails; if (ok) nOk++; else failures.Add(s.Where);
                if (verbose)
                {
                    Out.W($"MONO  {s.Where,-46} {s.Size,5} w{(int)s.Weight} [{s.SizeSource}]");
                    Out.W($"      {face}: {charset.Length} chars, missing {miss}, advance {mn:0.####}..{mx:0.####}; row column spread {spread:0.####} -> {(ok ? "ALIGNED" : "NOT ALIGNED")}  used: {used}");
                    Out.W($"      CB1 proportional {cUsed}: spread {cSpread:0.###} -> {(cFails ? "misaligned (control fails as required)" : "ALIGNED (control did not fail)")}");
                }
            }
            else
            {
                nDigit++;
                var fam = s.DigitsInMonoFace ? monoFamily : faces.Sans;
                var tf = new Typeface(fam, FontStyle.Normal, s.Weight);
                var (mn, mx, miss, face, per, feats) = Advances("0123456789", tf, s.Size, tnum);
                double spread = MaxColumnSpread(digitRows, tf, s.Size, tnum, out var used);
                var (dmn, dmx, _, _, _, _) = Advances("0123456789", tf, s.Size, null);
                double dSpread = MaxColumnSpread(digitRows, tf, s.Size, null, out _);
                string famName = face.Split('[')[0];
                bool ok = miss == 0 && mx - mn < tol && spread < tol && used.StartsWith(famName, StringComparison.Ordinal) && !used.Contains(" + ");
                bool weightHonoured = face.Contains($"[w{(int)s.Weight},None]");
                if (!weightHonoured) nWeightMissed++;
                // Control: the capability removed. For a digit site in the system face, proportional figures
                // forced on (pnum). For the one digit site inside a .monospaced run, the proportional face.
                double cSpread; string cDesc;
                if (s.DigitsInMonoFace)
                {
                    cSpread = MaxColumnSpread(digitRows, new Typeface(faces.Sans, FontStyle.Normal, s.Weight), s.Size, pnum, out var cu);
                    cDesc = $"CB1 proportional {cu} +pnum";
                }
                else
                {
                    var (pmn, pmx, _, _, _, _) = Advances("0123456789", tf, s.Size, pnum);
                    cSpread = MaxColumnSpread(digitRows, tf, s.Size, pnum, out _);
                    cDesc = $"CB2 +pnum (digit advance {pmn:0.####}..{pmx:0.####})";
                }
                bool cFails = cSpread >= tol;
                allOk &= ok; if (ok) nOk++; else failures.Add(s.Where);
                if (s.DigitsInMonoFace) cb1AllFail &= cFails; else cb2AllFail &= cFails;
                // Alternative reading: every site in Cascadia Mono with tnum.
                var atf = new Typeface(monoFamily, FontStyle.Normal, s.Weight);
                double aSpread = MaxColumnSpread(digitRows, atf, s.Size, tnum, out var aUsed);
                bool aOk = aSpread < tol && aUsed.StartsWith("Cascadia Mono", StringComparison.Ordinal) && !aUsed.Contains(" + ");
                allCascadia &= aOk;
                if (verbose || !ok)
                {
                    Out.W($"DIGIT {s.Where,-46} {s.Size,5} w{(int)s.Weight} [{s.SizeSource}]");
                    Out.W($"      {face} +tnum: digit advance {mn:0.####}..{mx:0.####}, row spread {spread:0.####} -> {(ok ? "ALIGNED" : "NOT ALIGNED")}  used: {used}; weight honoured: {weightHonoured}; face lists [{feats}]");
                    if (!ok) Out.W($"      per digit +tnum: {per}");
                    Out.W($"      no feature: digit advance {dmn:0.####}..{dmx:0.####}, spread {dSpread:0.####};  {cDesc}: spread {cSpread:0.###} -> {(cFails ? "misaligned (control fails as required)" : "ALIGNED (control did not fail)")}");
                    Out.W($"      alt reading, Cascadia Mono +tnum: spread {aSpread:0.####} -> {(aOk ? "ALIGNED" : "NOT ALIGNED")}  used: {aUsed}");
                }
            }
        }
        Out.W($"-> face '{faces.Name}': {nOk}/{nMono + nDigit} sites hold alignment ({nMono} .monospaced + {nDigit} .monospacedDigit); requested weight not honoured by a real face at {nWeightMissed} digit site(s)");
        if (failures.Count > 0) Out.W($"   not aligned: {string.Join(", ", failures)}");
        Out.W($"   CB1 fails at every site it covers: {cb1AllFail}; CB2 fails at every site it covers: {cb2AllFail}; all 50 under Cascadia Mono +tnum: {allCascadia}");

        if (verbose)
        {
            // What happens if the app asks for Cascadia Mono by name on this machine.
            double sysSpread = MaxColumnSpread(monoRows, new Typeface("Cascadia Mono"), 12, null, out var sysUsed);
            Out.W($"by-name request 'Cascadia Mono' (system lookup, 12pt): used {sysUsed}; row spread {sysSpread:0.###}");
        }
        return (allOk, cb1AllFail, cb2AllFail, allCascadia);
    }
}
