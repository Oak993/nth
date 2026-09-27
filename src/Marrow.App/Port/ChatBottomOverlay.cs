// PORT-ONLY: ChatView.swift:1094-1095 .padding(.bottom, 10).frame(height: 0, alignment: .bottom).
// Avalonia otherwise compresses the child inside the zero-height frame; arrange its full measured height above it.
using Avalonia;
using Avalonia.Controls;

namespace Marrow.App.Port;

internal sealed class ChatBottomOverlay : Decorator
{
    private readonly double bottomPadding;

    public ChatBottomOverlay(Control content, double bottomPadding)
    {
        Child = content;
        this.bottomPadding = bottomPadding;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child!.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, -(Child.DesiredSize.Height + bottomPadding), finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }
}
