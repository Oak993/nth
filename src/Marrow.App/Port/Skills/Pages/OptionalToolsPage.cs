// PORT-ONLY RR-18: optional downloads join the existing Tools page (now Updates by OD-0100), in their own layout-D library.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class OptionalToolsPage : LibraryPage
{
    public OptionalToolsPage() : base(LibrarySection.Skills, tools: true)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { LibraryUi.Text("Tools", 19, weight: SwiftFont.Weight.Semibold) } };
        if (ChromeAccessories.FirstOrDefault() is { } add) { Grid.SetColumn(add, 1); header.Children.Add(add); }
        var body = Child!;
        Child = null;
        Child = new StackPanel { Spacing = 12, Children = { header, body } };
    }
}
