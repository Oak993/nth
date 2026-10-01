using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraMergePopover.swift (B2 1fbf81e), :162-214, :230-285.
[Collection("Headless")]
public sealed class HydraMergePopoverTests(HeadlessSession headless)
{
    private const string FilesLine =
        "2 files (+3 \U00002212" + "1) landed on `main` from `hydra/fix-the-parser-a1b2c3`."; // U+2212 MINUS SIGN

    private const string Remark = "Checkout of `main` was left as it was.";
    private const string UpToDate = "Everything is up to date.";

    private const string Body =
        FilesLine + "\n" +
        Remark + "\n" +
        UpToDate + "\n" +
        "Heads\n" +
        "- Hank (1): Fix the parser \U00002014 2 files: Sources/Parser/Lexer.swift, Sources/Parser/Node.swift"; // U+2014 EM DASH

    private static SwiftURL Url(string value) =>
        SwiftURL.Create(value) ?? throw new Xunit.Sdk.XunitException("bad fixture URL " + value);

    private static MergeRequestLink Link(string value) =>
        MergeRequestLink.Create(Url(value)) ?? throw new Xunit.Sdk.XunitException("bad merge request URL " + value);

    private static HydraMergeNote Note(string url = "https://gitlab.com/acme/app/-/merge_requests/7") =>
        HydraMergeNote.Parse(Body, Link(url)) ?? throw new Xunit.Sdk.XunitException("fixture body must parse");

    private HydraMergePopover Build(HydraMergeNote note)
    {
        HydraMergePopover? result = null;
        headless.OnUiThread(() => result = new HydraMergePopover(note));
        return result!;
    }

    private static T[] Descendants<T>(ILogical root) where T : class =>
        LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    private static TextBlock? Text(ILogical root, string value) =>
        Descendants<TextBlock>(root).FirstOrDefault(block => block.Text == value);

    private static SwiftImage Symbol(ILogical root, string name) =>
        Descendants<SwiftImage>(root).First(image => image.SymbolName == name);

    private static void AssertForeground(Control control, SwiftColor token)
    {
        var brush = Assert.IsType<SolidColorBrush>(control switch
        {
            TextBlock text => text.Foreground,
            SwiftImage image => image.Foreground,
            _ => null,
        });
        Assert.Equal(token.Resolve(ThemeVariant.Light), brush.Color);
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-023")] // App/Views/Chat/HydraMergePopover.swift:244-252
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Head_file_paths_render_in_collection_order_with_six_point_spacing_and_single_line_trimming()
    {
        var popover = Build(Note());
        headless.OnUiThread(() =>
        {
            var lexer = Assert.IsType<TextBlock>(Text(popover, "Sources/Parser/Lexer.swift"));
            var node = Assert.IsType<TextBlock>(Text(popover, "Sources/Parser/Node.swift"));
            var textBlocks = Descendants<TextBlock>(popover);

            Assert.True(Array.IndexOf(textBlocks, lexer) < Array.IndexOf(textBlocks, node));

            foreach (var path in new[] { lexer, node })
            {
                Assert.Equal(11, path.FontSize);
                Assert.Contains("Cascadia Mono", path.FontFamily.Name, StringComparison.Ordinal);
                AssertForeground(path, Chrome.SecondaryText);
                Assert.Equal(1, path.MaxLines);
                Assert.NotEqual(TextTrimming.None, path.TextTrimming);
                Assert.Null(ToolTip.GetTip(path));
                Assert.IsType<StackPanel>(path.Parent);
                Assert.Equal(6, Assert.IsType<StackPanel>(path.Parent).Spacing);

                for (ILogical? ancestor = path; ancestor is not null; ancestor = ancestor.LogicalParent)
                    Assert.IsNotType<Button>(ancestor);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-024")] // App/Views/Chat/HydraMergePopover.swift:162-164, :275-285
    [Trait("Decision", "ED-059")]
    public void Source_chip_fills_the_row_and_shows_the_branch_with_a_full_name_tooltip()
    {
        var popover = Build(Note());
        headless.OnUiThread(() =>
        {
            var branch = Assert.IsType<TextBlock>(Text(popover, "hydra/fix-the-parser-a1b2c3"));
            var chip = Assert.IsType<Border>(branch.Parent);

            Assert.Equal(11, branch.FontSize);
            Assert.Contains("Cascadia Mono", branch.FontFamily.Name, StringComparison.Ordinal);
            AssertForeground(branch, Chrome.PrimaryText.Opacity(0.9));
            Assert.Equal(1, branch.MaxLines);
            Assert.NotEqual(TextTrimming.None, branch.TextTrimming);
            Assert.Equal("hydra/fix-the-parser-a1b2c3", ToolTip.GetTip(chip));
            Assert.Equal(HorizontalAlignment.Stretch, chip.HorizontalAlignment);
            Assert.Equal(new Thickness(8, 4), chip.Padding);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-025")] // App/Views/Chat/HydraMergePopover.swift:165-167
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Branch_arrow_uses_the_right_arrow_symbol_in_semibold_caption2_secondary_text()
    {
        var popover = Build(Note());
        headless.OnUiThread(() =>
        {
            var arrow = Symbol(popover, "arrow.right");
            var font = Assert.IsType<SwiftFont>(SwiftImage.GetFont(arrow));

            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Semibold), font);
            Assert.Equal(10, font.PointSize);
            AssertForeground(arrow, Chrome.SecondaryText);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-026")] // App/Views/Chat/HydraMergePopover.swift:168-169, :275-285
    [Trait("Decision", "ED-059")]
    public void Target_chip_hugs_its_text_without_a_tooltip_or_truncation()
    {
        var popover = Build(Note());
        headless.OnUiThread(() =>
        {
            var target = Assert.IsType<TextBlock>(Text(popover, "main"));
            var chip = Assert.IsType<Border>(target.Parent);

            Assert.Equal(11, target.FontSize);
            Assert.Contains("Cascadia Mono", target.FontFamily.Name, StringComparison.Ordinal);
            AssertForeground(target, Chrome.PrimaryText.Opacity(0.9));
            Assert.Equal(1, target.MaxLines);
            Assert.Equal(TextTrimming.None, target.TextTrimming);
            Assert.Null(ToolTip.GetTip(chip));
            Assert.Equal(HorizontalAlignment.Left, chip.HorizontalAlignment);
            Assert.Equal(new Thickness(8, 4), chip.Padding);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-027")] // App/Views/Chat/HydraMergePopover.swift:171-181
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Remark_row_uses_the_info_symbol_and_callout_secondary_text()
    {
        var popover = Build(Note());
        headless.OnUiThread(() =>
        {
            var icon = Symbol(popover, "info.circle");
            var iconFont = Assert.IsType<SwiftFont>(SwiftImage.GetFont(icon));
            var remark = Assert.IsType<TextBlock>(Text(popover, "Checkout of main was left as it was."));

            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout), iconFont);
            Assert.Equal(12, iconFont.PointSize);
            AssertForeground(icon, Chrome.SecondaryText);
            Assert.Equal(12, remark.FontSize);
            AssertForeground(remark, Chrome.SecondaryText);
            Assert.Equal(TextWrapping.Wrap, remark.TextWrapping);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-027")] // App/Views/Chat/HydraMergePopover.swift:66-69
    public void Remark_parser_strips_backticks_and_drops_the_up_to_date_line()
    {
        var note = Note();

        Assert.Equal(new[] { "Checkout of main was left as it was." }, note.Notes);
        Assert.DoesNotContain("`", string.Concat(note.Notes), StringComparison.Ordinal);

        var popover = Build(note);
        headless.OnUiThread(() =>
        {
            Assert.NotNull(Text(popover, "Checkout of main was left as it was."));
            Assert.Null(Text(popover, UpToDate));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-027")] // App/Views/Chat/HydraMergePopover.swift:171-181
    public void No_remarks_produce_no_remark_rows()
    {
        var body = FilesLine + "\n" + UpToDate;
        var note = HydraMergeNote.Parse(body, Link("https://gitlab.com/acme/app/-/merge_requests/7"));
        Assert.NotNull(note);
        Assert.Empty(note!.Notes);

        var popover = Build(note);
        headless.OnUiThread(() => Assert.DoesNotContain(Descendants<SwiftImage>(popover), image => image.SymbolName == "info.circle"));
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-028")] // App/Views/Chat/HydraMergePopover.swift:182-207
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    // TestRuntime is constructed inside the single OnUiThread dispatch, not around it (matches
    // ComposerWorkingDotsTests's established pattern): Ambient<T>'s override is AsyncLocal-scoped, and
    // HeadlessSession.OnUiThread's dispatch flows the calling context, so a runtime built on the outer test
    // thread before Build()/OnUiThread leaks its Loop into OnUiThread's own MainActor.Bind(Avalonia...) call
    // and throws "MainActor is already bound to another executor".
    public void Copy_link_button_shows_copied_with_a_success_checkmark_for_one_point_four_seconds() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the label morphs (numericText, :202-203)
        var popover = new HydraMergePopover(Note());

        var button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);
        Assert.NotNull(Text(button, "Copy link"));
        Assert.Null(Descendants<SwiftImage>(button).FirstOrDefault(image => image.SymbolName == "checkmark"));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        runtime.RunUntilIdle();

        button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copied") is not null);
        Assert.Null(Text(button, "Copy link"));
        Assert.NotNull(Text(button, "Copied"));

        var checkmark = Symbol(button, "checkmark");
        var font = Assert.IsType<SwiftFont>(SwiftImage.GetFont(checkmark));
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), font);
        Assert.Equal(11, font.PointSize);
        AssertForeground(checkmark, Chrome.Success);

        runtime.AdvanceMilliseconds(1399);
        Assert.NotNull(Text(popover, "Copied"));
        runtime.AdvanceMilliseconds(1);
        Assert.NotNull(Text(popover, "Copy link"));
        Assert.Null(Text(popover, "Copied"));
        Assert.DoesNotContain(Descendants<SwiftImage>(popover), image => image.SymbolName == "checkmark");
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-028")] // App/Views/Chat/HydraMergePopover.swift:186-194
    // See the comment on Copy_link_button_shows_copied... for why TestRuntime is built inside the dispatch.
    public void A_second_copy_click_while_copied_does_not_restart_the_reset() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the label morphs (numericText, :202-203)
        var popover = new HydraMergePopover(Note());

        var button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        runtime.RunUntilIdle();

        runtime.AdvanceMilliseconds(700);
        button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copied") is not null);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        runtime.AdvanceMilliseconds(699);
        Assert.NotNull(Text(popover, "Copied"));
        runtime.AdvanceMilliseconds(1);
        Assert.NotNull(Text(popover, "Copy link"));
        Assert.Null(Text(popover, "Copied"));
    });

    [Theory]
    [Trait("Req", "UI-HYDRAMERGE-029")] // App/Views/Chat/HydraMergePopover.swift:90-96, :208-214
    [InlineData("https://gitlab.com/acme/app/-/merge_requests/7", "Open merge request")]
    [InlineData("https://github.com/acme/app/pull/7", "Open pull request")]
    [InlineData("https://gitea.example/acme/app/pulls/7", "Open pull request")]
    public void Open_button_title_follows_the_forge_and_is_the_default_action(string url, string expectedTitle)
    {
        var popover = Build(Note(url));
        headless.OnUiThread(() =>
        {
            var button = Descendants<Button>(popover).First(candidate => Text(candidate, expectedTitle) is not null);

            Assert.True(button.IsEnabled);
            Assert.True(button.IsDefault);
            Assert.NotNull(Text(button, expectedTitle));
            Assert.Contains(Descendants<SwiftImage>(button), image => image.SymbolName == "arrow.up.right");
        });
    }
}
