using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class ComposerParagraphLayoutTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("123 اردو", true, false)]
    [InlineData("123 العربية", true, false)]
    [InlineData("123 שלום", true, false)]
    [InlineData("123 English اردو", false, false)]
    [InlineData("\u064b123 English", false, false)]
    [InlineData("\u05b0123 English", false, false)]
    [InlineData("\u061c123", true, false)]
    [InlineData("\U0001e900123", true, false)]
    [InlineData("\U00010400123", false, true)]
    [InlineData("\u2067اردو\u2069 English", false, false)]
    [InlineData("\u2068English\u2069 اردو", true, false)]
    [InlineData("\u2068\u2067اردو\u2069English\u2069 עברית", true, false)]
    [InlineData("\u2067اردو", false, false)]
    [InlineData("\u2067اردو\u2069", true, true)]
    [InlineData("(123)!", false, false)]
    [InlineData("(123)!", true, true)]
    [InlineData("", false, false)]
    public void First_strong_base_uses_unicode_classes_and_skips_isolates(string text, bool rtl, bool fallbackRtl)
    {
        Assert.Equal(rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            ComposerParagraphLayout.Direction(text, fallbackRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u0085")]
    [InlineData("\u2029")]
    [InlineData("\u2028")]
    public void Paragraph_separators_reset_the_base_but_a_soft_line_separator_does_not(string separator)
    {
        var properties = new GenericTextRunProperties(Typeface.Default, 14);
        const string first = "123 اردو";
        var source = new ComposerParagraphSource(first + separator + "123 English" + separator, properties, FlowDirection.LeftToRight, null);
        Assert.Equal(FlowDirection.RightToLeft, source.DirectionAt(0));
        Assert.Equal(separator == "\u2028" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, source.DirectionAt(first.Length + separator.Length));
        Assert.Equal(separator == "\u2028" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, source.DirectionAt(int.MaxValue));
    }

    [Fact]
    public void Native_formatter_installation_survives_a_fresh_locator_and_noncomposer_sources_are_delegated_unchanged()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(100, "hello");
            Assert.IsType<ComposerParagraphFormatter>(TextFormatter.Current);
            var recorder = new ParagraphFormatterRecorder();
            var wrapper = new ComposerParagraphFormatter(recorder);
            var source = new PlainParagraphTestSource();
            var properties = new GenericTextParagraphProperties(new GenericTextRunProperties(Typeface.Default, 14));
            var cache = new TextRunCache();
            wrapper.FormatLine(source, 7, 90, properties, null, cache);
            Assert.Same(source, recorder.Source); Assert.Same(properties, recorder.Properties); Assert.Same(cache, recorder.Cache);
            Assert.Equal(7, recorder.Start); Assert.Equal(90, recorder.Width);
            wrapper.FormatLine(source, 3, 80, properties);
            Assert.Same(properties, recorder.Properties); Assert.Null(recorder.Cache); Assert.Equal(3, recorder.Start); Assert.Equal(80, recorder.Width);
        });
    }
}
