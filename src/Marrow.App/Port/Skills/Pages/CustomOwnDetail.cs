// PORT-ONLY RR-18, sd-page.js:385,410,414: added copy, removal explanation and actual source provenance; no invented version/licence.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Custom;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class CustomOwnDetail : Decorator
{
    private readonly CustomOwnModel model;
    private readonly string id;
    private readonly CrossfadeSlot action = new();
    private readonly CzFoldRow removeWhy;
    private readonly CzCrossfadeText error = new("") { FontSize = 12, Wrap = true };
    private OwnItem current;
    internal CustomOwnDetail(CustomOwnModel model, CustomAddKind kind, OwnItem item)
    {
        this.model = model; current = item; id = item.Id;
        ProvenanceText = "Your own · " + item.Provenance.Location + (item.Provenance.Commit is { } commit ? " · " + commit : "");
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
            LibraryUi.Text(item.Name, 18, weight: SwiftFont.Weight.Semibold, trim: true), LibraryUi.Text(item.Description, 12, secondary: true, trim: true),
            LibraryUi.Text(ProvenanceText, 12, secondary: true, trim: true) } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), ColumnSpacing = 14, Children = { LibraryUi.Tile(CustomOwnRow.Symbol(kind), 48, 13), words } };
        Grid.SetColumn(words, 1);
        removeWhy = new CzFoldRow(LibraryUi.Panel(LibraryUi.Text("Removes it from Marrow’s folder. Your original stays where it was.", 12, secondary: true, wrap: true), 10, .06, new Thickness(12, 9)), false);
        var stack = new StackPanel { Spacing = 12, Children = { header, action, removeWhy } };
        if (kind != CustomAddKind.Tools)
        {
            var toggle = new SettingsSwitch(new Binding<bool>(() => { _ = model.Revision; return current.Enabled; }, value => _ = model.Perform(() => model.SetEnabled(id, value))));
            toggle.Named(item.Name, "own-switch-" + id);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { toggle, LibraryUi.Mono(item.Name, 11.5) } };
            stack.Children.Add(LibraryUi.Panel(new StackPanel { Spacing = 7, Children = { LibraryUi.Text(LibraryWords.InEverySession, 12, weight: SwiftFont.Weight.Semibold), row,
                LibraryUi.Text(LibraryWords.SwitchNote, 11.5, secondary: true, wrap: true) } }));
        }
        if (kind == CustomAddKind.Tools) stack.Children.Add(LibraryUi.Text("Provides " + item.Command, 12, secondary: true, wrap: true));
        stack.Children.Add(LibraryUi.Panel(new StackPanel { Spacing = 3, Children = {
            LibraryUi.Text("Source", 12, weight: SwiftFont.Weight.Semibold), LibraryUi.Text(item.Provenance.Location, 12, secondary: true, wrap: true) } }));
        if (item.Provenance.Commit is { } pinned) stack.Children.Add(LibraryUi.Text(pinned, 11.5, secondary: true, wrap: true));
        stack.Children.Add(error);
        Child = stack;
        Added(false);
    }
    internal string ProvenanceText { get; }
    internal void Apply(OwnItem item) { current = item; error.SetText(model.Error ?? ""); }
    private void Added(bool animate)
    {
        removeWhy.Open = false;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = {
            LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success), LibraryUi.Text("Added", 12.5, secondary: true),
            LibraryButtons.Glyph("trash", "Remove", "own-ask-remove-" + id, AskRemove) } };
        action.Show(line, animate ? LibraryUi.Ease(this, LibraryUi.Fade) : null);
    }
    private void AskRemove()
    {
        removeWhy.Open = true;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = {
            LibraryButtons.Make("Remove", () => _ = model.Perform(() => model.Remove(id)), ButtonLook.Plain, "own-remove-" + id),
            LibraryButtons.Make("Cancel", () => Added(true), ButtonLook.Plain, "own-cancel-remove-" + id) } };
        action.Show(line, LibraryUi.Ease(this, LibraryUi.Fade));
    }
}
