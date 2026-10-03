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

// REVIEW (defects-priority, adversarial round 3). Source: DroppyCode/App/Views/Settings/SettingsView.swift:1030-1033 (each document is
// `MarkdownView(text:)... .textSelection(.enabled)`); MarkdownView.swift:1130-1131, :1227-1229 (InlineText and CodeBlock wear MarkdownBlockSelection,
// which applies `.textSelection(.enabled)` while \.markdownBlockSelection is true, :855-861, its default and only value, :822-824). UI-UPDATE-032: "Text is selectable
// (.textSelection(.enabled))", interaction "Select and copy text"; UI-SETTINGS-060: interaction "Select and copy the text".
//
// The unit reports both records as proven. The tests that carry them assert only MarkdownView.GetMarkdownBlockSelection(body), which is true by
// default and has no writer anywhere (MarkdownBlockSelection.Body is the identity), so they pass whatever the popover can select. The shipped
// THIRD_PARTY_NOTICES.md carries fenced code blocks (the MIT texts of border-beam and the Uiverse button), which MarkdownView draws through CodeBlock
// with a VeiledText, a plain TextBlock that cannot select. On the Mac those lines are selectable like the rest of the popover. Measured with real
// input on the real popover: a drag over a prose line then Control+C copies what it covered (the premise), the same drag over a code block's line
// copies nothing of it.
[Collection("Headless")]
public sealed class LicensesSelectionReviewTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    // A drag across the line at y of the target, from 2 points in, then Control+C: what the clipboard holds afterwards. The target is scrolled into
    // view and the scroll left to settle first, and the press is checked to land on the target, so a miss can never pass for "nothing to select".
    private static string Copied(Avalonia.Controls.Window window, AppModelHarness h, Control target, double y)
    {
        window.Clipboard!.SetTextAsync("<nothing copied>").GetAwaiter().GetResult();
        for (var pass = 0; pass < 3; pass++)
        {
            target.BringIntoView();
            Drain(window);
            Tick(h, 500);
            Drain(window);
        }
        var from = target.TranslatePoint(new Point(2, y), window)!.Value;
        var to = target.TranslatePoint(new Point(Math.Min(120, target.Bounds.Width - 2), y), window)!.Value;
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

    [Fact]
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1033 .textSelection(.enabled): "Text is selectable", "Select and copy text"
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1033: "Select and copy the text"
    public void Every_line_of_the_licenses_popover_can_be_selected_and_copied_the_code_blocks_of_the_notices_too() => With(() => new LicensesRow(), (view, _, h, window) =>
    {
        Click(window, Nodes<Button>(view).Single(button => Text(button).Contains("Show")));
        Tick(h, 1000);
        Drain(window);
        var popup = Assert.Single(Nodes<Popup>(view));
        Assert.True(popup.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        var notices = Nodes<MarkdownView>(content)[1]; // Marrow, Third-party notices, Trademarks

        // The premise: a drag over a line of the notices' prose, then Control+C, copies what the drag covered.
        var prose = notices.GetVisualDescendants().OfType<ParagraphTextPresenter>().First();
        var copiedProse = Copied(window, h, prose, 8);
        Assert.False(string.IsNullOrEmpty(copiedProse) || copiedProse == "<nothing copied>", $"the premise: a drag over the prose copies it (got '{copiedProse}')");
        Assert.Contains(copiedProse, prose.Text.String, StringComparison.Ordinal);

        // A code block of the same document: its first line ("MIT License") under the same drag.
        var code = Assert.IsAssignableFrom<Control>(Nodes<CodeBlock>(notices).First());
        var codeText = code.GetVisualDescendants().OfType<TextBlock>().First(text => (text.Inlines?.Text ?? text.Text ?? "").StartsWith("MIT License", StringComparison.Ordinal));
        var codeString = codeText.Inlines?.Text ?? codeText.Text ?? "";
        var copiedCode = Copied(window, h, codeText, 8);
        Assert.True(copiedCode.Length > 0 && copiedCode != "<nothing copied>" && codeString.Contains(copiedCode, StringComparison.Ordinal),
            $"a drag over the code block's first line then Control+C copied '{copiedCode}', not part of '{codeString[..Math.Min(40, codeString.Length)]}': the code lines of the licences popover cannot be selected (SettingsView.swift:1033 .textSelection(.enabled))");
    }, nativePopup: true);
}
