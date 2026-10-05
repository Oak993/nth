using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Marrow.App;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift (B3 52bc7926), :1-21.

/// <summary>:6-21 <c>@MainActor final class MCPKeyPanel: FloatingGuidePanel</c>: the floating panel a user pastes
/// an MCP key into. Opens the entry's <c>keysURL</c> in the browser as it appears, so the key page sits beside
/// the panel.</summary>
public sealed class MCPKeyPanel : FloatingGuidePanel
{
    // :8 static let shared = MCPKeyPanel()
    public static MCPKeyPanel Shared { get; } = new();

    // :10 private init() { super.init(width: 380) }
    private MCPKeyPanel() : base(380) { }

    // :12-20 func present(entry:model:)
    public void Present(MCPCatalogEntry entry, AppModel model)
    {
        if (entry.KeysURL is { } keysURL && SwiftURL.Create(keysURL) is { } url)
            OpenURL(url); // :13-14

        // :16-18 Every presentation creates a new view, while the store retains changed drafts.
        var content = new MCPKeyPanelView(entry, model, Close);
        content.PresentedChrome();
        Present(content); // :19
    }

    // :13-14, :94 the link destination
    private static void OpenURL(SwiftURL url)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
            return;

        _ = mainWindow.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.Absolute));
    }
}

/// <summary>:24-195 <c>struct MCPKeyPanelView: View</c>: numbered steps, one field per entry field, and Save.</summary>
public sealed class MCPKeyPanelView : FloatingGuidePanelView
{
    private readonly MCPCatalogEntry entry; // :25
    private readonly AppModel model; // :26
    private readonly Action onDone; // :27
    private readonly List<TextBox> fields = []; // :29
    private readonly Button saveButton; // :40
    private OnChange<bool>? connectionChanges;

    // :25-27 let entry, let model, let onDone; :31-64 body
    public MCPKeyPanelView(MCPCatalogEntry entry, AppModel model, Action onDone)
    {
        this.entry = entry;
        this.model = model;
        this.onDone = onDone;

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        stack.Children.Add(BuildHeader()); // :33
        stack.Children.Add(BuildSteps()); // :34

        // :35-37
        foreach (var field in entry.Fields)
            stack.Children.Add(BuildField(field));

        // :38-45
        saveButton = new Button
        {
            Content = entry.IsOAuth ? "Sign in" : "Save and connect",
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6),
        };
        saveButton.Click += (_, _) => Save();
        // Not a Track(): MCPStore.CanConnect (Services.MCP.MCPStore.cs) reads Drafts, a plain public Dictionary
        // mutated in place with no Set()/Notify() call anywhere, so a Track subscription on it would only ever
        // fire once (at attach) and never again as the user types. RefreshCanConnect is called directly from
        // every place a draft changes instead (below), which is reliable regardless of that gap.
        RefreshCanConnect();

        var saveRow = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var spacer = new Border();
        Grid.SetColumn(spacer, 0);
        saveRow.Children.Add(spacer);
        Grid.SetColumn(saveButton, 1);
        saveRow.Children.Add(saveButton);
        stack.Children.Add(saveRow);

        var content = new Border
        {
            Width = 380,
            Padding = new Thickness(18),
            Child = stack,
            ClipToBounds = true, // PORT-ONLY (OD-0075, ED-NEW-corner-seam): the window's rectangle, not the 26 pt shape, as MCPSetupPanel's clip (FloatingGuidePanelView)
        };

        // :52-58
        var layers = new Grid();
        layers.Children.Add(new WindowBackdrop(BackdropOpacity));
        layers.Children.Add(new WindowDragArea());
        layers.Children.Add(content);
        Child = layers;

        // :59
        AttachedToVisualTree += (_, _) =>
        {
            if (fields.Count > 0)
                fields[0].Focus();
        };

        // :60
        KeyDown += OnKeyDown;

        // :61-63. onChange observes this entry's connected flag, without an initial callback.
        // A connected server's optional fields can still be edited when the panel opens.
        AttachedToVisualTree += (_, _) => connectionChanges ??= new OnChange<bool>(
            () => model.Mcp.State(entry.Id).IsConnected,
            (_, connected) => { if (connected) onDone(); });
        DetachedFromVisualTree += (_, _) =>
        {
            connectionChanges?.Dispose();
            connectionChanges = null;
        };
    }

    // :44, called directly wherever a draft changes (see the comment on saveButton's construction above).
    private void RefreshCanConnect() => saveButton.IsEnabled = model.Mcp.CanConnect(entry);

    // :60 .onSubmit
    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || !model.Mcp.CanConnect(entry))
            return;

        eventArgs.Handled = true;
        Save();
    }

    // :66-87 private var header
    private Control BuildHeader()
    {
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // :67,77 HStack(alignment: .top, spacing: 10): the texts are followed by 10, Spacer(minLength: 8) and another 10 before the close button.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new MCPIcon(entry, 26)
        {
            Width = 26,
            Height = 26,
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var title = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var name = new TextBlock { Text = entry.Name, TextWrapping = TextWrapping.Wrap }; // no line limit: a long name wraps
        SwiftFont.System(14, SwiftFont.Weight.Semibold).Apply(name);
        name.Foreground = Chrome.PrimaryText.Brush(name);
        title.Children.Add(name);

        var subtitle = new TextBlock { Text = "Connect" };
        SwiftFont.Subheadline.Apply(subtitle);
        subtitle.Foreground = Chrome.SecondaryText.Brush(subtitle);
        title.Children.Add(subtitle);
        Grid.SetColumn(title, 2);
        row.Children.Add(title);

        var closeImage = SwiftImage.SystemName("xmark")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold))
            .ForegroundStyle(Chrome.SecondaryText);
        var closeButton = new Button
        {
            Content = closeImage,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        closeButton.Click += (_, _) => onDone();
        Grid.SetColumn(closeButton, 4);
        row.Children.Add(closeButton);
        return row;
    }

    // :89-105 private var steps
    private Control BuildSteps()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (entry.KeysURL is { } keysURL && SwiftURL.Create(keysURL) is { } url)
        {
            var first = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            first.Children.Add(StepText("The page is open in your browser"));

            var linkText = new TextBlock { Text = "Open it again" };
            SwiftFont.Subheadline.Apply(linkText);
            var link = new Button
            {
                Content = linkText,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            link.Click += (_, _) => OpenURL(url);
            first.Children.Add(link);

            stack.Children.Add(StepRow(1, first));
            stack.Children.Add(StepRow(2, StepText("Copy the " + SwiftString.Lowercased(entry.Fields.Count > 0 ? entry.Fields[0].Label : "key"))));
            stack.Children.Add(StepRow(3, StepText("Paste it below and save")));
        }
        else
        {
            stack.Children.Add(StepRow(1, StepText("Enter the " + SwiftString.Lowercased(entry.Fields.Count > 0 ? entry.Fields[0].Label : "value"))));
            stack.Children.Add(StepRow(2, StepText(entry.IsOAuth ? "Sign in in your browser" : "Save")));
        }

        return stack;
    }

    // :13-14 NSWorkspace.shared.open(url); :94 Link destination
    private static void OpenURL(SwiftURL url)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
            return;

        _ = mainWindow.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.Absolute));
    }

    // :107-124 private func stepRow(_:text:accessory:)
    private static Control StepRow(int number, Control textAndAccessory)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var numberText = new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold).Apply(numberText);
        numberText.Foreground = Chrome.Accent.Brush(numberText);
        var circle = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Chrome.Accent.Opacity(0.18).Brush(numberText),
            Child = numberText,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(circle);
        row.Children.Add(textAndAccessory);
        return row;
    }

    // :117-122 step text and accessory
    private static TextBlock StepText(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }; // :108, :117 HStack: each child on the centre line
        SwiftFont.Callout.Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        return block;
    }

    // :126-168 private func fieldView(_:)
    private Control BuildField(MCPField field)
    {
        var block = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var label = new TextBlock { Text = field.Label };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(label);
        label.Foreground = Chrome.SecondaryText.Brush(label);
        block.Children.Add(label);

        var row = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox
        {
            PlaceholderText = field.Placeholder,
            Text = model.Mcp.Value(field, entry),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = false,
        };
        PlainControls.PlainTextField(textBox); // :141 .textFieldStyle(.plain); without a template the field drew nothing
        if (field.FieldKind == MCPField.Kind.Secret)
            textBox.PasswordChar = '\u2022'; // U+2022 BULLET
        SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced).Apply(textBox);
        textBox.TextChanged += (_, _) => SetDraft(field, textBox.Text ?? "");
        textBox.Track(
            () => model.Mcp.Value(field, entry),
            value =>
            {
                if (textBox.Text != value)
                    textBox.Text = value;
            });
        fields.Add(textBox);
        Grid.SetColumn(textBox, 0);
        row.Children.Add(textBox);

        if (field.FieldKind == MCPField.Kind.Path)
        {
            var folder = SwiftImage.SystemName("folder")
                .Font(SwiftFont.System(SwiftFont.TextStyle.Callout))
                .ForegroundStyle(Chrome.SecondaryText);
            var pickButton = new Button
            {
                Content = folder,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0), // :126 HStack(spacing: 6)
            };
            pickButton.Click += async (_, _) => await PickPath(field);
            Grid.SetColumn(pickButton, 1);
            row.Children.Add(pickButton);
        }

        var surface = new Border
        {
            Padding = new Thickness(8, 7),
            CornerRadius = new CornerRadius(8),
            Background = Chrome.Overlay(0.06).Brush(row),
            Child = row,
        };
        block.Children.Add(surface);

        if (field.Help is { Length: > 0 } help)
        {
            var helpText = new TextBlock { Text = help };
            SwiftFont.System(10.5).Apply(helpText);
            helpText.Foreground = Chrome.SecondaryText.Brush(helpText);
            block.Children.Add(helpText);
        }

        return block;
    }

    // :170-175 private func draftBinding(_:)
    private void SetDraft(MCPField field, string value)
    {
        model.Mcp.SetDraft(value, field, entry);
        RefreshCanConnect();
    }

    // :177-189 private func pickPath(for:)
    private async Task PickPath(MCPField field)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        if (entry.Id == "filesystem")
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folderPath)
                SetDraft(field, folderPath);
        }
        else
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } filePath)
                SetDraft(field, filePath);
        }
    }

    // :191-194 private func save()
    private void Save()
    {
        model.Mcp.Connect(entry);
        onDone();
    }
}
