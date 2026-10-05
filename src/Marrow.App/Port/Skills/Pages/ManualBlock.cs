// PORT-ONLY: first-release feature unit SK-14 ("Download it yourself", OD-0099 review of version 3). There is no Swift twin. The fallback block under a failed item, drawn as the locked skills-downloads design draws it
// (sd-page.js fbBlock): the heading and three numbered steps (1 the file and where it is from, with "Open download page ↗"; 2 the folder, with "Open folder"; 3 what Marrow checks, with "Check again", which gives way
// to Checking… and Installed). The words are ManualFallbackWords' and LibraryWords'; the step texts come from the catalogue (ManualFallbacks).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class ManualBlock : Border
{
    private readonly CrossfadeSlot state = new();
    private readonly string id;
    private readonly Marrow.Services.Port.Skills.Pages.LibraryController controller;
    private string? kind;

    public ManualBlock(LibraryModel model, string id)
    {
        this.id = id;
        controller = model.Controller;
        var fallback = controller.ManualFallbackOf(id);
        IsVisible = fallback is not null;
        if (fallback is null) return;
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(LibraryUi.Text(ManualFallbackWords.Heading, 12, weight: SwiftFont.Weight.Semibold));
        rows.Children.Add(Step("1", [LibraryUi.Text(ManualFallbackWords.Get, 12, wrap: true), LibraryUi.Mono(fallback.File, 11.5), LibraryUi.Text(fallback.Source, 11.5, secondary: true, wrap: true)],
            LibraryButtons.Make(ManualFallbackWords.OpenPage, () => controller.OpenManualPage(id), ButtonLook.Plain, "manual-open-page")));
        rows.Children.Add(Step("2", [LibraryUi.Text(ManualFallbackWords.PutIn, 12, wrap: true), LibraryUi.Mono(controller.ManualFolderText, 11.5)],
            LibraryButtons.Make(ManualFallbackWords.OpenFolder, controller.OpenManualFolder, ButtonLook.Plain, "manual-open-folder")));
        state.HorizontalAlignment = HorizontalAlignment.Right;
        rows.Children.Add(Step("3", [LibraryUi.Text(fallback.ForTool ? ManualFallbackWords.CheckingTool : ManualFallbackWords.CheckingSet, 12, wrap: true)], state));
        Child = rows;
        Padding = new Thickness(12, 9, 12, 10);
        Background = ChromeTokens.Overlay(0.06).Brush(this);
        this.RoundedRectangle(10, RoundedCornerStyle.Continuous);
    }

    private static Grid Step(string number, Control[] words, Control action)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 8 };
        var n = LibraryUi.Text(number, 11.5, secondary: true);
        n.VerticalAlignment = VerticalAlignment.Top;
        var text = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var word in words) { word.Margin = new Thickness(0, 0, 4, 0); text.Children.Add(word); }
        Grid.SetColumn(text, 1);
        Grid.SetColumn(action, 2);
        action.VerticalAlignment = VerticalAlignment.Top;
        line.Children.Add(n);
        line.Children.Add(text);
        line.Children.Add(action);
        return line;
    }

    /// <summary>The state beside step 3: Check again while it is not here, Checking… while it is fetched and checked, Installed when it is.</summary>
    public void Apply(LibraryRow row)
    {
        if (!IsVisible) return;
        // The state words belong to an item that failed: Check again while it is failed, Checking… while it is fetched and checked again, Installed when that ends well. An item that never failed shows none
        // (the block sits in the failure's fold, which is shut for it).
        var failed = kind is "again" or "checking";
        var next = row.State switch
        {
            ItemState.Failed => "again",
            ItemState.Queued or ItemState.Downloading or ItemState.Checking when failed => "checking",
            ItemState.Installed or ItemState.UpdateAvailable when failed || kind == "installed" => "installed",
            _ => "idle",
        };
        if (next == kind) return;
        var first = kind is null;
        kind = next;
        Control control = next switch
        {
            "checking" => Line(new SystemSpinner { Width = 14, Height = 14 }, LibraryUi.Text(LibraryWords.Checking, 12.5, secondary: true)),
            "idle" => new Border { Height = 0 },
            "installed" => Line(LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success), LibraryUi.Text(LibraryWords.Installed, 12.5, secondary: true)),
            _ => LibraryButtons.Make(ManualFallbackWords.CheckAgain, () => _ = controller.CheckAgain(id), ButtonLook.Accent, "manual-check-again"),
        };
        state.Show(control, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
    }

    private static StackPanel Line(Control mark, Control text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        mark.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(mark);
        line.Children.Add(text);
        return line;
    }
}
