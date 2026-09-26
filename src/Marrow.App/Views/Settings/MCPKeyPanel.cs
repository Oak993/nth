using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift (B2 1fbf81e), :1-251.

/// <summary>:6-76 <c>@MainActor final class MCPKeyPanel</c>: the floating panel a user pastes an MCP key into.</summary>
public sealed class MCPKeyPanel
{
    private const int CbtHook = 5; // ED-063 WH_CBT
    private const int CbtActivate = 5; // ED-063 HCBT_ACTIVATE

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    // ED-063: Avalonia can call SetFocus on a panel even when WS_EX_NOACTIVATE and MA_NOACTIVATE are used.
    // Refuse activation for this panel HWND with a thread-local WH_CBT hook.
    private static readonly HookProcedure ActivationHookProcedure = RefusePanelActivation;
    private static IntPtr activationHook;
    private static IntPtr activationPanelHandle;

    // :8 static let shared = MCPKeyPanel()
    public static MCPKeyPanel Shared { get; } = new();

    private Avalonia.Controls.Window? panel; // :10
    private bool escapeMonitorInstalled; // :11

    // :13 private init() {}
    private MCPKeyPanel() { }

    // :15-31 func present(entry:model:)
    public void Present(MCPCatalogEntry entry, AppModel model)
    {
        if (entry.KeysURL is { } keysURL && SwiftURL.Create(keysURL) is { } url)
            OpenURL(url); // :16-18

        // :19-25 Every presentation creates a new view, while the store retains changed drafts.
        var content = new MCPKeyPanelView(entry, model, Close);
        content.PresentedChrome();
        var measuredHeight = content.MeasureAtWidth(380);
        var window = panel ?? MakePanel();
        window.Content = content;
        window.Width = 380;
        window.Height = Math.Max(measuredHeight, 100);

        // :26-28 Place at the visible top right with a 24-point inset.
        if (window.Screens.Primary is { } screen)
        {
            var workingArea = screen.WorkingArea;
            var scaling = screen.Scaling;
            window.Position = new PixelPoint(
                checked((int)Math.Round(workingArea.X + workingArea.Width - (380 + 24) * scaling)),
                checked((int)Math.Round(workingArea.Y + 24 * scaling)));
        }

        // :29-30
        StartEscapeMonitor();
        window.Show();
        StartActivationHook(window);
    }

    // :33-36 func close()
    public void Close()
    {
        StopEscapeMonitor();
        StopActivationHook();
        if (panel is { } window)
            window.Hide();
    }

    // :38-61 private func makePanel() -> NSPanel
    private Avalonia.Controls.Window MakePanel()
    {
        var window = new Avalonia.Controls.Window
        {
            Title = "",
            Width = 380,
            Height = 200,
            MinHeight = 100,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
        };
        panel = window;
        return window;
    }

    // :63-70 private func startEscapeMonitor()
    private void StartEscapeMonitor()
    {
        if (escapeMonitorInstalled || panel is not { } window)
            return;

        window.KeyDown += OnPanelKeyDown;
        escapeMonitorInstalled = true;
    }

    // :72-75 private func stopEscapeMonitor()
    private void StopEscapeMonitor()
    {
        if (!escapeMonitorInstalled || panel is not { } window)
            return;

        window.KeyDown -= OnPanelKeyDown;
        escapeMonitorInstalled = false;
    }

    // ED-063: install a thread-local WH_CBT hook for the visible panel HWND.
    private static void StartActivationHook(Avalonia.Controls.Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || activationPanelHandle == handle && activationHook != IntPtr.Zero)
            return;

        StopActivationHook();
        activationPanelHandle = handle;
        activationHook = SetWindowsHookEx(CbtHook, ActivationHookProcedure, IntPtr.Zero, GetCurrentThreadId());
    }

    // ED-063: the hook is needed only while the panel is presented.
    private static void StopActivationHook()
    {
        if (activationHook != IntPtr.Zero)
            UnhookWindowsHookEx(activationHook);

        activationHook = IntPtr.Zero;
        activationPanelHandle = IntPtr.Zero;
    }

    // ED-063: refuse HCBT_ACTIVATE for the panel HWND, allowing other windows through.
    private static IntPtr RefusePanelActivation(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == CbtActivate && wParam == activationPanelHandle && activationPanelHandle != IntPtr.Zero)
            return new IntPtr(1);

        return CallNextHookEx(activationHook, code, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProcedure procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // :65-69 Escape closes the visible panel and consumes the event.
    private void OnPanelKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (panel?.IsVisible != true || eventArgs.Key != Key.Escape)
            return;

        eventArgs.Handled = true;
        Close();
    }

    // :16-18 NSWorkspace.shared.open(url); :150 the link destination
    private static void OpenURL(SwiftURL url)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
            return;

        _ = mainWindow.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.Absolute));
    }
}

/// <summary>:79-251 <c>struct MCPKeyPanelView: View</c>: numbered steps, one field per entry field, and Save.</summary>
public sealed class MCPKeyPanelView : Decorator
{
    private readonly MCPCatalogEntry entry; // :80
    private readonly AppModel model; // :81
    private readonly Action onDone; // :82
    private readonly List<TextBox> fields = []; // :85
    private readonly Button saveButton; // :96

    // :80-82 let entry, let model, let onDone; :87-120 body
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
        stack.Children.Add(BuildHeader()); // :89
        stack.Children.Add(BuildSteps()); // :90

        // :91-93
        foreach (var field in entry.Fields)
            stack.Children.Add(BuildField(field));

        // :94-101
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
            ClipToBounds = true,
            CornerRadius = new CornerRadius(Chrome.WindowCornerRadius),
        };

        // :108-114
        var layers = new Grid();
        layers.Children.Add(new WindowBackdrop(0.5));
        layers.Children.Add(new WindowDragArea());
        layers.Children.Add(content);
        Child = layers;

        // :115
        AttachedToVisualTree += (_, _) =>
        {
            if (fields.Count > 0)
                fields[0].Focus();
        };

        // :116
        KeyDown += OnKeyDown;

        // :117-119. Tracks the Read()-wrapped States property (not the State(id) method, which reads the same
        // backing dictionary directly): the correct dependency in principle, but see the STOP note in
        // MCPKeyPanelTests.cs - MCPStore's own Connect() (Services.MCP.MCPStore.cs) writes statesField in place
        // (`statesField[entry.Id] = ...`) with no Set()/Notify() call anywhere in the file, so nothing in this
        // class ever actually fires this Track today; a panel reopened while a probe is already running will not
        // self-close when it completes until MCPStore's own mutations are fixed to go through Set().
        this.Track(
            () => model.Mcp.States,
            states =>
            {
                if (states.TryGetValue(entry.Id, out var state) && state.IsConnected)
                    onDone();
            });
    }

    // :77-80, called directly wherever a draft changes (see the comment on saveButton's construction above).
    private void RefreshCanConnect() => saveButton.IsEnabled = model.Mcp.CanConnect(entry);

    // :87-104 frame width 380; :103 padding 18; :88 VStack(alignment: .leading, spacing: 14)
    internal double MeasureAtWidth(double width)
    {
        Measure(new Size(width, double.PositiveInfinity));
        return Math.Ceiling(DesiredSize.Height);
    }

    // :116 .onSubmit
    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || !model.Mcp.CanConnect(entry))
            return;

        eventArgs.Handled = true;
        Save();
    }

    // :122-143 private var header
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
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
        var name = new TextBlock { Text = entry.Name };
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

    // :145-161 private var steps
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

    // :16-18 NSWorkspace.shared.open(url); :150 Link destination
    private static void OpenURL(SwiftURL url)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
            return;

        _ = mainWindow.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.Absolute));
    }

    // :163-180 private func stepRow(_:text:accessory:)
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

    // :173-178 step text and accessory
    private static TextBlock StepText(string text)
    {
        var block = new TextBlock { Text = text };
        SwiftFont.Callout.Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        return block;
    }

    // :182-224 private func fieldView(_:)
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

    // :226-231 private func draftBinding(_:)
    private void SetDraft(MCPField field, string value)
    {
        model.Mcp.SetDraft(value, field, entry);
        RefreshCanConnect();
    }

    // :233-245 private func pickPath(for:)
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

    // :247-250 private func save()
    private void Save()
    {
        model.Mcp.Connect(entry);
        onDone();
    }
}
