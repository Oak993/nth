using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessageEditor.swift (B3 52bc792), :78-121 fileChanges, :125-154 fileList.
// Sizes follow ED-059 (callout 12, caption 10); Chrome.secondaryText is primaryText at 0.7 (ED-062).
[Collection("Headless")]
public sealed class MessageEditorFileChangesTests(HeadlessSession headless)
{
    private const string Checking = "Checking for file changes…"; // U+2026, :84
    private const string NoFiles = "This thread changed no files since this message"; // :91
    private const string KeptOne = "The file it changed has changed again since, so it stays as it is"; // :95
    private const string Undo = "Undo the file changes this thread made"; // :104
    private const string Notice = "Sending replaces this message and everything after it."; // :48

    private static string KeptMany(int count) => $"The {count} files it changed have changed again since, so they stay as they are"; // :96

    private static Color Colour(SwiftColor token, Control on) => token.Resolve(on.ActualThemeVariant);

    private static void AssertInk(IBrush? brush, SwiftColor token, Control on) =>
        Assert.Equal(Colour(token, on), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static TextBlock Text(MessageEditorRig rig, string text) =>
        Assert.Single(rig.All<TextBlock>(), block => block.Text == text);

    private static SwiftImage Glyph(MessageEditorRig rig, string symbol) =>
        Assert.Single(rig.All<SwiftImage>(), image => image.SymbolName == symbol);

    // The four state labels of :80-115 as the editor can show them; a state shows exactly its own.
    private static string[] StatesShown(MessageEditorRig rig) => rig.Texts().Where(text =>
        text == Checking || text == NoFiles || text.StartsWith("The file it changed", StringComparison.Ordinal)
        || (text.StartsWith("The ", StringComparison.Ordinal) && text.EndsWith("files it changed have changed again since, so they stay as they are", StringComparison.Ordinal))
        || text == Undo).ToArray();

    // The box of :117-120: the nearest background-carrying Border around a text of the state.
    private static Border Box(Control inside) => inside.GetLogicalAncestors().OfType<Border>().First(border => border.Background is not null);

    private static Point Origin(Control control, Visual relativeTo) =>
        control.TranslatePoint(default, relativeTo) ?? throw new Xunit.Sdk.XunitException("not in the same visual tree");

    // :81-86 checking: HStack(spacing: 8) { ProgressView().controlSize(.small); Text("Checking for file changes…") }
    // .foregroundStyle(Chrome.secondaryText), inside the callout box.
    [Fact]
    [Trait("Req", "UI-CHAT-066")] // MessageEditor.swift:81-86 the line is exactly "Checking for file changes…" with U+2026
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:81-86 checking shows a small spinner beside the line, and no other state
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:82-86 checking: a spinner beside the text, Chrome.secondaryText
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:81-86 checking: a small progress spinner beside "Checking for file changes…"
    public void While_the_preview_is_out_the_box_shows_a_spinner_beside_the_checking_line() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable, pumpOnShow: false);

        Assert.Equal([Checking], StatesShown(rig));
        var text = Text(rig, Checking);
        Assert.Equal("…", text.Text![^1..]);
        AssertInk(text.Foreground, Chrome.SecondaryText, text); // :86
        Assert.Equal(12, text.FontSize); // .font(.callout) on the box (:117), 12 at ED-059
        var spinner = Assert.Single(rig.All<ProgressBar>());
        Assert.True(spinner.IsIndeterminate);
        Assert.Same(spinner.Parent, text.Parent); // one HStack
        Assert.Equal(8, Origin(text, rig.Window).X - (Origin(spinner, rig.Window).X + spinner.Bounds.Width), 0.01); // HStack(spacing: 8)
        Assert.Equal(Origin(spinner, rig.Window).Y + spinner.Bounds.Height / 2, Origin(text, rig.Window).Y + text.Bounds.Height / 2, 1.0); // centred on one line
        Assert.Same(Box(text), Box(spinner));
    });

    // :90-92 no files: Label("This thread changed no files since this message", systemImage: "checkmark.circle")
    [Fact]
    [Trait("Req", "UI-CHAT-067")] // MessageEditor.swift:90-92 the line is exactly the string with no full stop, beside checkmark.circle
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:90-92 an empty preview shows the no-files line with a checkmark.circle
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:91-92 no files: checkmark.circle label in Chrome.secondaryText
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:90-92 no files: the sentence beside a checkmark.circle
    public void A_preview_without_files_shows_the_no_files_line_with_a_checkmark_circle() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles);
        rig.WaitForPreview();

        Assert.Equal([NoFiles], StatesShown(rig));
        var text = Text(rig, NoFiles);
        Assert.False(text.Text!.EndsWith('.')); // no trailing full stop
        AssertInk(text.Foreground, Chrome.SecondaryText, text); // :92
        Assert.Equal(12, text.FontSize);
        var glyph = Glyph(rig, "checkmark.circle");
        AssertInk(glyph.Foreground, Chrome.SecondaryText, glyph); // the label's icon takes the same style
        Assert.Same(glyph.Parent, text.Parent);
        Assert.True(Origin(glyph, rig.Window).X < Origin(text, rig.Window).X); // the glyph leads
        Assert.DoesNotContain(rig.All<SwiftImage>(), image => image.SymbolName is "hand.raised" or "doc" or "exclamationmark.triangle");
        Assert.Empty(rig.All<ProgressBar>());
        Assert.Same(Box(text), Box(glyph));
    });

    // :87-89 unavailable: Label(reason, systemImage: "exclamationmark.triangle").foregroundStyle(Chrome.secondaryText)
    [Fact]
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:87-89 a failed preview shows the error's description beside a warning triangle in secondaryText
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:88-89 unavailable: the reason beside exclamationmark.triangle, Chrome.secondaryText
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:87-89 unavailable: the failure reason beside an exclamationmark.triangle in Chrome.secondaryText
    public void A_failed_preview_shows_the_reason_beside_a_warning_triangle_in_secondary_text_and_still_allows_sending() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Unavailable);
        rig.WaitForPreview();

        // ThreadRuntime.swift:1805-1807: the turn has no file checkpoint, so the preview throws this ProviderError.
        const string reason = "No file checkpoint is available for this message. You can still keep the file changes.";
        var text = Text(rig, reason);
        AssertInk(text.Foreground, Chrome.SecondaryText, text);
        AssertInk(Glyph(rig, "exclamationmark.triangle").Foreground, Chrome.SecondaryText, text); // not Chrome.danger: the box line is the quiet one
        Assert.Empty(StatesShown(rig));
        Assert.Empty(rig.All<ProgressBar>());
        Assert.DoesNotContain(rig.Texts(), shown => shown == Undo || shown == NoFiles);
        Assert.True(rig.ButtonNamed("Send").IsEnabled); // :72-76 only checking blocks it, so a failed check still sends
    });

    // :93-99 nothing restorable: the one-file wording has no number, the plural carries the count; hand.raised, then the list.
    [Theory]
    [InlineData(1, KeptOne)]
    [InlineData(2, "The 2 files it changed have changed again since, so they stay as they are")]
    [InlineData(3, "The 3 files it changed have changed again since, so they stay as they are")]
    [Trait("Req", "UI-CHAT-068")] // MessageEditor.swift:93-99 singular without a number, plural with the count, neither with a full stop
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:93-99 nothing restorable shows the changed-again wording with a hand.raised, then the file list
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:95-99 all kept: the hand.raised label in secondaryText, then the file list
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:93-99 nothing restorable: the changed-again sentence beside a hand.raised, plus the file list
    public void A_preview_where_every_file_changed_again_shows_the_kept_wording_above_the_file_list(int files, string expected) => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.AllKept, files);
        rig.WaitForPreview();

        Assert.Equal([expected], StatesShown(rig));
        var text = Text(rig, expected);
        Assert.False(text.Text!.EndsWith('.'));
        if (files == 1) Assert.DoesNotContain(text.Text, char.IsDigit); // the singular form has no number
        AssertInk(text.Foreground, Chrome.SecondaryText, text); // :98
        Assert.Equal(12, text.FontSize);
        var banner = Assert.Single(rig.All<SwiftImage>(), image => image.SymbolName == "hand.raised" && Origin(image, rig.Window).Y < Origin(text, rig.Window).Y + text.Bounds.Height);
        AssertInk(banner.Foreground, Chrome.SecondaryText, banner);
        Assert.Same(banner.Parent, text.Parent);
        // the file list follows the wording, one row per file, all of them kept.
        var paths = rig.All<TextBlock>().Where(block => rig.Files.Contains(block.Text)).ToArray();
        Assert.Equal(files, paths.Length);
        Assert.All(paths, path => Assert.True(Origin(path, rig.Window).Y > Origin(text, rig.Window).Y + text.Bounds.Height));
        Assert.Equal(files, rig.Texts().Count(shown => shown == "Kept"));
        Assert.DoesNotContain(rig.Texts(), shown => shown == Undo || shown == Checking || shown == NoFiles);
        Assert.Empty(rig.All<ProgressBar>());
    });

    // :100-114 otherwise: the switch toggle, then the file list; no other state line.
    [Fact]
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:100-114 otherwise a switch toggle (and the list), none of the other states
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:100-114 restorable files: the undo toggle, then the file list
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:100-114 restorable: the undo toggle plus the file list
    public void A_preview_with_restorable_files_shows_the_undo_toggle_above_the_file_list() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable, 2);
        rig.WaitForPreview();

        Assert.Equal([Undo], StatesShown(rig));
        Assert.Empty(rig.All<ProgressBar>());
        Assert.DoesNotContain(rig.All<SwiftImage>(), image => image.SymbolName is "hand.raised" or "checkmark.circle" or "exclamationmark.triangle");
        var title = Text(rig, Undo);
        var paths = rig.All<TextBlock>().Where(block => rig.Files.Contains(block.Text)).ToArray();
        Assert.Equal(2, paths.Length);
        Assert.All(paths, path => Assert.True(Origin(path, rig.Window).Y > Origin(title, rig.Window).Y + title.Bounds.Height));
    });

    // :117-120 .font(.callout).padding(12).frame(maxWidth: .infinity, alignment: .leading)
    //          .background(Chrome.overlay(0.05), in: .rect(cornerRadius: 12, style: .continuous))
    [Fact]
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:117-120 padding 12, full width leading, overlay 0.05 in a 12-point continuous rounded rect
    [Trait("Req", "PR-CHAT-083")] // MessageEditor.swift:117-120 the box: 12 points of padding, full width, Chrome.overlay(0.05) in a 12-point continuous rect
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:117-120 the file-changes box is padded 12 with corner radius 12 continuous, Chrome.overlay(0.05)
    public void The_box_is_padded_12_full_width_and_filled_with_the_overlay_in_a_12_point_continuous_rect() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.AllKept);
        rig.WaitForPreview();

        var box = Box(Text(rig, KeptOne));
        Assert.Equal(new Thickness(12), box.Padding);
        Assert.Equal(new CornerRadius(12), box.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(box));
        AssertInk(box.Background, Chrome.Overlay(0.05), box);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.05).Resolve(box.ActualThemeVariant), Colour(Chrome.Overlay(0.05), box));
        // frame(maxWidth: .infinity, alignment: .leading): as wide as the editor's content column, starting at its leading edge.
        var column = Assert.IsAssignableFrom<Control>(box.Parent);
        Assert.Equal(column.Bounds.Width, box.Bounds.Width, 0.01);
        Assert.Equal(0, box.Bounds.Left, 0.01);
        // padding(12) on every side of its content.
        var content = Assert.IsAssignableFrom<Control>(box.Child);
        Assert.Equal(12, content.Bounds.Left, 0.01);
        Assert.Equal(12, content.Bounds.Top, 0.01);
        Assert.Equal(box.Bounds.Width - 12, content.Bounds.Right, 0.01);
        Assert.Equal(box.Bounds.Height - 12, content.Bounds.Bottom, 0.01);
    });

    // :79 VStack(alignment: .leading, spacing: 10): the state's views are 10 points apart and lead-aligned.
    [Theory]
    [InlineData(EditorPreviewKind.AllKept)] // the hand.raised label, then fileList
    [InlineData(EditorPreviewKind.Restorable)] // the toggle, then fileList
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:79 VStack(alignment: .leading, spacing: 10)
    public void The_views_of_the_box_stack_10_points_apart_from_the_leading_edge(EditorPreviewKind kind) => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(kind);
        rig.WaitForPreview();

        var box = Box(Text(rig, kind == EditorPreviewKind.AllKept ? KeptOne : Undo));
        var stack = Assert.IsType<StackPanel>(box.Child);
        Assert.Equal(2, stack.Children.Count);
        var (first, second) = ((Control)stack.Children[0], (Control)stack.Children[1]);
        Assert.Equal(10, second.Bounds.Top - first.Bounds.Bottom, 0.01);
        Assert.Equal(0, first.Bounds.Left, 0.01);
        Assert.Equal(0, second.Bounds.Left, 0.01);
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, stack.Orientation);
    });

    // :143 .font(.caption) on the file rows; :84 the box's callout is not inherited by them.
    [Fact]
    [Trait("Req", "UI-GIT-053")] // MessageEditor.swift:117, :143 callout throughout the box, caption on the file rows
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:49, :117 callout throughout the extra content
    public void The_box_text_is_callout_12_and_the_file_rows_caption_10() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.FirstKept, 2);
        rig.WaitForPreview();

        foreach (var callout in new[] { Undo, "1 file", Notice })
            Assert.Equal(12, Text(rig, callout).FontSize);
        foreach (var row in new[] { rig.Files[0], rig.Files[1], "Kept" })
            Assert.Equal(10, Text(rig, row).FontSize);
        Assert.All(rig.All<TextBlock>().Where(block => block.Text == Undo || block.Text == Notice),
            block => Assert.Equal("Inter", block.FontFamily.Name));
    });

    // :149-153 more than five files scroll in a 110-point strip; up to five sit in the box.
    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:149-153 the file list becomes a 110-point scroller past five files
    public void The_file_list_scrolls_in_a_110_point_strip_only_past_five_files(int files, bool scrolls) => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable, files);
        rig.WaitForPreview();

        // the editor's text field has a scroller of its own; the file list's holds the paths.
        var strips = rig.All<ScrollViewer>().Where(viewer => rig.All<TextBlock>().Any(block => rig.Files.Contains(block.Text) && viewer.IsLogicalAncestorOf(block))).ToArray();
        if (!scrolls)
        {
            Assert.Empty(strips);
            return;
        }
        var strip = Assert.Single(strips);
        Assert.Equal(110, strip.Height);
        Assert.Equal(110, strip.Bounds.Height, 0.01);
        Assert.Same(Box(strip), Box(Text(rig, Undo)));
        Assert.Equal(files, rig.All<TextBlock>().Count(block => rig.Files.Contains(block.Text) && strip.IsLogicalAncestorOf(block)));
    });

    // :48-50 the explanatory line, exactly the sentence, callout in Chrome.secondaryText, after the box.
    [Fact]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:48-50 "Sending replaces this message and everything after it." in Chrome.secondaryText
    public void The_explanatory_line_is_the_exact_sentence_in_secondary_text_below_the_box() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles);
        rig.WaitForPreview();

        var notice = Text(rig, Notice);
        AssertInk(notice.Foreground, Chrome.SecondaryText, notice);
        Assert.Equal(12, notice.FontSize);
        Assert.True(Origin(notice, rig.Window).Y >= Origin(Box(Text(rig, NoFiles)), rig.Window).Y + Box(Text(rig, NoFiles)).Bounds.Height);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(notice.ActualThemeVariant), Colour(Chrome.SecondaryText, notice)); // ED-062
    });
}
