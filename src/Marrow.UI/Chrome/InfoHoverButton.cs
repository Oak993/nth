using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/InfoHoverButton.swift (B3 52bc792), :5-56.
// A button displaying an info mark that opens an explanatory popover on hover, keeping it open while the
// pointer is over either the button or popover, and closing 320ms after the pointer leaves both.
// Port shape (R-VIEW-1, R-VIEW-2, R-VIEW-9): a SwiftUI View is a Decorator; the info mark is a SwiftImage;
// @ViewBuilder parameter content becomes Func<Control> (ED-095). @State properties (isHovering, isOverPopover,
// isPresented, closeTask) become properties of a ViewState class inheriting MainActorModel; the close task is
// a TaskHandle (R-TASK-1, ED-066). Text color (line 18) derives from Chrome.SecondaryText (label at 0.7 opacity;
// ED-062) at 0.6, rising to 1.0 on hover.
public sealed class InfoHoverButton : Decorator
{
    // :6 let help: String
    private readonly string help;

    // :7 var width: CGFloat = 360
    private readonly double width;

    // :8 @ViewBuilder let content: () -> Content
    private readonly Func<Control> content;

    // :10-13 @State properties
    private sealed class ViewState : MainActorModel
    {
        // :10 @State private var isHovering = false
        private bool isHovering;
        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

        // :11 @State private var isOverPopover = false
        private bool isOverPopover;
        public bool IsOverPopover { get => Read(isOverPopover); set => Set(ref isOverPopover, value); }

        // :12 @State private var isPresented = false
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }

        // :13 @State private var closeTask: Task<Void, Never>?
        private TaskHandle? closeTask;
        public TaskHandle? CloseTask { get => Read(closeTask); set => Set(ref closeTask, value); }
    }

    // State exposed for testing
    public bool IsPopoverPresented { get; private set; }
    public double PopoverWidth { get; }
    public string PopoverArrowEdge => "bottom"; // :35 arrowEdge: .bottom

    // Constructor with parameter order: help (required), content (required, ViewBuilder), width (optional, default 360)
    // (ED-095: @ViewBuilder parameter content is required; in C# positional order after required help, optional width follows)
    public InfoHoverButton(string help, Func<Control> content, double width = 360)
    {
        this.help = help;
        this.width = width;
        this.content = content;
        PopoverWidth = width;

        var state = new ViewState();

        // :16-24 Body starts with Image(systemName: "info.circle") with modifiers
        var image = SwiftImage.SystemName("info.circle"); // :16

        // :17 .font(.system(.callout, weight: .medium))
        var font = SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium);
        SwiftImage.SetFont(image, font);

        // :18 .foregroundStyle(Chrome.secondaryText.opacity(isHovering ? 1 : 0.6))
        // Initial state: idle at 0.6 opacity
        var foreground = new AnimatedColor(image);
        image.SetValue(TextElement.ForegroundProperty, foreground.Brush);

        // Track isHovering to update opacity
        image.Track(() => state.IsHovering, hovering =>
        {
            var opacity = hovering ? 1.0 : 0.6;
            foreground.Set(Chrome.SecondaryText.Opacity(opacity), Transaction.Current);
        });

        // :19 .frame(width: 20, height: 20)
        image.Width = 20;
        image.Height = 20;

        // :21 .focusable(false) applies to the interactive control as well as the image.

        // :22 .help(help)
        ToolTip.SetTip(image, help);

        // :23 .accessibilityLabel(Text(help))
        AutomationProperties.SetName(image, help);

        // :24 .accessibilityAddTraits(.isButton) — handled by wrapping in Button via PlainControls.Button

        // :25-32 .onHover { over in isHovering = over; ... }
        image.OnHover(hovering =>
        {
            state.IsHovering = hovering;
            if (hovering)
            {
                // :27-28 if over { closeTask?.cancel(); isPresented = true }
                state.CloseTask?.Cancel();
                state.IsPresented = true;
            }
            else
            {
                // :30-31 else { scheduleClose() }
                ScheduleClose(state);
            }
        });

        // :34 .onTapGesture { isPresented.toggle() }
        var button = PlainControls.Button(image, () => state.IsPresented = !state.IsPresented);
        button.Focusable = false; // :21
        Avalonia.Input.KeyboardNavigation.SetIsTabStop(button, false); // :21

        // :35-46 .popover(isPresented: $isPresented, arrowEdge: .bottom) { ... }
        var popover = PlainControls.Popover(
            button,
            new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
            () => BuildPopoverContent(state));

        // Track IsPopoverPresented for testing
        popover.Track(() => state.IsPresented, presented => IsPopoverPresented = presented);

        // Build the body: button with popover
        Child = new Panel { Children = { button, popover } };
    }

    private Control BuildPopoverContent(ViewState state)
    {
        // :37-46 The content wrapped with frame and hover tracking
        var body = content(); // :37 content()

        // :37 .frame(width: width)
        var container = new Border { Width = width, Child = body };

        // :38-44 .onHover { over in isOverPopover = over; ... }
        container.OnHover(hovering =>
        {
            state.IsOverPopover = hovering;
            if (hovering)
            {
                // :40-41 if over { closeTask?.cancel() }
                state.CloseTask?.Cancel();
            }
            else
            {
                // :42-43 else { scheduleClose() }
                ScheduleClose(state);
            }
        });

        return container;
    }

    // :49-55 private func scheduleClose()
    private static void ScheduleClose(ViewState state)
    {
        // :50 closeTask?.cancel()
        state.CloseTask?.Cancel();

        // :51-55 closeTask = Task { try? await Task.sleep(...); guard ...; isPresented = false }
        state.CloseTask = MainActor.Spawn(async () =>
        {
            // :52 try? await Task.sleep(for: .milliseconds(320))
            var slept = await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(320));

            // :53 guard !Task.isCancelled, !isHovering, !isOverPopover else { return }
            if (!slept || SwiftTask.IsCancelled || state.IsHovering || state.IsOverPopover)
                return;

            // :54 isPresented = false
            state.IsPresented = false;
        });
    }
}
