using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// OD-0102 reaches every Border whose corner can sit in ED-275 (2)'s band (h / 3.057 &lt; r &lt; h / 2), not only the ones that call `.RoundedRectangle(...)`. Avalonia fills the
/// circle of a plain Border's CornerRadius, and Apple's corner at the largest radius that fits lies outside that circle near the diagonal, so a raw `CornerRadius = new
/// CornerRadius(r)` Border whose Swift is `.continuous` and whose height can be between 2 r and 3.057 r draws option B, not the owner's option A (the blitz-cornerband
/// re-review: "about 60 sites"; ED-275 (7) lists them as NOT DONE). This table accounts for EVERY such assignment in src: each is routed through
/// <see cref="SwiftShapes.RoundedRectangle{T}"/> (its fill, clip and ring then follow the curve) or is classed with the reason it never reaches the band. A new raw site, or a
/// routed one that loses its call, fails here.
/// </summary>
[Collection("Headless")]
public sealed class ContinuousCornerSitesTests(HeadlessSession headless)
{
    /// <summary>Swift `.continuous`, and the element can be between 2 r and 3.057 r tall: it must call RoundedRectangle (or UnevenRoundedRectangle).</summary>
    private const string Routed = "Routed";

    /// <summary>The radius is half the height or more by construction (999, 1000, height / 2, a pill of known height): the capsule regime, circular in both.</summary>
    private const string Capsule = "Capsule";

    /// <summary>A square element with radius half its side, or a circle by construction.</summary>
    private const string Circle = "Circle";

    /// <summary>A radius of at most 3 pt on a bar, tick, dot or cell (a capsule or a pixel-sized rectangle): no band a pixel shows.</summary>
    private const string Tiny = "Tiny";

    /// <summary>The Swift is `.circular` or a CALayer cornerRadius (HydraMergePopover.swift:126, DragHandle.swift:84-85), or the site is a design-page layout, not a Swift shape.</summary>
    private const string Circular = "Circular";

    /// <summary>
    /// Swift `.continuous` (or a Windows control theme), but the element is a panel, window, card or row taller than 3.057 r at every state, so Apple's corner at the stated
    /// radius fits and the band is not reached; the circle of the same radius differs from it by at most 0.0138 r (ED-275 (7), tracked, outside OD-0102).
    /// </summary>
    private const string Fit = "Fit";

    /// <summary>Not a Border drawn by Avalonia (a Panel's own property), a zero radius, or the shape helpers themselves.</summary>
    private const string Other = "Other";

    // (file under src, the radius argument as written, how many assignments, kind)
    private static readonly (string File, string Radius, int Count, string Kind)[] Sites =
    [
        ("Marrow.App/Port/ChatDialogControls.cs", "6", 1, Fit),
        ("Marrow.App/Port/LookCard.cs", "10", 1, Circular),
        ("Marrow.App/Port/LookCard.cs", "12", 1, Circular),
        ("Marrow.App/Views/Chat/ChatBadge.cs", "TimelineMetrics.PillRadius", 1, Other),
        ("Marrow.App/Views/Chat/HydraButton.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "10", 1, Fit),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "12", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraMergePopover.cs", "6", 1, Circular),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "14", 1, Circle),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "16", 1, Routed),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "6", 1, Fit),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "Chrome.CapsuleHeight / 2", 1, Capsule),
        ("Marrow.App/Views/Chat/HydraPanel.cs", "CornerRadius", 1, Fit),
        ("Marrow.App/Views/Chat/HydraProgressBar.cs", "Chrome.CardCornerRadius", 2, Routed),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "10", 1, Fit),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "6", 1, Fit),
        ("Marrow.App/Views/Chat/HydraReportsPopover.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "Chrome.CapsuleHeight / 2", 1, Capsule),
        ("Marrow.App/Views/Chat/SubagentPanel.cs", "CornerRadius", 2, Fit),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Chat/ThreadTimeline.cs", "TimelineMetrics.PillRadius", 2, Routed),
        ("Marrow.App/Views/Chat/TimelineMinimap.cs", "1", 1, Tiny),
        ("Marrow.App/Views/Chat/TimelineRows.cs", "14", 1, Circle),
        ("Marrow.App/Views/Composer/ComposerTextView.cs", "0", 1, Other),
        ("Marrow.App/Views/Composer/FollowUpQueueTab.cs", "12, 12, 0, 0", 1, Fit),
        ("Marrow.App/Views/Composer/FollowUpQueueTab.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Composer/HydraFusedPairIcon.cs", "0.5", 1, Tiny),
        ("Marrow.App/Views/Composer/PromptEditor.cs", "12", 1, Routed),
        ("Marrow.App/Views/Composer/QuestionTab.cs", "3", 1, Circle),
        ("Marrow.App/Views/Composer/ThreadChangesTab.cs", "12, 12, 0, 0", 1, Fit),
        ("Marrow.App/Views/Diff/DiffInspector.cs", "1000", 1, Capsule),
        ("Marrow.App/Views/Diff/DiffInspector.cs", "12", 1, Routed),
        ("Marrow.App/Views/Settings/AboutUpdateViews.cs", "1.5", 1, Tiny),
        ("Marrow.App/Views/Settings/MCPKeyPanel.cs", "8", 1, Fit),
        ("Marrow.App/Views/Settings/MCPKeyPanel.cs", "9", 1, Circle),
        ("Marrow.App/Views/Settings/MCPKeyPanel.cs", "Chrome.WindowCornerRadius", 1, Fit),
        ("Marrow.App/Views/Settings/MCPSetupPanel.cs", "9", 1, Circle),
        ("Marrow.App/Views/Settings/MCPSetupPanel.cs", "Chrome.WindowCornerRadius", 1, Fit),
        ("Marrow.App/Views/Settings/ModelsSettingsPage.cs", "10", 1, Fit),
        ("Marrow.App/Views/Settings/ModelsSettingsPage.cs", "Chrome.CardCornerRadius", 1, Fit),
        ("Marrow.App/Views/Settings/TokenActivitySection.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Settings/TokenActivitySection.cs", "TokenActivityStyle.CellRadius", 1, Tiny),
        ("Marrow.App/Views/Sidebar/FloatingSidebarPanel.cs", "22", 2, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "16, 16, 0, 0", 1, Fit),
        ("Marrow.App/Views/Terminal/TerminalPanel.cs", "999", 1, Capsule),
        ("Marrow.App/Views/Tour/TourView.cs", "CardCornerRadius", 4, Fit),
        ("Marrow.App/Views/Usage/UsageViews.cs", "2.5", 2, Tiny),
        ("Marrow.UI/Chrome/Chrome.cs", "side / 2", 1, Circle),
        ("Marrow.UI/Chrome/ChromeSegmentedPicker.cs", "11", 1, Capsule),
        ("Marrow.UI/Chrome/DragHandle.cs", "Metrics.GlowRadius", 1, Circular),
        ("Marrow.UI/Chrome/DragHandle.cs", "grip.Thickness / 2", 1, Capsule),
        ("Marrow.UI/Port/PortControlThemes.cs", "10", 1, Circle),
        ("Marrow.UI/Port/PortControlThemes.cs", "2", 1, Tiny),
        ("Marrow.UI/Port/PortControlThemes.cs", "7", 1, Circle),
        ("Marrow.UI/Port/PortControlThemes.cs", "7 + 3 * fraction", 1, Circle),
        ("Marrow.UI/Port/SwiftShapeBorder.cs", "cornerRadius", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "CapsuleCornerRadius(border.Bounds.Size)", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "CapsuleCornerRadius(e.NewSize)", 1, Other),
        ("Marrow.UI/Port/SwiftShapes.cs", "cornerRadius", 1, Other),
        ("Marrow.UI/Port/SystemButtons.cs", "Radius", 2, Fit),
        ("Marrow.UI/Port/SystemButtons.cs", "Radius + FocusBand", 1, Fit),
    ];

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "..", "..", "..", "..", "src"));

    // Every `CornerRadius = new CornerRadius(<arg>)` assignment under src, with the statement it sits in (to the `;` that ends it, past the initializer's closing brace).
    private static List<(string File, string Radius, string Statement)> Found()
    {
        var root = SourceRoot();
        var found = new List<(string, string, string)>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, @"CornerRadius\s*=\s*new\s*(?:CornerRadius)?\s*\("))
            {
                int i = m.Index + m.Length, depth = 1, j = i;
                while (depth > 0 && j < text.Length) { depth += text[j] == '(' ? 1 : text[j] == ')' ? -1 : 0; j++; }
                var radius = text[i..(j - 1)].Trim();
                int braces = 0, k = j;
                while (k < text.Length)
                {
                    if (text[k] == '{') braces++;
                    else if (text[k] == '}') braces--;
                    else if (text[k] == ';' && braces <= 0) break;
                    k++;
                }
                found.Add((relative, radius, text[m.Index..Math.Min(k + 1, text.Length)]));
            }
        }
        return found;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0102 / ED-275 (7): the table is the proof that no raw CornerRadius Border is left unaccounted for; the pixels of the routed ones are below
    public void Every_raw_CornerRadius_assignment_is_routed_through_the_continuous_shape_or_classed_with_why_it_never_reaches_the_band()
    {
        var found = Found();
        var problems = new List<string>();
        foreach (var group in found.GroupBy(f => (f.File, f.Radius)))
        {
            var entry = Sites.Where(s => s.File == group.Key.File && s.Radius == group.Key.Radius).ToArray();
            if (entry.Length != 1) { problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) x{group.Count()} is not in the table"); continue; }
            if (entry[0].Count != group.Count()) problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) is {group.Count()} assignments, the table says {entry[0].Count}");
            if (entry[0].Kind == Routed && group.Any(g => !g.Statement.Contains("RoundedRectangle(", StringComparison.Ordinal)))
                problems.Add($"{group.Key.File}: CornerRadius({group.Key.Radius}) is a Swift `.continuous` Border that can sit in the band and does not call RoundedRectangle(...) / UnevenRoundedRectangle(...)");
        }
        foreach (var entry in Sites)
            if (!found.Any(f => f.File == entry.File && f.Radius == entry.Radius)) problems.Add($"{entry.File}: CornerRadius({entry.Radius}) is in the table but no longer in the source");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ---- the routed sites on the screen ----------------------------------------------------------------------------------------------------------------------

    private static HydraHeadInfo Info() =>
        new HydraHeadInfo(0, "Task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, ToolCalls = 3 };

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:466-471 the footer bar is a 16-point continuous rounded rectangle; one line tall it is about 37 pt, in the band (32.1 < h < 48.9)
    public void The_head_footer_bar_one_line_tall_draws_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var footer = new HydraHeadFooter(Info()) { VerticalAlignment = VerticalAlignment.Bottom };
        var window = new Avalonia.Controls.Window { Content = new Grid { Children = { footer } }, Width = 420, Height = 200 };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var bar = Assert.IsType<Border>(footer.Child);
            Assert.InRange(bar.Bounds.Height, 16 * 2 + 0.01, 16 * 2 * ContinuousCorner.Extent - 0.01);
            Assert.Equal(new CornerRadius(16), SwiftShapes.StatedCornerRadius(bar));
            Assert.True(ContinuousCornerChecks.Drawn(bar, expectApple: true));
            // The Border fills no circle of its own: the clip (Apple's path) makes the corner.
            Assert.Equal(default, bar.CornerRadius);
            Assert.NotNull(bar.Clip);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraProgressBar.swift:571-578 ProgressCardSurface: the card is a 16-point continuous rounded rectangle (Chrome.cardCornerRadius)
    public void The_progress_cards_surface_is_a_continuous_shape_that_draws_Apples_corner_in_the_band() => headless.OnUiThread(() =>
    {
        var surface = Assert.IsType<Border>(new ProgressCardSurface(IsOnGlassPanel: true, IsDark: true).Body(new Border { Height = 37, Width = 260 }));
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Content = new Grid { Children = { surface } }, Width = 400, Height = 120 };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
            Assert.Equal(new CornerRadius(ChromeTokens.CardCornerRadius), SwiftShapes.StatedCornerRadius(surface));
            Assert.True(ContinuousCornerChecks.Drawn(surface, expectApple: true));
        }
        finally { window.Close(); }
    });
    // ---- REVIEW (blitz-cornerband CB-0 re-check, 2026-10-04) ------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: the table's census reads only `CornerRadius = new CornerRadius(...)`; a Border given its radius from a variable or a ternary is never counted
    public void REVIEW_every_CornerRadius_assignment_in_src_is_in_the_census_not_only_the_new_CornerRadius_ones()
    {
        var root = SourceRoot();
        var missed = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
            if (relative is "Marrow.UI/Port/SwiftShapes.cs" or "Marrow.UI/Port/SwiftShapeBorder.cs") continue; // the shape helpers themselves
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                // an assignment (object initializer or property set) of a Border's CornerRadius whose value is not a `new CornerRadius(...)` the census reads
                var m = Regex.Match(lines[i], @"\bCornerRadius\s*=(?![=>])(?!\s*new\s*(?:CornerRadius)?\s*\()(.+)");
                if (!m.Success) continue;
                var line = lines[i].TrimStart();
                if (line.StartsWith("private const", StringComparison.Ordinal) || line.StartsWith("public const", StringComparison.Ordinal)
                    || line.StartsWith("internal const", StringComparison.Ordinal) || line.Contains("double CornerRadius", StringComparison.Ordinal)) continue;
                missed.Add($"{relative}:{i + 1}: {line}");
            }
        }
        Assert.True(missed.Count == 0, "CornerRadius assignments outside the census (each is a raw Border the table neither routes nor classes):" + Environment.NewLine + string.Join(Environment.NewLine, missed));
    }
}
