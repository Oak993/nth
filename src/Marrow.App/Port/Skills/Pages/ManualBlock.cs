// PORT-ONLY: first-release feature unit SK-14 ("Download it yourself", OD-0099 review of version 3). There is no Swift twin. The fallback block under a failed item, drawn as the locked skills-downloads design draws it
// (sd-page.js fbBlock, skills-downloads.html CSS 567 to 575): the heading and three numbered steps (1 the file and where it is from, with "Open download page ↗"; 2 the folder, with "Open folder"; 3 what Marrow checks, with
// "Check again", which gives way to Checking… and Installed). The words are ManualFallbackWords' and LibraryWords'; the step texts come from the catalogue (ManualFallbacks). Dimensions are the CSS's: padding 9 and 11,
// rows 7 apart, the number a 18 point circle in an 18 point column, 8 between columns, buttons 24 high with 11.5 labels, code 11 and broken anywhere (word-break:break-all) inside the column.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
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
        var rows = new StackPanel { Spacing = 7 };
        rows.Children.Add(LibraryUi.Text(ManualFallbackWords.Heading, 12, weight: SwiftFont.Weight.Semibold));
        rows.Children.Add(Step("1", [StepOne(fallback), LibraryUi.Text(fallback.Source, 11, secondary: true, wrap: true)],
            Small(ManualFallbackWords.OpenPage, () => controller.OpenManualPage(id), ButtonLook.Plain, "manual-open-page")));
        rows.Children.Add(Step("2", [LibraryUi.Text(ManualFallbackWords.PutIn, 12, secondary: true, wrap: true), Code(controller.ManualFolderText)],
            Small(ManualFallbackWords.OpenFolder, controller.OpenManualFolder, ButtonLook.Plain, "manual-open-folder")));
        state.HorizontalAlignment = HorizontalAlignment.Right;
        rows.Children.Add(Step("3", [LibraryUi.Text(fallback.ForTool ? ManualFallbackWords.CheckingTool : ManualFallbackWords.CheckingSet, 12, secondary: true, wrap: true)], state));
        Child = rows;
        Padding = new Thickness(11, 9);
        Margin = new Thickness(0, 8, 0, 0);
        Background = ChromeTokens.Overlay(0.06).Brush(this);
        this.RoundedRectangle(10, RoundedCornerStyle.Continuous);
    }

    private static Button Small(string text, Action action, ButtonLook look, string buttonId) => LibraryButtons.Make(text, action, look, buttonId, size: 11.5, height: 24);

    /// <summary>
    /// Step 1 as one wrapping line of prose (ED-NEW-dlhonest-1, owner 2026-10-11, over the design's whole phrase in code): "Get " and the words in the step's 12 point secondary text, and only the literal values
    /// (the file name, the commit, wheels.txt) in the design's code look, 11 point mono in the primary colour on its tint.
    /// </summary>
    internal static TextBlock StepOne(ManualFallback fallback)
    {
        var line = LibraryUi.Text("", 12, secondary: true, wrap: true);
        line.Text = null;
        line.Inlines ??= new InlineCollection();
        line.Inlines.Add(new Run(ManualFallbackWords.Get));
        foreach (var part in fallback.Parts)
        {
            var run = new Run(part.Text);
            if (part.Code)
            {
                run.FontFamily = LibraryUi.MonoFamily;
                run.FontSize = 11;
                run.Foreground = ChromeTokens.PrimaryText.Brush(line);
                run.Background = ChromeTokens.Overlay(0.06).Brush(line);
            }
            line.Inlines.Add(run);
        }
        AutomationProperties.SetName(line, ManualFallbackWords.Get + fallback.File);
        return line;
    }

    /// <summary>The design's code: 11 points, on its own tint, broken anywhere to stay inside the column.</summary>
    private static Border Code(string text)
    {
        var block = LibraryUi.Mono(text, 11);
        block.TextWrapping = TextWrapping.Wrap;
        var tint = new Border { Child = block, Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Left };
        tint.RoundedRectangle(4, RoundedCornerStyle.Continuous);
        tint.Background = ChromeTokens.Overlay(0.06).Brush(tint);
        return tint;
    }

    private static Grid Step(string number, Control[] words, Control action)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,Auto"), ColumnSpacing = 8 };
        var n = new Border { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Child = LibraryUi.Text(number, 10.5, weight: SwiftFont.Weight.Semibold) };
        n.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        n.Background = ChromeTokens.Overlay(0.10).Brush(n);
        if (n.Child is Control digit) { digit.HorizontalAlignment = HorizontalAlignment.Center; digit.VerticalAlignment = VerticalAlignment.Center; }
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var word in words) text.Children.Add(word);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(action, 2);
        action.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(n);
        line.Children.Add(text);
        line.Children.Add(action);
        return line;
    }

    /// <summary>True while Checking… shows: the failure's fold stays open for it (the design keeps the block in view while it checks).</summary>
    public bool Checking => kind == "checking";

    /// <summary>The state beside step 3: Check again while it is failed (and off while nothing can be fetched), Checking… while it is fetched and checked again, Installed when that ends well.</summary>
    public void Apply(LibraryRow row)
    {
        if (!IsVisible) return;
        // The state words belong to an item that failed. An item that never failed shows none (the block sits in the failure's fold, which is shut for it).
        var failed = kind is "again" or "checking";
        var next = row.State switch
        {
            ItemState.Failed => "again",
            ItemState.Queued or ItemState.Downloading or ItemState.Checking when failed => "checking",
            ItemState.Installed or ItemState.UpdateAvailable when failed || kind == "installed" => "installed",
            _ => "idle",
        };
        if (next != kind)
        {
            var first = kind is null;
            kind = next;
            Control control = next switch
            {
                "checking" => Line(new Marrow.UI.Port.Nth.NthSpinner { Width = 14, Height = 14 }, LibraryUi.Text(LibraryWords.Checking, 12.5, secondary: true)),
                "idle" => new Border { Height = 0 },
                "installed" => Line(LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success), LibraryUi.Text(LibraryWords.Installed, 12.5, secondary: true)),
                _ => Small(ManualFallbackWords.CheckAgain, () => _ = controller.CheckAgain(id), ButtonLook.Accent, "manual-check-again"),
            };
            state.Show(control, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (state.Live is Button again) again.IsEnabled = controller.CanFetchItem(id);
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
