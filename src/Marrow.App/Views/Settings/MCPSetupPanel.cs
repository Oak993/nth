using System;
using System.Threading.Tasks;
using Avalonia;
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
        content.ReportGeometry(); // :84-86 the first size is reported once the panel is up
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

    // PORT-ONLY: whether the visible panel is showing this content (a view in another window is never fitted to it).
    internal bool Shows(Control content) => panel?.IsVisible == true && ReferenceEquals(panel.Content, content);

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
public sealed class MCPSetupPanelView : FloatingGuidePanelView
{
    private readonly MCPCatalogEntry entry; // :56
    private readonly MCPSetupGuide guide; // :57
    private readonly AppModel model; // :58
    private readonly Action onDone; // :59
    private bool checkedOnce; // :62
    private bool successPopped; // :63
    private readonly StackPanel body; // :67-144
    private bool wasConnectedState;
    private bool builtOnce;
    private bool fitPending;

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
        builtOnce = true;
        SizeChanged += (_, _) => MCPSetupPanel.Shared.Fit(Bounds.Height);
        LayoutUpdated += (_, _) => FitToContent();
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
        // The backdrop and the drag area fill the whole 400 pt frame, the padding included (:123-130 `.padding(18).frame(width: 400)` come BEFORE
        // `.background { WindowDragArea() }.background { WindowBackdrop(opacity: 0.5) }`, so they fill the padded frame): inside this padded host
        // they reach out over it by the padding's own width. Without it the glass showed bare in an 18 pt band around the card (measured: the pixel
        // 8 pt from the edge read the Acrylic with no scrim).
        var backdrop = new WindowBackdrop(BackdropOpacity) { Margin = new Thickness(-18) };
        var drag = new WindowDragArea { Margin = new Thickness(-18) };
        layers.Children.Add(backdrop);
        layers.Children.Add(drag);
        layers.Children.Add(content);
        Child = new Border
        {
            Width = 400,
            Padding = new Thickness(18),
            Background = Brushes.Transparent,
            ClipToBounds = true, // PORT-ONLY (OD-0075, ED-338): the window's rectangle, not the 26 pt shape: the backdrop reaches the window's edge (FloatingGuidePanelView)
            Child = layers,
        };
    }

    // :67-144 body, then :84-86 `.onGeometryChange(for: CGFloat.self) { $0.size.height } action: { MCPSetupPanel.shared.fit(height: $0) }`:
    // the panel follows the content as it grows (the status line, the success view). The hosting window has a fixed
    // height, so the content's own height never changes the view's bounds: the wanted height is measured here, after
    // the layout the change causes (a measure taken inside the change itself still reads the old content).
    private void UpdateBody()
    {
        BuildBody();
        if (builtOnce) fitPending = true;
    }

    // Asks for the content's height to be reported to the panel after the next layout pass.
    internal void ReportGeometry()
    {
        fitPending = true;
        InvalidateMeasure();
    }

    // The wanted height is read once the layout the change caused has run (LayoutUpdated): a measure taken inside the
    // change itself still reads the old content.
    private void FitToContent()
    {
        // Only the panel that shows this view follows it, and a view not yet on screen keeps waiting for its first layout.
        if (!fitPending || !MCPSetupPanel.Shared.Shows(this)) return;
        fitPending = false;
        Child!.Measure(new Size(400, double.PositiveInfinity));
        MCPSetupPanel.Shared.Fit(Child.DesiredSize.Height);
    }

    private void BuildBody()
    {
        body.Children.Clear();
        body.Children.Add(Header());

        var state = model.Mcp.State(entry.Id);
        var wasConnected = wasConnectedState;
        wasConnectedState = state.IsConnected;
        if (state.IsConnected)
        {
            body.Children.Add(SuccessView(state));
            // :135-141 onChange(of: state.isConnected) runs on a change, not for the value the panel opened with.
            if (!wasConnected && builtOnce)
                MainActor.Spawn(async () =>
                {
                    await SwiftTask.TrySleep(TimeSpan.FromSeconds(1.2));
                    onDone();
                });
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

        // :134-138 Button(guide.pageLabel) { open the page }.buttonStyle(.glass)
        var openPage = GlassButton(guide.PageLabel, () => OpenGuidePage(guide.PageURL));
        Grid.SetColumn(openPage, 0);
        buttons.Children.Add(openPage);

        if (state.IsConnecting)
        {
            // A spinner and a label are not controls: a press on them drags the panel like the background (:77-80).
            var checking = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, IsHitTestVisible = false };
            checking.Children.Add(ConnectingWait.Mark()); // nth step 11: the 12 px spinner and the light through the label
            checking.Children.Add(ConnectingWait.Label("Checking", SwiftFont.Subheadline, Chrome.SecondaryText, wrap: true));
            Grid.SetColumn(checking, 2);
            buttons.Children.Add(checking);
        }
        else
        {
            // :147-153 Button("Check again") { checkedOnce = true; model.mcp.refresh(entry) }.buttonStyle(.glassProminent)
            var check = GlassProminentButton("Check again", () =>
            {
                checkedOnce = true;
                model.Mcp.Refresh(entry);
                UpdateBody();
            });
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

        // :197 HStack(alignment: .top, spacing: 10): the icon sits at the top, not centred against the two lines.
        var icon = new MCPIcon(entry, 26) { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var texts = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            // :197-215 spacing 10 on each side of Spacer(minLength: 8): at least 10 + 8 + 10 points before the close button.
            Margin = new Thickness(0, 0, 18, 0),
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
            Margin = new Thickness(10, 0, 0, 0),
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
            IsHitTestVisible = false, // :222-227 the circle is a shape, not a control: a press on it drags the panel
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
        // :240 VStack(spacing: 8), :253 .frame(maxWidth: .infinity, minHeight: 120, alignment: .center): a full-width
        // block at least 120 tall with the stack centred in it.
        var success = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var successFrame = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 120,
            Children = { success },
        };
        var glyph = SwiftImage.SystemName("checkmark.circle.fill")
            .Font(SwiftFont.System(34))
            .ForegroundStyle(Chrome.Success);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.RenderTransformOrigin = RelativePoint.Center;
        glyph.RenderTransform = new ScaleTransform(successPopped ? 1 : 0.5, successPopped ? 1 : 0.5);
        success.Children.Add(glyph);
        success.Children.Add(Label("Connected \U000000B7 " +
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " tools", // U+00B7 MIDDLE DOT
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), Chrome.PrimaryText));

        // :255-259 .onAppear: under reduce-motion the glyph is set straight to full size, otherwise it springs from 0.5 to 1
        // with `.spring(duration: 0.5, bounce: 0.4)`, once. ENGINEERING DECISION: the frame seam and the shared spring,
        // as the card's success view does (MCPSettingsPage.cs), so a test can drive the frames.
        glyph.AttachedToVisualTree += (_, _) =>
        {
            if (successPopped) return;
            successPopped = true;
            if (EnvironmentValues.GetAccessibilityReduceMotion(this))
            {
                glyph.RenderTransform = new ScaleTransform(1, 1);
                return;
            }

            var start = SwiftClock.Now;
            var spring = Marrow.UI.Port.Animation.Spring(duration: 0.5, bounce: 0.4);
            var frames = FrameClock.Source.Current;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var elapsed = (SwiftClock.Now - start).TotalSeconds;
                var complete = elapsed >= spring.Duration; // ED-011: the shared spring's settling duration
                var scale = complete ? 1 : 0.5 + 0.5 * spring.Progress(elapsed);
                glyph.RenderTransform = new ScaleTransform(scale, scale);
                if (!complete) frames.RequestFrame(frame!);
            };
            glyph.DetachedFromVisualTree += (_, _) => frames.CancelFrame(frame!);
            frames.RequestFrame(frame);
        };
        return successFrame;
    }

    // :134-138 `.buttonStyle(.glass)` at the default control size: the regular interactive glass in a continuous capsule,
    // the same recipe FollowUpEditor and MCPSettingsPage.ActionButton use (ENGINEERING DECISION; OD-0005 governs the material).
    private static Control GlassButton(string title, Action action)
    {
        var framed = new Border
        {
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = Chrome.CapsuleContentHeight,
            Background = Brushes.Transparent,
            Child = ButtonLabel(title),
        }.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(framed, action);
    }

    // :147-153 `.buttonStyle(.glassProminent)`: the tinted twin, the recipe WelcomeView.GlassProminentButton holds.
    private static Control GlassProminentButton(string title, Action action) =>
        WelcomeView.GlassProminentButton(ButtonLabel(title), Chrome.CapsuleContentHeight, action);

    // A glass button's label (MCPSetupPanel.swift:83-88, :98-102: Button(title) with no .font): SwiftUI's implicit
    // default in primary text, the body size in the face's own line box (ED-290 (1)).
    private static TextBlock ButtonLabel(string title)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.Default.Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        return label;
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
            IsHitTestVisible = false, // :77-80 the reason, steps and status line are not controls: a press drags the panel
        };
        font.Apply(label);
        label.Foreground = color.Brush(label);
        return label;
    }
}
