using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

[Collection("Headless")]
public sealed class LinkParagraphViewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-142")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:106-133
    public void Inline_code_uses_the_monospace_face_one_point_smaller() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("`code`", pointSize: 13);
        var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
        var found = false;

        foreach (var inline in block.Inlines!)
        {
            if (inline is Run run && run.Text == "code")
            {
                Assert.Equal(12, run.FontSize);
                found = true;
            }
        }

        Assert.True(found);
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:90-100
    public void A_valid_url_link_value_is_handled()
    {
        var coordinator = new LinkParagraphView.Coordinator();

        Assert.True(coordinator.TextView(new LinkTextView(), SwiftURL.Create("https://example.com")!, 0));
    }

    [Fact]
    [Trait("Req", "PR-THEME-152")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:498-507
    public void Repeated_measurements_at_the_same_width_return_the_cached_height() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("A paragraph with text.");
        var view = (LinkTextView)paragraph.Child!;

        var first = view.HeightForWidth(120);
        var second = view.HeightForWidth(120);

        Assert.True(first > 0);
        Assert.Equal(first, second);
    });

    [Fact]
    [Trait("Req", "PR-THEME-180")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:121, 152
    public void Dimmed_prose_uses_label_color_with_reduced_alpha() => headless.OnUiThread(() =>
    {
        var normalParagraph = new LinkParagraphView("prose");
        var dimmedParagraph = new LinkParagraphView("prose", dimmed: true);
        var normalBlock = (SelectableTextBlock)((LinkTextView)normalParagraph.Child!).Child!;
        var dimmedBlock = (SelectableTextBlock)((LinkTextView)dimmedParagraph.Child!).Child!;
        Run? normalRun = null;
        Run? dimmedRun = null;

        foreach (var inline in normalBlock.Inlines!)
            if (inline is Run run) { normalRun = run; break; }

        foreach (var inline in dimmedBlock.Inlines!)
            if (inline is Run run) { dimmedRun = run; break; }

        var normalColor = Assert.IsType<SolidColorBrush>(normalRun!.Foreground).Color;
        var dimmedColor = Assert.IsType<SolidColorBrush>(dimmedRun!.Foreground).Color;
        Assert.InRange(
            System.Math.Abs((int)dimmedColor.A - (int)System.Math.Round(normalColor.A * 0.7)),
            0,
            1);
    });

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:247-249
    public void Hydra_head_attribute_key_keeps_its_exact_name()
    {
        Assert.Equal("droppycode.hydraHead", LinkParagraphView.HydraHead);
    }

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:90-100
    public void A_link_value_that_is_not_a_url_or_string_is_not_handled()
    {
        var coordinator = new LinkParagraphView.Coordinator();

        Assert.False(coordinator.TextView(new LinkTextView(), new object(), 0));
    }

    [Fact]
    [Trait("Req", "PR-THEME-152")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:498-500
    public void A_nonpositive_width_measures_zero_height()
    {
        var view = new LinkTextView();

        Assert.Equal(0, view.HeightForWidth(0));
        Assert.Equal(0, view.HeightForWidth(-1));
    }
}
