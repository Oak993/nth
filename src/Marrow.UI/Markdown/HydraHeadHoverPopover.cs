using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.UI.Markdown;

// Source: DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift (B3 52bc792), :1-90.
// Records: PR-HYDRA-197, UI-HYDRA-035.
// Form (ED-095): NSPopover is Avalonia Popup; a @MainActor class with a static `let shared` becomes
// a sealed class with a static Instance property, main-actor isolated via R-ISO-1 MainActor; a stored
// `Task` becomes TaskHandle (R-TASK-1).

/// <summary>
/// A popover that appears when hovering a head mention in the chat timeline. It shows the head's glyph,
/// name, task, and current status after a 250 millisecond delay. Hovering the same head again while
/// the popover is shown does nothing; moving away cancels a pending card and closes an open one.
/// </summary>
// :4-5 @MainActor final class HydraHeadHoverPopover: NSObject, NSPopoverDelegate
public sealed class HydraHeadHoverPopover
{
    private static HydraHeadHoverPopover? _instance;

    /// <summary>:6 static let shared = HydraHeadHoverPopover()</summary>
    public static HydraHeadHoverPopover Instance => _instance ??= new();

    private readonly Popup _popover; // :8 private let popover = NSPopover()
    private Guid? _shownThreadID; // :9 private var shownID: UUID?
    private TaskHandle? _pending; // :10 private var pending: Task<Void, Never>?
    private bool _presented; // :20,37 shown state across the R-VIEW-9 presentation seam
    private bool IsShown => _presented || _popover.IsOpen;

    /// <summary>The thread ID of the popover currently shown, or null if none.</summary>
    public Guid? ShownThreadID => _shownThreadID;

    private HydraHeadHoverPopover()
    {
        // :12-16 private override init()
        _popover = new Popup();
        // :14 popover.behavior = .applicationDefined
        // :15 popover.animates = true
        _popover.IsLightDismissEnabled = false;
        // :16 popover.delegate = self
        _popover.Closed += (_, _) => PopoverDidClose(null);
    }

    /// <summary>
    /// Show the popover after 250 milliseconds of hovering. If the popover is already shown for this head,
    /// does nothing. If hovering a different head, closes the previous popover and schedules the new one.
    /// :19-39 func show(persona:target:relativeTo:of:)
    /// </summary>
    /// <param name="persona">The head's persona (name, asset, color).</param>
    /// <param name="target">The mention target (thread ID, task, status).</param>
    /// <param name="relativeTo">The rectangle of the head mention in the view.</param>
    /// <param name="of">The view the mention appears in.</param>
    public void Show(HydraPersona persona, HydraMentionTarget target, Rect relativeTo, Visual of)
    {
        // :20 if popover.isShown, shownID == target.threadID { return }
        if (IsShown && _shownThreadID == target.ThreadID)
        {
            return;
        }

        // :21 pending?.cancel()
        _pending?.Cancel();

        // :22-38 pending = Task { [weak self] in ... }
        _pending = MainActor.Spawn(async () =>
        {
            // :23 try? await Task.sleep(for: .milliseconds(250))
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250));

            // :24 guard let self, !Task.isCancelled else { return }
            if (SwiftTask.IsCancelled)
            {
                return;
            }

            // :25 if self.popover.isShown { self.popover.close() }
            if (IsShown)
            {
                _presented = false;
                Presentation.Presenter.Current.SetPresented(_popover, false);
            }

            // :26-29 // Measured with the card's own width pinned...
            // :30 let width = HydraHeadHoverCard.width
            var width = HydraHeadHoverCard.CardWidth;

            // :31 let content = HydraHeadHoverCard(persona: persona, target: target)
            var content = new HydraHeadHoverCard(persona, target);

            // :32 var size = NSHostingView(rootView: content.frame(width: width)).intrinsicContentSize
            // In Avalonia, we measure by setting the width constraint and calling Measure
            content.Width = width;
            content.Measure(new(width, double.PositiveInfinity));
            var measuredSize = content.DesiredSize;

            // :33 if size.width < width { size.width = width }
            if (measuredSize.Width < width)
            {
                measuredSize = new Size(width, measuredSize.Height);
            }

            // :34 if size.height <= 0 { size.height = 78 }
            if (measuredSize.Height <= 0)
            {
                measuredSize = new Size(measuredSize.Width, 78);
            }

            // :35 self.popover.setFixedContent(content, size: size)
            _popover.SetFixedContent(content, measuredSize);

            // :36 self.shownID = target.threadID
            _shownThreadID = target.ThreadID;

            // :37 self.popover.show(relativeTo: rect, of: view, preferredEdge: .maxY)
            // Avalonia Popup positioning: set placement target to the view, with Bottom placement
            // to show the popover below the mention (equivalent to preferredEdge: .maxY)
            _popover.Placement = PlacementMode.Bottom;
            _popover.PlacementTarget = of as Control;
            _presented = true; // :37
            Presentation.Presenter.Current.SetPresented(_popover, true);
        });
    }

    /// <summary>
    /// Hide the popover and cancel any pending show task.
    /// :41-46 func hide()
    /// </summary>
    public void Hide()
    {
        // :42 pending?.cancel()
        _pending?.Cancel();

        // :43 pending = nil
        _pending = null;

        // :44 if popover.isShown { popover.performClose(nil) }
        if (IsShown)
        {
            _presented = false;
            Presentation.Presenter.Current.SetPresented(_popover, false);
        }

        // :45 shownID = nil
        _shownThreadID = null;
        _presented = false;
    }

    /// <summary>
    /// Called when the popover closes (through user action or other means).
    /// :48-50 func popoverDidClose(_ notification: Notification)
    /// </summary>
    public void PopoverDidClose(object? notification)
    {
        // :49 shownID = nil
        _shownThreadID = null;
        _presented = false;
    }
}

/// <summary>
/// The card shown in the popover: a 300-point wide container showing the head's glyph, name, task, and status.
/// :53 private struct HydraHeadHoverCard: View
/// </summary>
internal sealed class HydraHeadHoverCard : Decorator
{
    /// <summary>
    /// The card's width, and the popover's: a narrower frame breaks a task title after two words with the
    /// card's own width left unused.
    /// :56 static let width: CGFloat = 300
    /// </summary>
    public const double CardWidth = 300;

    // :58-59 let persona: HydraPersona, let target: HydraMentionTarget
    public HydraPersona Persona { get; }
    public HydraMentionTarget Target { get; }

    /// <summary>
    /// :61-80 var body: some View
    /// </summary>
    public HydraHeadHoverCard(HydraPersona persona, HydraMentionTarget target)
    {
        Persona = persona;
        Target = target;

        // HStack(spacing: 10)
        var hstack = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), // :62 spacing 10; :76 flexible text column
        };

        // HydraGlyph(persona: persona, size: 28, isRunning: target.status == .running, status: target.status)
        var glyph = new HydraGlyph(
            persona: persona,
            size: 28, // :63 size: 28
            isRunning: target.Status == HydraHeadInfo.Status.Running, // :63 isRunning: target.status == .running
            status: target.Status // :63 status: target.status
        );
        hstack.Children.Add(glyph);

        // VStack(alignment: .leading, spacing: 2)
        var vstack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 2, // :64 spacing: 2
        };

        // Text(verbatim: persona.name)
        //     .font(.system(.body, weight: .semibold))
        //     .foregroundStyle(persona.color)
        var nameText = new TextBlock
        {
            Text = persona.Name, // :65 Text(verbatim: persona.name)
        };
        var bodyFont = SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold); // :66 .font(.system(.body, weight: .semibold))
        bodyFont.Apply(nameText);
        // :67 .foregroundStyle(persona.color) - convert persona color tuple to SwiftColor then to brush
        var (red, green, blue, alpha) = persona.Color;
        var personaSwiftColor = new SwiftColor(red, green, blue, alpha);
        nameText.Foreground = personaSwiftColor.Brush(nameText);
        vstack.Children.Add(nameText);

        // Text(verbatim: target.task)
        //     .font(.system(.callout))
        //     .foregroundStyle(Chrome.primaryText)
        //     .lineLimit(3)
        var taskText = new TextBlock
        {
            Text = target.Task, // :68 Text(verbatim: target.task)
            MaxLines = 3, // :71 .lineLimit(3)
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var calloutFont = SwiftFont.System(SwiftFont.TextStyle.Callout); // :69 .font(.system(.callout))
        calloutFont.Apply(taskText);
        taskText.Foreground = UI.Chrome.Chrome.PrimaryText.Brush(taskText); // :70 .foregroundStyle(Chrome.primaryText)
        vstack.Children.Add(taskText);

        // Text(verbatim: statusLine)
        //     .font(.system(.subheadline))
        //     .foregroundStyle(Chrome.secondaryText)
        var statusLine = StatusLine(target.Status); // :72 Text(verbatim: statusLine)
        var statusText = new TextBlock
        {
            Text = statusLine,
        };
        var subheadlineFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline); // :73 .font(.system(.subheadline))
        subheadlineFont.Apply(statusText);
        statusText.Foreground = UI.Chrome.Chrome.SecondaryText.Brush(statusText); // :74 .foregroundStyle(Chrome.secondaryText)
        vstack.Children.Add(statusText);

        // .frame(maxWidth: .infinity, alignment: .leading)
        vstack.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(vstack, 2); // :76 bounded to the remaining width, enabling :71's three-line limit
        hstack.Children.Add(vstack);

        // .padding(12)
        var padded = new Border
        {
            Child = hstack,
            Padding = new Thickness(12), // :78 .padding(12)
        };

        // .frame(width: Self.width, alignment: .leading)
        padded.Width = CardWidth; // :79 .frame(width: Self.width, alignment: .leading)
        padded.HorizontalAlignment = HorizontalAlignment.Left;

        Child = padded;
    }

    /// <summary>
    /// Compute the status line string based on the head's current status.
    /// :82-89 private var statusLine: String { switch target.status { ... } }
    /// </summary>
    public static string StatusLine(HydraHeadInfo.Status status)
    {
        return status switch
        {
            HydraHeadInfo.Status.Running => "Working…", // :84 "Working…" U+2026 HORIZONTAL ELLIPSIS
            HydraHeadInfo.Status.Completed => "Done · click to open the chat", // :85 "Done · click to open the chat" U+00B7 MIDDLE DOT
            HydraHeadInfo.Status.Failed => "Failed · click to open the chat", // :86 "Failed · click to open the chat" U+00B7 MIDDLE DOT
            HydraHeadInfo.Status.Stopped => "Stopped · click to open the chat", // :87 "Stopped · click to open the chat" U+00B7 MIDDLE DOT
            _ => throw new UnreachableException(),
        };
    }
}
