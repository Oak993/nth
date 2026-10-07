using Avalonia.Media.TextFormatting;

namespace Marrow.App.Tests.Fixtures;

internal sealed class ParagraphFormatterRecorder : TextFormatter
{
    internal ITextSource? Source;
    internal TextParagraphProperties? Properties;
    internal TextRunCache? Cache;
    internal int Start;
    internal double Width;
    public override TextLine? FormatLine(ITextSource source, int start, double width, TextParagraphProperties properties, TextLineBreak? previous = null) =>
        FormatLine(source, start, width, properties, previous, null);
    public override TextLine? FormatLine(ITextSource source, int start, double width, TextParagraphProperties properties, TextLineBreak? previous, TextRunCache? cache)
    {
        Source = source; Properties = properties; Cache = cache; Start = start; Width = width; return null;
    }
}

internal sealed class PlainParagraphTestSource : ITextSource
{
    public TextRun? GetTextRun(int at) => null;
}
