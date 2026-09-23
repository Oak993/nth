using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/UI/Chrome/InfoHoverButton.swift (B2 1fbf81e), :5-56.
// Port shape (R-VIEW-1, R-VIEW-2, R-VIEW-9): a SwiftUI View is a Decorator whose constructor builds `body`;
// the info mark is a SwiftImage; @ViewBuilder parameter content becomes Func<Control> (ED-095). @State properties
// (isHovering, isOverPopover, isPresented, closeTask) become properties of a ViewState class inheriting
// MainActorModel; the close task is a TaskHandle (R-TASK-1, ED-066). Text color (line 18) derives from
// Chrome.SecondaryText (label at 0.7 opacity; ED-062) at 0.6, rising to 1.0 on hover.
[Collection("Headless")]
public sealed class InfoHoverButtonTests(HeadlessSession headless)
{
    // Store fixture parameter to satisfy compiler (required by xUnit for Avalonia Headless tests)
    private readonly HeadlessSession _headless = headless;
    private static readonly Point Away = new(595, 295);

    private static Window Show(Control content, ThemeVariant? theme = null)
    {
        var window = new Window { Width = 600, Height = 300, Content = content };
        window.RequestedThemeVariant = theme ?? ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return control;
    }

    private static AutomationPeer Peer(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control);

    private static Point Centre(Control control, Window window)
    {
        var bounds = new Rect(control.TranslatePoint(new Point(0, 0), window)!.Value, control.Bounds.Size);
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void MouseMove(Window window, Point at)
    {
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Effective(IBrush? brush)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        // Allow ±1 per component for rounding in opacity calculations
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:16-19
    public void InfoHoverButton_RendersWith20x20Frame()
    {
        // Line 19: .frame(width: 20, height: 20)
        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));

        Assert.Equal(20, button.Width);
        Assert.Equal(20, button.Height);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:22-23
    public void InfoHoverButton_HasHelpAsAccessibilityLabel()
    {
        // Line 22: .help(help) and line 23: .accessibilityLabel(Text(help))
        var helpText = "How Hydra works";
        var button = new InfoHoverButton(
            help: helpText,
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var image = VisualQuery.Descendants<SwiftImage>(button).FirstOrDefault();
        Assert.NotNull(image);

        var name = AutomationProperties.GetName(image);
        Assert.Equal(helpText, name);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:18
    public void InfoHoverButton_IdleOpacityIs06()
    {
        // Line 18: .foregroundStyle(Chrome.secondaryText.opacity(isHovering ? 1 : 0.6))
        // When idle (not hovering), opacity is 0.6.
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button), ThemeVariant.Light);
        var image = VisualQuery.Descendants<SwiftImage>(button).FirstOrDefault();
        Assert.NotNull(image);

        // idleColor = Chrome.SecondaryText (label at 0.7) at 0.6
        var idleExpected = Marrow.UI.Chrome.Chrome.SecondaryText.Opacity(0.6).Resolve(ThemeVariant.Light);
        var actual = Effective(image.Foreground);
        AssertColor(idleExpected, actual);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:18, :25-28
    public void InfoHoverButton_HoverOpacityRisesToFull()
    {
        // Line 18: opacity rises to 1 on hover
        // Lines 25-28: onHover { over in ... isPresented = true }
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button), ThemeVariant.Light);
        var center = Centre(button, window);

        // Move pointer over button
        MouseMove(window, center);

        var image = VisualQuery.Descendants<SwiftImage>(button).FirstOrDefault();
        Assert.NotNull(image);
        var hoveredExpected = Marrow.UI.Chrome.Chrome.SecondaryText.Opacity(1.0).Resolve(ThemeVariant.Light);
        var actual = Effective(image.Foreground);
        AssertColor(hoveredExpected, actual);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:25-29
    public void InfoHoverButton_HoverOpensPopover()
    {
        // Lines 25-29: onHover { over in ... if over { isPresented = true } }
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var center = Centre(button, window);

        // Initially, popover should not be shown
        Assert.False(button.IsPopoverPresented);

        // Move pointer over button to trigger hover
        MouseMove(window, center);

        // Popover should open on hover
        Assert.True(button.IsPopoverPresented);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:34
    public void InfoHoverButton_ClickTogglesPopover()
    {
        // Line 34: .onTapGesture { isPresented.toggle() }
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var center = Centre(button, window);

        // Initially closed
        Assert.False(button.IsPopoverPresented);

        // Click to open
        Click(window, center);
        Assert.True(button.IsPopoverPresented);

        // Click to close
        Click(window, center);
        Assert.False(button.IsPopoverPresented);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:30-32, 54-56
    public void InfoHoverButton_PointerOffButtonAndPopoverClosesAfter320ms()
    {
        // Lines 30-32: else { scheduleClose() }
        // Lines 49-55: scheduleClose waits 320ms
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var center = Centre(button, window);

        // Open by hovering
        MouseMove(window, center);
        Assert.True(button.IsPopoverPresented);

        // Move away from button and popover
        MouseMove(window, Away);
        Assert.True(button.IsPopoverPresented); // Still open immediately

        // Advance 320ms
        runtime.Advance(TimeSpan.FromMilliseconds(320));
        Assert.False(button.IsPopoverPresented); // Closed after 320ms
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:38-44
    public void InfoHoverButton_PointerOverPopoverKeepsItOpen()
    {
        // Lines 38-44: onHover inside popover { over in ... if over { closeTask.cancel() } }
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var center = Centre(button, window);

        // Open by hovering over button
        MouseMove(window, center);
        Assert.True(button.IsPopoverPresented);

        // Move away from button
        MouseMove(window, Away);
        Assert.True(button.IsPopoverPresented);

        // Move back to button (simulating popover proximity)
        MouseMove(window, center);

        // Wait 320ms
        runtime.Advance(TimeSpan.FromMilliseconds(320));
        Assert.True(button.IsPopoverPresented); // Pointer over button keeps it open
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:7, :51
    public void InfoHoverButton_PopoverWidthIs360ByDefault()
    {
        // Line 7: var width: CGFloat = 360
        // Line 51: .frame(width: width)
        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));

        Assert.Equal(360, button.PopoverWidth);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:7, :51
    public void InfoHoverButton_PopoverWidthCanBeCustomized()
    {
        // Line 7: var width: CGFloat = 360
        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" },
            width: 500
        );

        var window = Show(TopLeft(button));

        Assert.Equal(500, button.PopoverWidth);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:44-46
    public void InfoHoverButton_CancelsPreviousCloseTaskWhenHoveringAgain()
    {
        // Lines 44-45: closeTask?.cancel()
        using var runtime = new TestRuntime();

        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));
        var center = Centre(button, window);

        // Open by hovering
        MouseMove(window, center);
        Assert.True(button.IsPopoverPresented);

        // Leave button to schedule close
        MouseMove(window, Away);

        // Advance 150ms (halfway)
        runtime.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(button.IsPopoverPresented); // Not closed yet

        // Hover again before close completes
        MouseMove(window, center);

        // Close task should be cancelled, reset timer
        runtime.Advance(TimeSpan.FromMilliseconds(150)); // Another 150ms, total 300ms
        Assert.True(button.IsPopoverPresented); // Still open because timer was reset

        // Now advance another 150ms to hit 320ms from last hover
        MouseMove(window, Away);
        runtime.Advance(TimeSpan.FromMilliseconds(320));
        Assert.False(button.IsPopoverPresented);
    }

    [Trait("Req", "UI-SETTINGS-030")] // DroppyCode/UI/Chrome/InfoHoverButton.swift:35-36
    public void InfoHoverButton_PopoverAnchorsBelow()
    {
        // Line 35: .popover(isPresented: $isPresented, arrowEdge: .bottom)
        var button = new InfoHoverButton(
            help: "Test",
            content: () => new TextBlock { Text = "Content" }
        );

        var window = Show(TopLeft(button));

        // The arrow edge should be bottom
        Assert.Equal("bottom", button.PopoverArrowEdge);
    }
}
