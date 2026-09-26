using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

// Source: DroppyCode/App/Views/Settings/MCPSetupPanel.swift (B3 52bc7926), :1-51.

namespace Marrow.App.Views.Settings;

/// <summary>:6-51 <c>@MainActor final class MCPSetupPanel: FloatingGuidePanel</c>: the floating panel shown when
/// a server refuses on its own side: the guide's page opens in the browser as the panel appears, so the steps
/// sit beside it.</summary>
public sealed class MCPSetupPanel : FloatingGuidePanel
{
    // :8 static let shared = MCPSetupPanel()
    public static MCPSetupPanel Shared { get; } = new();

    private string? presentedEntryID; // :10

    // :12 private init() { super.init(width: 400) }
    private MCPSetupPanel() : base(400) { }

    // :14-18 func isPresenting(_ entry: MCPCatalogEntry) -> Bool
    public bool IsPresenting(MCPCatalogEntry entry) =>
        panel?.IsVisible == true && presentedEntryID == entry.Id;

    // :20-33 func present(entry:guide:model:)
    public void Present(MCPCatalogEntry entry, MCPSetupGuide guide, AppModel model)
    {
        if (IsPresenting(entry))
        {
            panel?.Show(); // :22 panel?.orderFrontRegardless(); ED-063 (via FloatingGuidePanel.MakePanel's
                            // ShowActivated = false) means Show() brings the panel front without activating,
                            // matching MCPKeyPanel and the base class's own Present(_:).
            return;
        }

        presentedEntryID = entry.Id; // :25
        OpenPage(guide.PageURL); // :26-28

        var content = new MCPSetupPanelView(entry, guide, model, Close); // :29-31
        Present(content); // :32
    }

    // :26-28, :84-86 Open the guide page in the default browser.
    private static void OpenPage(string pageURL)
    {
        if (SwiftURL.Create(pageURL) is not { } url) return;
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } window) return;
        _ = window.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.RelativeOrAbsolute));
    }

    // :35-38 override func close()
    public override void Close()
    {
        presentedEntryID = null;
        base.Close();
    }

    // :42-50 func fit(height: CGFloat)
    public void Fit(double height)
    {
        if (panel?.IsVisible != true) return;
        var wanted = Math.Max(Math.Ceiling(height), 100);
        if (Math.Abs(panel.Height - wanted) <= 1) return;
        panel.Height = wanted;
    }
}

/// <summary>:55-212 <c>struct MCPSetupPanelView: View</c>: the reason, numbered steps, an open page button with
/// Check again, and the success view once the server connects.</summary>
public sealed class MCPSetupPanelView : Decorator
{
    private readonly MCPCatalogEntry entry; // :56
    private readonly MCPSetupGuide guide; // :57
    private readonly AppModel model; // :58
    private readonly Action onDone; // :59
    private bool checkedOnce; // :62
    private bool successPopped; // :63
    private readonly StackPanel body; // :67-144

    // :56-59 let entry, let guide, let model, let onDone; :67-144 body
    public MCPSetupPanelView(MCPCatalogEntry entry, MCPSetupGuide guide, AppModel model, Action onDone)
    {
        this.entry = entry;
        this.guide = guide;
        this.model = model;
        this.onDone = onDone;

        body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        PaddingHost(body);
        UpdateBody();
        SizeChanged += (_, _) => MCPSetupPanel.Shared.Fit(Bounds.Height);
        this.Track(() => model.Mcp.State(entry.Id), _ => UpdateBody());
        this.Track(() => EnvironmentValues.GetAccessibilityReduceMotion(this), _ => UpdateBody());
    }

    // :117-185 body. B3/ED-200 forces this: WindowDragArea is no longer a Decorator a caller can wrap content in
    // (:124-130 `.background { WindowDragArea() }.background { WindowBackdrop(opacity: 0.5) }` - SwiftUI's
    // background stacks behind the modified view in call order, so content sits above WindowDragArea, which sits
    // above WindowBackdrop). A Grid gives the same stack bottom to top, the way MCPKeyPanel.cs :108-114 already
    // layers its own WindowBackdrop/WindowDragArea/content.
    private void PaddingHost(Control content)
    {
        var layers = new Grid();
        layers.Children.Add(new WindowBackdrop(0.5));
        layers.Children.Add(new WindowDragArea());
        layers.Children.Add(content);
        Child = new Border
        {
            Width = 400,
            Padding = new Thickness(18),
            Background = Brushes.Transparent,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(Chrome.WindowCornerRadius),
            Child = layers,
        };
    }

    // :67-144 body
    private void UpdateBody()
    {
        body.Children.Clear();
        body.Children.Add(Header());

        var state = model.Mcp.State(entry.Id);
        if (state.IsConnected)
        {
            body.Children.Add(SuccessView(state));
            return;
        }

        body.Children.Add(Label(guide.Reason, SwiftFont.Callout, Chrome.PrimaryText));

        var steps = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        for (var index = 0; index < guide.Steps.Count; index++)
            steps.Children.Add(StepRow(index + 1, guide.Steps[index]));
        body.Children.Add(steps);

        var buttons = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        var openPage = new Button { Content = guide.PageLabel };
        openPage.Click += (_, _) => OpenGuidePage(guide.PageURL);
        Grid.SetColumn(openPage, 0);
        buttons.Children.Add(openPage);

        if (state.IsConnecting)
        {
            var checking = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            checking.Children.Add(new WorkingSpinner(cellSize: 3));
            checking.Children.Add(Label("Checking", SwiftFont.Subheadline, Chrome.SecondaryText));
            Grid.SetColumn(checking, 2);
            buttons.Children.Add(checking);
        }
        else
        {
            var check = new Button { Content = "Check again" };
            check.Click += (_, _) =>
            {
                checkedOnce = true;
                model.Mcp.Refresh(entry);
                UpdateBody();
            };
            Grid.SetColumn(check, 2);
            buttons.Children.Add(check);
        }

        body.Children.Add(buttons);

        if (!checkedOnce || state.IsConnecting) return;
        if (state is MCPConnectionState.NeedsSetup)
        {
            body.Children.Add(Label("Still off on the server's side. " + (guide.Note ?? ""),
                SwiftFont.Subheadline, Chrome.Warning));
        }
        else if (state is MCPConnectionState.Failed failed)
        {
            body.Children.Add(Label(failed.Message, SwiftFont.Subheadline, Chrome.Danger));
        }
    }

    // :146-167 private var header
    private Control Header()
    {
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        var icon = new MCPIcon(entry, 26) { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var texts = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            Margin = new Thickness(0, 0, 10, 0),
        };
        texts.Children.Add(Label(entry.Name,
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText));
        texts.Children.Add(Label(guide.Title, SwiftFont.Subheadline, Chrome.SecondaryText));
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        var close = new Button
        {
            Content = SwiftImage.SystemName("xmark")
                .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold))
                .ForegroundStyle(Chrome.SecondaryText),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        close.Click += (_, _) => onDone();
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        return row;
    }

    // :169-187 private func stepRow(_:text:accessory:)
    private static Control StepRow(int number, string text)
    {
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        var numberLabel = Label(number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold), Chrome.Accent);
        numberLabel.TextAlignment = TextAlignment.Center;
        numberLabel.VerticalAlignment = VerticalAlignment.Center;

        var numberCircle = new Border
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(9),
            Background = Chrome.Accent.Opacity(0.18).Brush(row),
            Child = numberLabel,
        };
        Grid.SetColumn(numberCircle, 0);
        row.Children.Add(numberCircle);

        var stepText = Label(text, SwiftFont.Callout, Chrome.PrimaryText);
        Grid.SetColumn(stepText, 1);
        row.Children.Add(stepText);
        return row;
    }

    // :189-211 private var successView
    private Control SuccessView(MCPConnectionState state)
    {
        var count = state is MCPConnectionState.Connected connected ? connected.ToolCount : 0;
        var success = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 120,
        };
        var glyph = SwiftImage.SystemName("checkmark.circle.fill")
            .Font(SwiftFont.System(34))
            .ForegroundStyle(Chrome.Success);
        glyph.RenderTransformOrigin = RelativePoint.Center;
        glyph.RenderTransform = new ScaleTransform(successPopped ? 1 : 0.5, successPopped ? 1 : 0.5);
        success.Children.Add(glyph);
        success.Children.Add(Label("Connected \U000000B7 " +
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " tools", // U+00B7 MIDDLE DOT
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), Chrome.PrimaryText));

        if (!successPopped)
        {
            successPopped = true;
            if (EnvironmentValues.GetAccessibilityReduceMotion(this))
            {
                glyph.RenderTransform = new ScaleTransform(1, 1);
            }
            else
            {
                var animation = new Avalonia.Animation.Animation
                {
                    Duration = TimeSpan.FromSeconds(0.5),
                    Easing = Marrow.UI.Port.Animation.Spring(duration: 0.5, bounce: 0.4).ToEasing(),
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, 1d),
                                new Setter(ScaleTransform.ScaleYProperty, 1d),
                            },
                        },
                    },
                };
                _ = animation.RunAsync(glyph);
            }

            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(1.2));
                onDone();
            });
        }
        return success;
    }

    // :26-28, :84-86 Open the guide page in the default browser.
    private static void OpenGuidePage(string pageURL)
    {
        if (SwiftURL.Create(pageURL) is not { } url) return;
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } window) return;
        _ = window.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.RelativeOrAbsolute));
    }

    // :150-155, :73-76
    private static TextBlock Label(string text, SwiftFont font, SwiftColor color)
    {
        var label = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        font.Apply(label);
        label.Foreground = color.Brush(label);
        return label;
    }
}
