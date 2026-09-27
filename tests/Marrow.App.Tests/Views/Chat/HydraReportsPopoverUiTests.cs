// Source: DroppyCode/App/Views/Chat/HydraReportsPopover.swift (B2 1fbf81e), :207-654.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraReportsPopoverUiTests(HeadlessSession headless)
{
    private const string Minus = "\U00002212"; // U+2212 MINUS SIGN
    private const string MiddleDot = "\U000000B7"; // U+00B7 MIDDLE DOT

    private static HydraPersona Persona(string name, uint hex = 0x4D82C4) =>
        new(name, name, hex);

    private static string SingleHeadReport(string body = "Streaming report body text.") =>
        "## Juno: stream the parser (high)\n" +
        "Landed in your checkout: src/A.cs (+12 " + Minus + "3), src/B.cs (+1 " + Minus + "1) with conflicts.\n" +
        body;

    private static string MultiHeadReport() =>
        "Juno is still at work on the parser.\n\n" +
        "## Juno: stream the parser (high)\n" +
        "Landed in your checkout: src/A.cs (+12 " + Minus + "3), src/B.cs (+1 " + Minus + "1) with conflicts.\n" +
        "Juno's report body.\n\n" +
        "## Hank: Hank: sweep the tests\n" +
        "Did not land (patch rejected)\n" +
        "No report.";

    private static Control Attach(Control view)
    {
        var root = new Decorator { Child = view };
        root.Measure(new Size(600, 900));
        root.Arrange(new Rect(0, 0, 600, 900));
        return root;
    }

    /// <summary>
    /// Shows a multi-head popover the way it appears: in a window, which is when its <c>.task(id: text)</c> runs
    /// (HydraReportsPopover.swift:253-265; AttachedToVisualTree in the port). A bare Measure/Arrange never attaches,
    /// so that task never starts. The task parses the collapsed heads' reports off main and publishes them
    /// (<c>blocks = warmed</c>, :264), which rebuilds the popover's content. The dispatcher runs until that happens,
    /// so opening a card then finds its blocks ready (:254-255) instead of the spinner it shows before (:649-653).
    /// </summary>
    private static Avalonia.Controls.Window Present(HydraReportsPopover popover)
    {
        var warmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        popover.PropertyChanged += (_, change) =>
        {
            if (change.Property == Decorator.ChildProperty) warmed.TrySetResult();
        };
        var window = new Avalonia.Controls.Window { Width = 600, Height = 900, Content = popover };
        window.Show();
        HeadlessSession.RunDispatcherUntil(warmed.Task, TimeSpan.FromSeconds(10));
        return window;
    }

    private static IEnumerable<Control> Controls(Control root) =>
        LogicalQuery.SelfAndDescendants<Control>(root);

    private static string TextOf(TextBlock block)
    {
        if (!string.IsNullOrEmpty(block.Text)) return block.Text;

        return block.Inlines is { } inlines
            ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
            : string.Empty;
    }

    private static IReadOnlyList<string> TextsOf(Control root) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(TextOf).ToArray();

    private static string CombinedText(Control root) => string.Concat(TextsOf(root));

    private static Button? ButtonContaining(Control root, string text) =>
        Controls(root).OfType<Button>()
            .FirstOrDefault(button => CombinedText(button).Contains(text, StringComparison.Ordinal));

    [Fact]
    [Trait("Req", "PR-HYDRA-203")] // App/Views/Chat/HydraReportsPopover.swift:218-266
    public void Popover_Is460PointsWideAndPreparesTheSingleHeadReportBeforeLayout()
    {
        headless.OnUiThread(() =>
        {
            var root = Attach(new HydraReportsPopover(
                "Juno: streaming path",
                SingleHeadReport(),
                [Persona("Juno")]));

            var popover = Assert.IsType<HydraReportsPopover>(
                Controls(root).First(control => control is HydraReportsPopover));
            Assert.Equal(460, popover.Width);
            Assert.Contains("Streaming report body text.", CombinedText(root), StringComparison.Ordinal);
            Assert.DoesNotContain("Failed", TextsOf(root));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-203")] // App/Views/Chat/HydraReportsPopover.swift:228-252
    public void Popover_WithSeveralHeadsStartsCollapsedAndShowsTheirLandingRows()
    {
        headless.OnUiThread(() =>
        {
            var root = Attach(new HydraReportsPopover(
                "Batch report",
                MultiHeadReport(),
                [Persona("Juno"), Persona("Hank")]));

            // Starts collapsed (:506-508 gates reportBody on expanded.contains(head.name), empty at
            // construction): report bodies stay hidden, but landing rows render unconditionally (:505).
            Assert.DoesNotContain("Juno's report body.", CombinedText(root), StringComparison.Ordinal);
            Assert.DoesNotContain("No report.", CombinedText(root), StringComparison.Ordinal);
            Assert.Contains("src/A.cs", CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-203")] // App/Views/Chat/HydraReportsPopover.swift:228-234, :494-508
    public void Popover_MultiHeadReportsAreHiddenUntilTheirNameRowIsOpened()
    {
        headless.OnUiThread(() =>
        {
            var root = new HydraReportsPopover(
                "Batch report",
                MultiHeadReport(),
                [Persona("Juno"), Persona("Hank")]);
            var window = Present(root);
            try
            {
                Assert.DoesNotContain("Juno's report body.", CombinedText(root), StringComparison.Ordinal);
                var toggle = Assert.IsAssignableFrom<Button>(ButtonContaining(root, "Juno"));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Juno's report body.", CombinedText(root), StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-204")] // App/Views/Chat/HydraReportsPopover.swift:277-315
    [Trait("Req", "UI-HYDRA-036")] // App/Views/Chat/HydraReportsPopover.swift:277-293
    public void MultiHeadHeader_ShowsNineGlyphsAndAnOverflowPillForTheRemainingHeads()
    {
        headless.OnUiThread(() =>
        {
            var personas = Enumerable.Range(0, 11).Select(index => Persona("Face" + index)).ToArray();
            var root = Attach(new HydraReportsPopover(
                "Batch report", MultiHeadReport(), personas));

            Assert.Equal(9, LogicalQuery.SelfAndDescendants<HydraGlyph>(root).Count(glyph => glyph.Size == 22));
            Assert.Contains("+2", CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-206")] // App/Views/Chat/HydraReportsPopover.swift:415-432
    [Trait("Req", "UI-HYDRA-037")] // App/Views/Chat/HydraReportsPopover.swift:299-302, :415-432
    public void MultiHeadHeader_TotalsIncludeFilesFromBothLandingShapesAndUseTheExactSeparators()
    {
        headless.OnUiThread(() =>
        {
            var root = Attach(new HydraReportsPopover(
                "Batch report", MultiHeadReport(), [Persona("Juno"), Persona("Hank")]));

            Assert.Contains("2 heads " + MiddleDot + " 2 files " + MiddleDot + " +13 " + Minus + "4",
                CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-208")] // App/Views/Chat/HydraReportsPopover.swift:435-445
    [Trait("Req", "UI-HYDRA-038")] // App/Views/Chat/HydraReportsPopover.swift:303-308, :435-445
    public void Header_ShowsTheStillAtWorkSentenceFromTheIntro()
    {
        headless.OnUiThread(() =>
        {
            var root = Attach(new HydraReportsPopover(
                "Batch report", MultiHeadReport(), [Persona("Juno"), Persona("Hank")]));

            Assert.Contains("Juno is still at work on the parser.", CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-208")] // App/Views/Chat/HydraReportsPopover.swift:435-445
    public void StillAtWorkSentence_RunsFromTheLastEarlierPeriodToTheEndOfTheIntro()
    {
        headless.OnUiThread(() =>
        {
            var report = "First sentence. Juno is still at work. More intro text.\n\n" +
                         "## Juno: task\nReport.";
            var root = Attach(new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank")]));

            // StillAtWorkSentence (:435-445) ends with .Trimmed(), so the leading space after the period is gone.
            Assert.Contains("Juno is still at work. More intro text.", CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-205")] // App/Views/Chat/HydraReportsPopover.swift:318-389
    [Trait("Req", "UI-HYDRA-039")] // App/Views/Chat/HydraReportsPopover.swift:318-356
    public void SingleHeadHeader_ShowsTheOutcomeCaptionFilesAndNamePrefixFreeTask()
    {
        headless.OnUiThread(() =>
        {
            var root = Attach(new HydraReportsPopover(
                "Juno: Juno: streaming path",
                "## Juno: Juno: stream the parser (high)\n" +
                "Landed in your checkout: src/A.cs (+12 " + Minus + "3), src/B.cs (+1 " + Minus + "1)\nReport.",
                [Persona("Juno")]));

            var text = CombinedText(root);
            Assert.Contains("Juno", text, StringComparison.Ordinal);
            Assert.Contains("Finished", text, StringComparison.Ordinal);
            Assert.Contains("high", text, StringComparison.Ordinal);
            Assert.Contains("2 files " + MiddleDot + " +13 " + Minus + "4", text, StringComparison.Ordinal);
            Assert.Contains("stream the parser", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Juno: stream the parser", text, StringComparison.Ordinal);
            Assert.Contains(24, LogicalQuery.SelfAndDescendants<HydraGlyph>(root).Select(glyph => glyph.Size));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-207")] // App/Views/Chat/HydraReportsPopover.swift:393-412
    [Trait("Req", "PR-HYDRA-209")] // App/Views/Chat/HydraReportsPopover.swift:494-504
    [Trait("Req", "UI-HYDRA-041")] // App/Views/Chat/HydraReportsPopover.swift:494-504
    public void MultiHeadTaskLineRemovesExactlyOneColonNamePrefix()
    {
        headless.OnUiThread(() =>
        {
            var report =
                "## Juno: Juno: Juno: sweep the tests\nBody.\n\n" +
                "## Hank: other task\nNo report.";
            var root = Attach(new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank")]));

            // CleanedTitle (:393-405) keeps one trailing "Juno: " prefix via its lookahead loop, then
            // WithoutNamePrefix (:408-412) strips that last one too, so the rendered task line is fully clean.
            Assert.Contains("sweep the tests", CombinedText(root), StringComparison.Ordinal);
            Assert.DoesNotContain("Juno: sweep the tests", CombinedText(root), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-209")] // App/Views/Chat/HydraReportsPopover.swift:465-550
    [Trait("Req", "UI-HYDRA-040")] // App/Views/Chat/HydraReportsPopover.swift:465-550
    public void MultiHeadNameRowTogglesAReportAndAHeadWithoutAReportHasNoToggle()
    {
        headless.OnUiThread(() =>
        {
            var report =
                "## Juno: task (failed)\nJuno report body.\n\n" +
                "## Hank: other task\nNo report.";
            var root = new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank")]);
            var window = Present(root);
            try
            {
                Assert.DoesNotContain("Juno report body.", CombinedText(root), StringComparison.Ordinal);
                var junoToggle = Assert.IsAssignableFrom<Button>(ButtonContaining(root, "Juno"));
                Assert.Null(ButtonContaining(root, "Hank"));

                junoToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Juno report body.", CombinedText(root), StringComparison.Ordinal);
                junoToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.DoesNotContain("Juno report body.", CombinedText(root), StringComparison.Ordinal);

                Assert.Contains("Failed", CombinedText(root), StringComparison.Ordinal);
                Assert.Contains(20, LogicalQuery.SelfAndDescendants<HydraGlyph>(root).Select(glyph => glyph.Size));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-210")] // App/Views/Chat/HydraReportsPopover.swift:553-586, :591-626
    [Trait("Req", "UI-HYDRA-042")] // App/Views/Chat/HydraReportsPopover.swift:566-569
    [Trait("Req", "UI-HYDRA-043")] // App/Views/Chat/HydraReportsPopover.swift:558-565
    [Trait("Req", "UI-HYDRA-044")] // App/Views/Chat/HydraReportsPopover.swift:570-582
    public void LandingRowsShowTheEmptyFailedAndUnfinishedShapesAndTheirPaths()
    {
        headless.OnUiThread(() =>
        {
            var report =
                "## Juno: task\nChanged nothing.\nBody.\n\n" +
                "## Hank: task\nDid not land (patch rejected)\nNo report.\n\n" +
                "## Ada: task\nNothing landed in its copy at /tmp/head-copy.\nNo report.";
            var root = Attach(new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank"), Persona("Ada")]));

            var text = CombinedText(root);
            Assert.Contains("Changed nothing.", text, StringComparison.Ordinal);
            Assert.Contains("Did not land: patch rejected", text, StringComparison.Ordinal);
            Assert.Contains("Nothing landed", text, StringComparison.Ordinal);
            Assert.Contains("/tmp/head-copy", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-210")] // App/Views/Chat/HydraReportsPopover.swift:591-626
    public void FileRowsShowDiffCountsAndTheConflictTooltipPath()
    {
        headless.OnUiThread(() =>
        {
            var report =
                "## Juno: task\n" +
                "Landed in your checkout: src/clean.cs (+12 " + Minus + "3), src/conflict.cs (+1 " + Minus + "2) with conflicts\n" +
                "Report.\n\n" +
                "## Hank: task\nNo report.";
            var root = Attach(new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank")]));

            var text = CombinedText(root);
            Assert.Contains("src/clean.cs", text, StringComparison.Ordinal);
            Assert.Contains("+12", text, StringComparison.Ordinal);
            Assert.Contains(Minus + "3", text, StringComparison.Ordinal);
            Assert.Contains("src/conflict.cs", text, StringComparison.Ordinal);
            Assert.Contains("+1", text, StringComparison.Ordinal);
            Assert.Contains(Minus + "2", text, StringComparison.Ordinal);
            Assert.Contains("src/conflict.cs (with conflicts)",
                Controls(root).Select(ToolTip.GetTip).OfType<string>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-211")] // App/Views/Chat/HydraReportsPopover.swift:629-654
    public void ExpandedReportShowsNoReportOrItsMarkdownBody()
    {
        headless.OnUiThread(() =>
        {
            var report =
                "## Juno: task\n# Report heading\n\nReport paragraph.\n\n" +
                "## Hank: task\nNo report.";
            var root = new HydraReportsPopover(
                "Batch report", report, [Persona("Juno"), Persona("Hank")]);
            var window = Present(root);
            try
            {
                Assert.DoesNotContain("Report paragraph.", CombinedText(root), StringComparison.Ordinal);
                var toggle = Assert.IsAssignableFrom<Button>(ButtonContaining(root, "Juno"));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Contains("Report heading", CombinedText(root), StringComparison.Ordinal);
                Assert.Contains("Report paragraph.", CombinedText(root), StringComparison.Ordinal);

                // "No report." (:631-634) shows only where reportBody runs for a head without a report: a card that is
                // not collapsible (:506), the single-head popover's. With several heads such a head gets no toggle
                // (:468-492, PR-HYDRA-209's failure case), so its card never opens and never shows the line.
                Assert.Null(ButtonContaining(root, "Hank"));
                Assert.DoesNotContain("No report.", CombinedText(root), StringComparison.Ordinal);
                var single = Attach(new HydraReportsPopover("Hank task", "## Hank: task\nNo report.", [Persona("Hank")]));
                Assert.Contains("No report.", CombinedText(single), StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-212")] // App/Views/Chat/HydraReportsPopover.swift:449-461
    public void GlyphResolutionPrefersAnExactSuppliedPersonaThenFallsBackToTheRoster()
    {
        headless.OnUiThread(() =>
        {
            var suppliedJuno = Persona("Juno", 0x123456);
            var root = Attach(new HydraReportsPopover(
                "Juno task", "## Juno: task\nReport.", [suppliedJuno]));

            var singleGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(root)
                .First(glyph => glyph.Size == 24);
            Assert.Same(suppliedJuno, singleGlyph.Persona);
            Assert.Equal(HydraHeadInfo.Status.Completed, singleGlyph.Status);

            var roundReport = "## jUnO 2: task\nReport.";
            var roundRoot = Attach(new HydraReportsPopover(
                "Juno task", roundReport, [Persona("Walter")]));
            var roundGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(roundRoot)
                .First(glyph => glyph.Size == 24);
            Assert.Equal("Juno 2", roundGlyph.Persona.Name);
        });
    }
}

