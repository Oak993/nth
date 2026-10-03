using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift:1030-1033 (each document is `MarkdownView(text:)... .textSelection(.enabled)`); MarkdownView.swift:1130-1131,
// :1227-1229 (InlineText and CodeBlock wear MarkdownBlockSelection, which applies `.textSelection(.enabled)` while \.markdownBlockSelection is true, :855-861, its
// default and only value, :822-824). UI-UPDATE-032: "Text is selectable (.textSelection(.enabled))", interaction "Select and copy text"; UI-SETTINGS-060: interaction
// "Select and copy the text".
//
// What this proves: on the real Licenses row with its popover on the overlay popup host, every block of the three shipped documents (every prose run and every code
// block of LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md, the fenced MIT texts of the notices included, and each fenced block's one word caption "code", which
// SwiftUI's `.textSelection(.enabled)` environment reaches as it reaches every Text below it) can be selected by a real drag and copied by a real Control+C, and what
// the clipboard holds is that block's own selection. Not proven here: the native popup window of the running app (a native check). That the caption is NOT selectable
// where no ancestor enables selection (a chat message) is VeiledTextSelectionTests.
//
// History: the tests that carried these records asserted only MarkdownView.GetMarkdownBlockSelection(body), which is true by default and has no writer, so they passed
// whatever the popover could select; a drag over a code block's first line then Control+C copied nothing of it (the clipboard kept the prose line selected before),
// because VeiledText drew a plain TextBlock. This test failed on that code and passes with VeiledText's SelectableTextBlock.
[Collection("Headless")]
public sealed class LicensesSelectionTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private const string Sentinel = "<nothing copied>";

    // Where a press goes: a prose run's first character that is not part of a link (a press on a link opens it instead of selecting), 1 point inside its own line box
    // (a heading's run carries space above its first line); a code block's text 2 points in on its first line.
    private static Point Start(Control target)
    {
        if (target is not ParagraphTextPresenter presenter) return new Point(2, Math.Min(8, target.Bounds.Height / 2));
        var links = presenter.GetVisualAncestors().OfType<LinkTextView>().First();
        for (var index = 0; index < presenter.Text.Length; index++)
        {
            if (presenter.RangeRects(index, 1).FirstOrDefault() is not { Width: > 1, Height: > 0 } rect) continue;
            var point = new Point(rect.Left + 1, rect.Center.Y);
            if (!links.HasLink(point)) return point;
        }
        throw new Xunit.Sdk.XunitException("a prose run with nothing in it but links");
    }

    // The target's first line is scrolled into view and the scroll left to settle, then a press (see Start), a drag 118 points on (or to the edge), a release and
    // Control+C. The press is checked to land on the target, so a miss can never pass for "nothing to select". Returns what the clipboard holds afterwards.
    private static string Copied(Avalonia.Controls.Window window, AppModelHarness h, Control target)
    {
        window.Clipboard!.SetTextAsync(Sentinel).GetAwaiter().GetResult();
        for (var pass = 0; pass < 3; pass++)
        {
            target.BringIntoView(new Rect(0, 0, target.Bounds.Width, Math.Min(24, target.Bounds.Height)));
            Drain(window);
            Tick(h, 500);
            Drain(window);
        }
        var start = Start(target);
        var from = target.TranslatePoint(start, window)!.Value;
        var to = target.TranslatePoint(new Point(Math.Min(start.X + 118, target.Bounds.Width - 2), start.Y), window)!.Value;
        var hit = window.InputHitTest(from) as Visual;
        Assert.True(hit is not null && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target)),
            $"the press lands on the text under test (it lands on {hit?.GetType().Name ?? "nothing"})");
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Drain(window);
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        Drain(window);
        return window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() ?? "";
    }

    private static string Shown(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : block.Text ?? "";

    // The code lines of a fenced block: the monospaced text under its scroll viewer (the header's caption and the copy button are not it).
    private static TextBlock CodeText(CodeBlock block) =>
        block.GetVisualDescendants().OfType<ScrollViewer>().SelectMany(viewer => viewer.GetVisualDescendants().OfType<TextBlock>()).First(text => Shown(text).Length > 0);

    [Fact]
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1033 .textSelection(.enabled): "Text is selectable", "Select and copy text"
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1033: "Select and copy the text"
    public void Every_prose_run_and_every_code_block_of_the_three_documents_can_be_selected_by_a_drag_and_copied_with_Control_C() => With(() => new LicensesRow(), (view, _, h, window) =>
    {
        // Nothing here may reach the shell: a press on a link opens it (ShellExecute), and these presses are placed off the links, which the test asserts.
        var opened = new List<string>();
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = opened.Add;
        try
        {
            Click(window, Nodes<Button>(view).Single(button => Text(button).Contains("Show")));
            Tick(h, 1000);
            Drain(window);
            var popup = Assert.Single(Nodes<Popup>(view));
            Assert.True(popup.IsOpen);
            var content = Assert.IsAssignableFrom<Control>(popup.Child);
            var documents = Nodes<MarkdownView>(content); // LICENSE, THIRD_PARTY_NOTICES.md, TRADEMARK.md
            Assert.Equal(3, documents.Length);

            var proseRuns = 0;
            var codeBlocks = 0;
            foreach (var (document, name) in documents.Zip(new[] { "LICENSE", "THIRD_PARTY_NOTICES.md", "TRADEMARK.md" }))
            {
                // Every block of the document, in the order it is drawn: a prose run (headings, paragraphs, lists, quotes) or a fenced code block.
                var blocks = document.GetVisualDescendants().Where(visual => visual is ParagraphTextPresenter or CodeBlock).Cast<Control>().ToArray();
                Assert.NotEmpty(blocks);
                foreach (var block in blocks)
                {
                    if (block is ParagraphTextPresenter presenter)
                    {
                        proseRuns++;
                        var copied = Copied(window, h, presenter);
                        Assert.True(copied.Length > 0 && copied != Sentinel, $"{name}: a drag over a prose run then Control+C copied nothing ('{copied}')");
                        Assert.Contains(copied, presenter.Text.String, StringComparison.Ordinal);
                        Assert.Equal(presenter.SelectedText, copied); // the clipboard holds this block's own selection, not an earlier block's
                    }
                    else
                    {
                        codeBlocks++;
                        var code = CodeText((CodeBlock)block);
                        var text = Shown(code);
                        var selectable = Assert.IsAssignableFrom<SelectableTextBlock>(code);
                        var copied = Copied(window, h, code);
                        Assert.True(copied.Length > 0 && copied != Sentinel, $"{name}: a drag over the code block starting '{text[..Math.Min(20, text.Length)]}' then Control+C copied nothing of it ('{copied}')");
                        Assert.Contains(copied, text, StringComparison.Ordinal);
                        Assert.Equal(selectable.SelectedText, copied);

                        // The block's caption, `Text(language ?? "code")` (MarkdownView.swift:1286-1289): the popover's `.textSelection(.enabled)` reaches it, as it reaches every Text below.
                        var caption = Assert.IsAssignableFrom<SelectableTextBlock>(((CodeBlock)block).GetVisualDescendants().OfType<TextBlock>().First(label => Shown(label) == "code"));
                        var copiedCaption = Copied(window, h, caption);
                        Assert.True(copiedCaption.Length > 0 && copiedCaption != Sentinel, $"{name}: a drag over the code block's caption then Control+C copied nothing of it ('{copiedCaption}')");
                        Assert.Contains(copiedCaption, "code", StringComparison.Ordinal);
                        Assert.Equal(caption.SelectedText, copiedCaption);
                    }
                }
            }

            Assert.True(proseRuns >= 3, $"a prose run in each document (found {proseRuns})");
            Assert.True(codeBlocks >= 2, $"the notices' fenced MIT texts (found {codeBlocks} code blocks)");
            Assert.Empty(opened); // no press opened a link
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }, nativePopup: true);
}
