// PORT-ONLY: T11 step 2. The sheet behind "Show an example" under "What was handed over", in the Download all dialog's style: a glass surface 440 wide, the page's title and sub line, its introduction, the list of what is kept and what is shortened, and the
// line that names the handoff file. It is an example, not a record (the in-chat view of a real switch is a later step).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class HandedSheetView : Decorator
{
    internal HandedSheetView(HandedOverExample example, Action dismiss)
    {
        var title = LibraryUi.Text(example.Title, 15, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(example.Sub, 12, secondary: true);
        var close = LibraryButtons.Glyph("xmark", MultiAccountWords.HandedClose, "handed-close", dismiss);
        close.VerticalAlignment = VerticalAlignment.Top;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(close, 1);
        head.Children.Add(new StackPanel { Spacing = 1, Children = { title, sub } });
        head.Children.Add(close);

        var stack = new StackPanel { Spacing = 12, Children = { head } };
        if (example.AiNote is { } ai) stack.Children.Add(LibraryUi.Panel(LibraryUi.Text(ai, 12, wrap: true), 10, 0.06, new Thickness(12, 10)));
        stack.Children.Add(LibraryUi.Text(example.Intro, 12.5, wrap: true));
        if (example.Items.Count > 0)
        {
            var list = new StackPanel { Spacing = 8 };
            foreach (var item in example.Items)
            {
                var mark = LibraryUi.Glyph(item.Kept ? "checkmark" : "doc.text", 11, item.Kept ? ChromeTokens.Accent : ChromeTokens.SecondaryText, SwiftFont.Weight.Medium);
                mark.VerticalAlignment = VerticalAlignment.Top;
                mark.Margin = new Thickness(0, 3, 0, 0);
                var words = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { LibraryUi.Text(item.Title, 12.5, weight: SwiftFont.Weight.Medium), LibraryUi.Text(item.Count, 12, secondary: true) } },
                        LibraryUi.Text(item.Note, 11.5, secondary: true, wrap: true),
                    },
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                Grid.SetColumn(words, 1);
                row.Children.Add(mark);
                row.Children.Add(words);
                list.Children.Add(row);
            }
            stack.Children.Add(LibraryUi.Panel(LibraryUi.Scroller(list, 230), 10, 0.06, new Thickness(12, 10)));
        }
        if (example.File is { } file)
            stack.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { LibraryUi.Mono(file, 11.5), LibraryUi.Text("· " + example.FileLine, 12, secondary: true) },
            });
        if (example.HandshakeLine is { } handshake) stack.Children.Add(LibraryUi.Text(handshake, 12, secondary: true, wrap: true));

        var surface = new Border { Child = stack, Padding = new Thickness(18), Width = 440 }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        this.Named(example.Title, "handed-sheet");
    }
}
