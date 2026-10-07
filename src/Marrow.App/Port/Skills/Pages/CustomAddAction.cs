// PORT-ONLY RR-18: title-row Add/chevron -> source menu -> sheet -> native picker (OD-0099 Custom C and Browse amendment).
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class CustomAddAction : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool presented;
        public bool Presented { get => Read(presented); set => Set(ref presented, value); }
    }
    private readonly ViewState state = new();
    private readonly CustomAddKind kind;
    private CustomAddSheet? sheet;
    internal CustomAddAction(CustomAddKind kind)
    {
        this.kind = kind;
        var chevron = LibraryUi.Glyph("chevron.down", 11, ChromeTokens.SecondaryText);
        var rotation = new AnimatedScalar(chevron, 0, angle => chevron.RenderTransform = new RotateTransform(angle));
        chevron.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        this.Track(() => state.Presented, show => rotation.Set(show ? 180 : 0, LibraryUi.Ease(this, Marrow.UI.Port.Settings.CzMotion.Ez(.3))));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { LibraryUi.Glyph("plus", 12, ChromeTokens.PrimaryText), LibraryUi.Text("Add", 12), chevron } };
        var button = PlainControls.Button(row, () => state.Presented = !state.Presented);
        SwiftGlass.StyleButton(button);
        button.Padding = new Avalonia.Thickness(13, 0, 9, 0);
        button.Named("Add", "custom-add");
        var popup = PlainControls.Popover(button, new Binding<bool>(() => state.Presented, value => state.Presented = value), Menu, PlacementMode.BottomEdgeAlignedRight);
        Child = new Panel { Children = { button, popup } };
        DetachedFromVisualTree += (_, _) => { sheet?.Dismiss(); sheet = null; };
    }
    private Control Menu()
    {
        var list = new StackPanel { Spacing = 2 };
        foreach (var source in new[] { CustomAddSource.Folder, CustomAddSource.Zip, CustomAddSource.GitHub })
        {
            var label = source switch { CustomAddSource.Folder => "From a folder\u2026", CustomAddSource.Zip => "From a .zip\u2026", _ => "From GitHub\u2026" };
            list.Children.Add(LibraryButtons.Make(label, () => Open(source), ButtonLook.Plain, "custom-from-" + source, 12.5, 30));
        }
        var menu = LibraryUi.Panel(list, 12, .06, new Avalonia.Thickness(5));
        menu.Width = 196;
        return menu;
    }
    private void Open(CustomAddSource source)
    {
        state.Presented = false;
        ChatSheetPresentation.Show(this, close =>
        {
            sheet = new CustomAddSheet(kind, CustomAddSeams.Backends.Current.For(kind), Pick, close);
            return sheet;
        }, () => { var old = sheet; sheet = null; old?.Dismiss(); }, inWindow: true, still: LibraryUi.Still(this));
        if (sheet is { } shown) _ = shown.SelectSource(source);
    }
    private async Task<string?> Pick(CustomAddSource source)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        if (source == CustomAddSource.Folder)
        {
            var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "Select folder" });
            return picked.FirstOrDefault()?.TryGetLocalPath();
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = "Open", FileTypeFilter = [new FilePickerFileType("Zip files (*.zip)") { Patterns = ["*.zip"] }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
