// PORT-ONLY: lays out and draws a ParagraphText with its per-paragraph styles, one selection and one index space
// (ED-XXX, the ProseRunView representation decision).
using Avalonia;
using Avalonia.Controls;

namespace Marrow.UI.Port;

public sealed class ParagraphTextPresenter : Control
{
    public ParagraphText Text { get; private set; } = new();

    public string SelectedText => throw new NotImplementedException("MARROW_STUB ParagraphTextPresenter");

    public void SetText(ParagraphText text) => throw new NotImplementedException("MARROW_STUB ParagraphTextPresenter");

    public double Layout(double width) => throw new NotImplementedException("MARROW_STUB ParagraphTextPresenter");

    public double HeightForWidth(double width) => throw new NotImplementedException("MARROW_STUB ParagraphTextPresenter");

    public IReadOnlyList<Rect> RangeRects(int location, int length) =>
        throw new NotImplementedException("MARROW_STUB ParagraphTextPresenter");
}
