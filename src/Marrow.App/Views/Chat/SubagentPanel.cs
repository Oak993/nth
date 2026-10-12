using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Window;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B2 1fbf81e), :1-738. B3 (52bc792) re-verification is
// partial: only PanelDragHandle below (B3 :640-666) has been re-pinned and re-cited against the B3 tree (unit
// App.Views.Chat.SubagentPanel, plan section 2.B row L9/20, B3_PLAN.md:696); every other member's citations
// still point at the B2 tree pending its own re-verification pass.
// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B3 52bc792), :268-327, :504-547 (sweep C).

/// <summary>:4-7 <c>enum FloatingPanelID: Hashable</c>. An enum with an associated value is a record union.</summary>
public abstract record FloatingPanelID
{
    private FloatingPanelID() { }

    public sealed record Subagent : FloatingPanelID; // :6
    public sealed record Hydra : FloatingPanelID; // :6
    public sealed record Popped : FloatingPanelID; // :6
    public sealed record Usage : FloatingPanelID; // :6
    public sealed record Auto(Guid Value) : FloatingPanelID; // :6
}

/// <summary>:9-12 <c>enum PanelDockSide: Equatable</c>.</summary>
public enum PanelDockSide
{
    Leading, // :11
    Trailing, // :11
}

/// <summary>:14-45 <c>enum PanelDockCorner: String, Equatable</c>.</summary>
public enum PanelDockCorner
{
    [RawValue("topLeading")] TopLeading, // :16
    [RawValue("topTrailing")] TopTrailing, // :16
    [RawValue("bottomLeading")] BottomLeading, // :16
    [RawValue("bottomTrailing")] BottomTrailing, // :16
}

/// <summary>:18-44 the members declared on <c>PanelDockCorner</c>.</summary>
public static class PanelDockCornerExtensions
{
    extension(PanelDockCorner corner)
    {
        // :18-23
        public PanelDockSide Side => corner switch
        {
            PanelDockCorner.TopLeading or PanelDockCorner.BottomLeading => PanelDockSide.Leading,
            PanelDockCorner.TopTrailing or PanelDockCorner.BottomTrailing => PanelDockSide.Trailing,
            _ => throw new UnreachableException(),
        };

        // :25
        public bool IsTop => corner is PanelDockCorner.TopLeading or PanelDockCorner.TopTrailing;

        // :37-44
        public PanelDockCorner AcrossTheColumn => corner switch
        {
            PanelDockCorner.TopLeading => PanelDockCorner.TopTrailing,
            PanelDockCorner.TopTrailing => PanelDockCorner.TopLeading,
            PanelDockCorner.BottomLeading => PanelDockCorner.BottomTrailing,
            PanelDockCorner.BottomTrailing => PanelDockCorner.BottomLeading,
            _ => throw new UnreachableException(),
        };
    }

    extension(PanelDockCorner)
    {
        // :27-34
        public static PanelDockCorner Make(PanelDockSide side, bool isTop) => (side, isTop) switch
        {
            (PanelDockSide.Leading, true) => PanelDockCorner.TopLeading,
            (PanelDockSide.Trailing, true) => PanelDockCorner.TopTrailing,
            (PanelDockSide.Leading, false) => PanelDockCorner.BottomLeading,
            (PanelDockSide.Trailing, false) => PanelDockCorner.BottomTrailing,
            _ => throw new UnreachableException(),
        };
    }
}

/// <summary>:47-52 <c>struct PanelReserve: Equatable</c>.</summary>
public sealed record PanelReserve
{
    public double Leading { get; init; } = 0; // :50
    public double Trailing { get; init; } = 0; // :51
}

/// <summary>:54-97 the four docked spots for the pane as it is.</summary>
public sealed record PanelDocks
{
    public Point TopLeading { get; init; } // :56
    public Point TopTrailing { get; init; } // :57
    public Point BottomLeading { get; init; } // :58
    public Point BottomTrailing { get; init; } // :59

    // :61-66
    public PanelDocks(SubagentPanelLayout layout, PanelReserve reserve)
    {
        TopLeading = layout.DockedOrigin(PanelDockCorner.TopLeading, reserve);
        TopTrailing = layout.DockedOrigin(PanelDockCorner.TopTrailing, reserve);
        BottomLeading = layout.DockedOrigin(PanelDockCorner.BottomLeading, reserve);
        BottomTrailing = layout.DockedOrigin(PanelDockCorner.BottomTrailing, reserve);
    }

    // :68-75
    public Point this[PanelDockCorner corner] => corner switch
    {
        PanelDockCorner.TopLeading => TopLeading,
        PanelDockCorner.TopTrailing => TopTrailing,
        PanelDockCorner.BottomLeading => BottomLeading,
        PanelDockCorner.BottomTrailing => BottomTrailing,
        _ => throw new UnreachableException(),
    };

    // :80-85
    public Point Stacked(PanelDockCorner corner, long below, long slots, SubagentPanelLayout layout)
    {
        var step = slots > 1 ? (BottomLeading.Y - TopLeading.Y) / (double)(slots - 1) : 0;
        var index = corner.IsTop ? below : slots - 1 - below;
        var y = TopLeading.Y + Math.Max(0, index) * step;
        return new Point(this[corner].X, y);
    }

    // :90-96
    public static (PanelDockCorner Corner, long Below) Corner(long forSlot, long of, PanelDockSide side)
    {
        if (forSlot <= (of - 1) / 2)
            return (PanelDockCorner.Make(side, isTop: true), forSlot);
        return (PanelDockCorner.Make(side, isTop: false), of - 1 - forSlot);
    }
}

/// <summary>:99-242 <c>struct SubagentPanelLayout: Equatable</c>.</summary>
public sealed record SubagentPanelLayout
{
    public const double Width = 400; // :105
    public const double MinWidth = 300; // :106
    public const double MinHeight = 220; // :107
    public const double Gap = 12; // :109
    public const double SideMargin = 20; // :111
    public const double BottomMargin = 14; // :112
    public const double MinComposerWidth = 360; // :114
    public const double ComposerMaxWidth = 820; // :115
    public const double CornerSlack = 56; // :118

    public required Size Pane { get; init; } // :120
    public required double ComposerAreaHeight { get; init; } // :122
    public long StackDepth { get; init; } = 1; // :126
    public Size? Preferred { get; init; } = null; // :130
    public bool Compact { get; init; } = false; // :134

    public const double CompactHeight = 280; // :136

    // :138-140
    public double PanelWidth =>
        Math.Min(Math.Max(Preferred?.Width ?? Width, MinWidth), Math.Max(MinWidth, Pane.Width - 2 * SideMargin));

    // :143-145
    public bool SitsBesideComposer =>
        Pane.Width - 2 * SideMargin - PanelWidth - Gap >= MinComposerWidth;

    // :149-151
    public bool FitsBothSides =>
        Pane.Width - 2 * SideMargin - 2 * (PanelWidth + Gap) >= MinComposerWidth;

    // :154-156
    public double ComposerReserve => SitsBesideComposer ? PanelWidth + Gap : 0;

    // :160-163
    public double VerticalRoom
    {
        get
        {
            var below = SitsBesideComposer ? BottomMargin : ComposerAreaHeight + Gap;
            return Pane.Height - Chrome.ContentTopInset - below;
        }
    }

    // :165-170
    public double PanelHeight
    {
        get
        {
            var ideal = Compact
                ? CompactHeight
                : Preferred?.Height ?? Math.Min(520, Math.Max(300, Pane.Height * 0.5));
            var depth = Math.Max(1, StackDepth);
            var share = (VerticalRoom - (depth - 1) * Gap) / depth;
            return Math.Max(MinHeight, Math.Min(ideal, share));
        }
    }

    // :172
    public Size PanelSize => new(PanelWidth, PanelHeight);

    // :177-181
    public Size Fitted(Size size)
    {
        var trial = this with { Preferred = new Size(size.Width, double.MaxValue) };
        return new Size(trial.PanelWidth, Math.Max(MinHeight, Math.Min(size.Height, trial.PanelHeight)));
    }

    // :187-201
    public Point DockedOrigin(PanelDockCorner corner, PanelReserve reserve)
    {
        var y = corner.IsTop
            ? Chrome.ContentTopInset
            : Pane.Height - (SitsBesideComposer ? BottomMargin : ComposerAreaHeight + Gap) - PanelHeight;

        if (SitsBesideComposer)
        {
            var row = Pane.Width - reserve.Leading - reserve.Trailing;
            var composerWidth = Math.Min(ComposerMaxWidth, row - 2 * SideMargin);
            var composerLeft = reserve.Leading + (row - composerWidth) / 2;
            return corner.Side switch
            {
                PanelDockSide.Leading => new Point(composerLeft - Gap - PanelWidth, y),
                PanelDockSide.Trailing => new Point(composerLeft + composerWidth + Gap, y),
                _ => throw new UnreachableException(),
            };
        }

        return new Point(
            corner.Side == PanelDockSide.Leading ? SideMargin : Pane.Width - SideMargin - PanelWidth,
            y);
    }

    // :206-225
    public PanelDockCorner DockCorner(Point nearest, PanelDockCorner keeping, PanelDocks docks)
    {
        var centre = new Point(nearest.X + PanelWidth / 2, nearest.Y + PanelHeight / 2);
        var middle = new Point(Pane.Width / 2, (docks.TopLeading.Y + docks.BottomLeading.Y + PanelHeight) / 2);
        var side = (centre.X - middle.X) switch
        {
            < -CornerSlack => PanelDockSide.Leading,
            >= CornerSlack => PanelDockSide.Trailing,
            _ => keeping.Side,
        };
        var isTop = (centre.Y - middle.Y) switch
        {
            < -CornerSlack => true,
            >= CornerSlack => false,
            _ => keeping.IsTop,
        };
        return PanelDockCorner.Make(side, isTop);
    }

    // :229-232
    public long SlotIndex(Point at, long slots, PanelDocks docks)
    {
        var step = slots > 1 ? (docks.BottomLeading.Y - docks.TopLeading.Y) / (double)(slots - 1) : 0;
        if (step <= 0) return 0;
        var rounded = Math.Round((at.Y - docks.TopLeading.Y) / step, MidpointRounding.AwayFromZero);
        return Math.Min(slots - 1, Math.Max(0, (long)rounded));
    }

    // :235-241
    public Point Clamped(Point origin)
    {
        const double inset = 8;
        return new Point(
            Math.Min(Math.Max(origin.X, inset), Math.Max(inset, Pane.Width - PanelWidth - inset)),
            Math.Min(Math.Max(origin.Y, inset), Math.Max(inset, Pane.Height - PanelHeight - inset)));
    }
}

/// <summary>:244-357 <c>struct SubagentPanel: View</c>.</summary>
public sealed class SubagentPanel : Decorator
{
    private const double CornerRadius = 22; // :265

    // :266
    private static double HandleHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    // :251-258, :268-356
    public SubagentPanel(
        ChatThread thread,
        Size size,
        string? workingDirectory,
        string? projectName,
        Action<Size> onDrag,
        Action onDragEnd,
        Action close)
        : this(thread, size, workingDirectory, projectName, onDrag, onDragEnd, close,
            state => new JumpToLatestButton(state)) { }

    // :268-327. Test-only dependency injection keeps this unit testable while ChatView's jump control is a stub.
    internal SubagentPanel(
        ChatThread thread, Size size, string? workingDirectory, string? projectName,
        Action<Size> onDrag, Action onDragEnd, Action close, Func<TimelineScrollState, Control> jumpButton)
    {
        var model = WindowManager.Shared.Model;
        var runtime = model.Runtime(thread.Id);
        var shape = new Border
        {
            Width = size.Width,
            Height = size.Height,
            CornerRadius = new CornerRadius(CornerRadius),
            ClipToBounds = true,
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        var scrollChrome = new ChromeScrollModel();
        var scrollState = new TimelineScrollState();
        Control MakeTranscript(bool compact)
        {
            Control transcript = compact
                ? new HydraHeadProgress(thread, runtime)
                : new ThreadTimeline(
                runtime,
                scrollChrome,
                scrollState,
                projectName,
                workingDirectory,
                supportsRewind: false,
                columnHeight: size.Height,
                showsMinimap: false);
            transcript.Margin = new Thickness(16, HandleHeight + 8, 16, 12);
            return transcript;
        }
        var compactBody = thread.IsHydraHead && !model.Settings.HydraShowsHeadDetails;
        var transcriptHost = new Border { Child = MakeTranscript(compactBody) };
        // :271-291. Observe just this conditional body; pointer placement never rebuilds its content.
        transcriptHost.Track(() => thread.IsHydraHead && !model.Settings.HydraShowsHeadDetails, compact =>
        {
            if (compact == compactBody) return;
            compactBody = compact;
            transcriptHost.Child = MakeTranscript(compact);
        });
        Grid.SetRow(transcriptHost, 0);
        content.Children.Add(transcriptHost);

        var composer = new ComposerArea(
            runtime,
            workingDirectory,
            compactModelChip: true,
            takesFocusOnAppear: false,
            showsChanges: false);
        var composerContainer = new Grid();
        composerContainer.Children.Add(composer);
        // :292-297 `.overlay(alignment: .top)`: the zero-height button sits on the composer's top edge, not at its vertical middle (a Stretch
        // control whose own Height is smaller than its slot is centred), as ChatView.cs does for the main composer.
        var jump = jumpButton(scrollState);
        jump.VerticalAlignment = VerticalAlignment.Top;
        composerContainer.Children.Add(jump);
        Grid.SetRow(composerContainer, 0);
        composerContainer.VerticalAlignment = VerticalAlignment.Bottom;
        content.Children.Add(composerContainer);

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var handle = new PanelDragHandle(onDrag, onDragEnd)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = HandleHeight,
        };
        ToolTip.SetTip(handle, "Drag to move"); // :308
        AutomationProperties.SetName(handle, "Drag to move"); // :309
        strip.Children.Add(handle);

        var working = new SubagentWorkingBox(thread.Title, runtime.IsRunning)
        {
            Margin = new Thickness(0, Chrome.ChromeTopPadding, 0, 0),
        };
        Grid.SetColumn(working, 1);
        strip.Children.Add(working);
        working.Track(() => runtime.IsRunning, running => working.Update(thread.Title, running));

        var closeState = new ClosePanelPresentationState();
        var closeButton = new ChromeCircleButton(
            "xmark",
            runtime.IsRunning ? "Close this chat and stop its turn" : "Close this chat",
            () =>
            {
                if (runtime.IsRunning) closeState.IsConfirmingClose = true;
                else close();
            })
        {
            Margin = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
        };
        closeButton.Track(
            () => runtime.IsRunning,
            running =>
            {
                var help = running ? "Close this chat and stop its turn" : "Close this chat";
                ToolTip.SetTip(closeButton, help);
                // ChromeCircleButton puts its initial help on the actual button (:433-434).
                foreach (var button in Avalonia.LogicalTree.LogicalExtensions.GetSelfAndLogicalDescendants(closeButton).OfType<Button>())
                {
                    ToolTip.SetTip(button, help);
                    AutomationProperties.SetName(button, help);
                }
            });
        Grid.SetColumn(closeButton, 2);
        strip.Children.Add(closeButton);

        var closePopover = PlainControls.Popover(
            closeButton,
            new Binding<bool>(() => closeState.IsConfirmingClose, value => closeState.IsConfirmingClose = value),
            () =>
            {
                var confirmation = new ClosePanelPopover(thread.Title, close);
                Popovers.SetClosePopover(confirmation, () => closeState.IsConfirmingClose = false);
                return confirmation;
            },
            PlacementMode.Bottom); // :323 .popover(isPresented: $isConfirmingClose, arrowEdge: .bottom)
        Grid.SetColumn(closePopover, 2);
        strip.Children.Add(closePopover);

        strip.VerticalAlignment = VerticalAlignment.Top; // :301-327
        content.Children.Add(strip);

        shape.Child = content;
        SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous))); // :339
        SwiftGlass.SetCastsOwnShadow(shape, true); // OD-0147: the scrim below casts the panel's one shadow
        // :347-355 The scrim sits under the clipped glass, not around it, and carries the panel's one shadow: a plain filled
        // shape, so its shadow is drawn once and kept (a shadow on the whole panel was blurred again with every streamed token).
        // Unclipped, so the shadow reaches past the panel's edge.
        var scrim = new Border
        {
            Width = size.Width,
            Height = size.Height,
            CornerRadius = new CornerRadius(CornerRadius),
        };
        // :354 the shadow is the filled scrim's own coverage, cast at its fill's alpha and drawn under it, through it (ED-358).
        var shadow = new SwiftShadow.Caster(scrim, Colors.Black, radius: 28, y: 10);

        void UpdateSurface()
        {
            var isDark = shape.ActualThemeVariant == ThemeVariant.Dark;
            shape.Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(shape);
            scrim.Background = (isDark ? SwiftColor.Black : SwiftColor.White).Opacity(isDark ? 0.3 : 0.34).Brush(scrim);
            shadow.Set(Color.FromArgb(isDark ? (byte)255 : (byte)166, 0, 0, 0), radius: 28, y: 10); // :354 (sigma 28, ED-327), cast by the 0.3 / 0.34 fill
        }

        UpdateSurface();
        shape.ActualThemeVariantChanged += (_, _) => UpdateSurface();
        Child = new Grid { Children = { shadow, scrim, shape } }; // :345 .clipShape(shape), then :349 .background { scrim } (its shadow under it)
    }
}

/// <summary>:262-263 <c>@State private var isConfirmingClose = false</c>.</summary>
internal sealed class ClosePanelPresentationState : MainActorModel
{
    private bool isConfirmingClose;

    public bool IsConfirmingClose
    {
        get => Read(isConfirmingClose);
        set => Set(ref isConfirmingClose, value);
    }
}

/// <summary>:359-413 <c>private struct SubagentWorkingBox: View</c>.</summary>
internal sealed class SubagentWorkingBox : Decorator
{
    private readonly Marrow.UI.Port.Nth.NthSpinner spinner = new() { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }; // OD-0136 amendment 2, step 1 B
    private readonly TextBlock text = new();
    private string name = "Helper";
    private bool isRunning;
    private bool isSending;
    private int sendingGeneration;

    // :367-368, :379-412
    public SubagentWorkingBox(string title, bool isRunning)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = Chrome.CapsuleContentHeight,
        };
        row.Children.Add(spinner);
        text.MaxLines = 1;
        text.VerticalAlignment = VerticalAlignment.Center;
        SwiftFont.Callout.Apply(text); // B1:DroppyCode/UI/Common/ChatZoom.swift:52, ED-059
        text.Foreground = Chrome.SecondaryText.Brush(text); // ED-062
        row.Children.Add(text);

        var capsule = new Border
        {
            Child = row,
            Height = Chrome.CapsuleHeight,
            CornerRadius = new CornerRadius(Chrome.CapsuleHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        }.ChromeGlassCapsule();
        Child = capsule;

        name = Name(title);
        this.isRunning = isRunning;
        Refresh();
    }

    // :374-377
    private static new string Name(string title)
    {
        var shortTitle = TextCleanup.SingleLine(title, 24).Trimmed();
        return shortTitle.Length == 0 ? "Helper" : shortTitle;
    }

    // :381-398, :403-410
    public void Update(string title, bool running)
    {
        var wasRunning = isRunning;
        name = Name(title);
        isRunning = running;
        if (running && !wasRunning && !isSending)
        {
            isSending = true;
            var generation = ++sendingGeneration;
            MainActor.Spawn(async () =>
            {
                var completed = await SwiftTask.TrySleep(TimeSpan.FromSeconds(0.9));
                if (!completed || generation != sendingGeneration) return;
                isSending = false;
                Refresh();
            });
        }

        Refresh();
    }

    // :379-402
    private void Refresh()
    {
        var visible = isRunning || isSending;
        IsVisible = visible;
        if (!visible) return;
        text.Text = isSending && !isRunning ? "Sending\u2026" : $"{name} working"; // U+2026 HORIZONTAL ELLIPSIS
        AutomationProperties.SetName(this, isSending && !isRunning ? "Sending" : $"{name} working");
    }
}

/// <summary>:415-432 <c>private struct ClosePanelPopover: View</c>.</summary>
internal sealed class ClosePanelPopover : Decorator
{
    // :419-420, :422-431
    public ClosePanelPopover(string title, Action onClose)
    {
        var menu = new PopoverMenu(() =>
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(new PopoverSectionHeader("Stop this helper?"));
            stack.Children.Add(new PopoverNote(
                $"\U0000201C{title}\U0000201D is still working. Closing the panel stops its turn. What it has done so far stays, and its chat moves to the sidebar under this one."));
            stack.Children.Add(new PopoverDivider());
            stack.Children.Add(new PopoverItem("Close and stop", onClose, symbol: "stop.circle", isDestructive: true));
            stack.Children.Add(new PopoverItem("Keep it working", () => { }, symbol: "arrow.uturn.backward"));
            return stack;
        });
        menu.Width = 300;
        Child = menu;
    }
}

/// <summary>:434-496 <c>@Observable @MainActor final class PanelDragState</c>.</summary>
public sealed class PanelDragState : MainActorModel
{
    private Point? position;
    private Point? grab;
    private Vector velocity;
    private ContinuousInstant lastMove;

    // :441
    public Point? Position { get => Read(position); private set => Set(ref position, value); }

    private const double FlickCarry = 0.12; // :450
    private const double RestBeforeDrop = 0.08; // :452

    // :456-474
    public Point Move(Size by, Point from, SubagentPanelLayout @in)
    {
        var start = grab ?? from;
        grab = start;
        var next = @in.Clamped(new Point(start.X + by.Width, start.Y + by.Height));
        var now = SwiftClock.Now;
        if (Position is { } oldPosition)
        {
            var elapsed = (now - lastMove).TotalSeconds;
            if (elapsed >= 0.004)
            {
                var instant = new Vector((next.X - oldPosition.X) / elapsed, (next.Y - oldPosition.Y) / elapsed);
                velocity = new Vector(velocity.X * 0.4 + instant.X * 0.6, velocity.Y * 0.4 + instant.Y * 0.6);
            }
        }
        else
        {
            velocity = default;
        }

        lastMove = now;
        Position = next;
        return next;
    }

    // :478-482
    public void Reclamp(SubagentPanelLayout @in)
    {
        if (Position is not { } current) return;
        var next = @in.Clamped(current);
        if (next != current) Position = next;
    }

    // :486-495
    public Point? Release()
    {
        var current = Position;
        try
        {
            if (current is null) return null;
            if ((SwiftClock.Now - lastMove).TotalSeconds >= RestBeforeDrop) return current;
            return new Point(current.Value.X + velocity.X * FlickCarry, current.Value.Y + velocity.Y * FlickCarry);
        }
        finally
        {
            Position = null;
            grab = null;
            velocity = default;
        }
    }
}

/// <summary>:498-525 <c>struct PlacedPanel&lt;Content: View&gt;: View</c>.</summary>
public sealed class PlacedPanel : Decorator
{
    private readonly PanelPlacementView placement;

    // :505-514, :516-524
    public PlacedPanel(
        PanelDragState drag,
        Point rest,
        Size size,
        Control content,
        PanelResize? resize = null,
        bool isResizing = false)
    {
        content.Width = size.Width;
        content.Height = size.Height;

        Control placedContent = content;
        if (resize is not null)
        {
            placedContent = new Panel
            {
                Children =
                {
                    content,
                    new PanelResizeGrips(resize),
                },
            };
            placedContent.Width = size.Width;
            placedContent.Height = size.Height;
        }

        placement = new PanelPlacementView(drag, rest, size, placedContent, isResizing, content);
        Child = placement;
    }

    // :535-546. Re-evaluate the Swift view's immutable placement inputs without replacing its conversation.
    public void UpdatePlacement(Point rest, Size size, bool isResizing = false) => placement.Update(rest, size, isResizing);

    // :541-545 `.frame(width:height:)` frames the panel's surface too, so the surface takes each presented size of a settle (a
    // frame that glides under panelSlide), not the placement's final size ahead of it. The owner sizes whatever it mounted inside.
    internal Action<Size>? PresentSurface
    {
        get => placement.PresentSurface;
        set => placement.PresentSurface = value;
    }

    // The surface just mounted (or swapped in) takes the size the placement is presenting now.
    internal void PresentSurfaceNow() => placement.PresentSurfaceNow();
}

/// <summary>:527-547 <c>private struct PanelPlacement: ViewModifier</c>.</summary>
internal sealed record PanelPlacement(PanelDragState Drag, Point Rest, Size Size, bool IsResizing)
{
    // :537-546
    public Control Body(Control content)
        => new PanelPlacementView(Drag, Rest, Size, content, IsResizing);
}

// :530-547. A placement modifier owns only offset and size; content survives every pointer/frame update.
internal sealed class PanelPlacementView : Border
{
    private readonly PanelDragState drag;
    private readonly Control frameContent;
    private readonly IGeniePlatform motionPlatform = Chrome.ReduceMotionPlatform.Current;
    private readonly TimeProvider clock = SwiftClock.Provider.Current;
    private Point rest;
    private Size size;
    private bool isResizing;
    private Point presented;
    private Size presentedSize;
    private Point target;
    private Size targetSize;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;

    // Called with every size the placement presents: the surface mounted inside follows the frame as it glides.
    internal Action<Size>? PresentSurface { get; set; }

    internal void PresentSurfaceNow() => PresentSurface?.Invoke(presentedSize);

    public PanelPlacementView(PanelDragState drag, Point rest, Size size, Control content, bool isResizing, Control? frameContent = null)
    {
        this.drag = drag;
        this.frameContent = frameContent ?? content;
        this.rest = rest;
        this.size = size;
        this.isResizing = isResizing;
        Child = content;
        target = drag.Position ?? rest;
        targetSize = size;
        Present(target, size);
        this.Track(() => (drag.Position, AppEnvironment.GetWindowLiveResize(this).IsActive), state => Place(state.IsActive));
        DetachedFromVisualTree += (_, _) => Stop();
    }

    // :535-546. Both origin and size settle; a grip or WINDOW live resize follows immediately.
    public void Update(Point newRest, Size newSize, bool resizing)
    {
        rest = newRest;
        size = newSize;
        isResizing = resizing;
        Place(TopLevel.GetTopLevel(this) is not null && AppEnvironment.GetWindowLiveResize(this).IsActive);
    }

    private void Place(bool liveResize)
    {
        var origin = drag.Position ?? rest;
        var settles = drag.Position is null && !isResizing && !liveResize;
        if (settles && origin == target && size == targetSize) return;
        target = origin;
        targetSize = size;
        Stop();
        if (!settles || TopLevel.GetTopLevel(this) is null || (presented == origin && presentedSize == size))
        {
            Present(origin, size);
            return;
        }
        var from = presented;
        var fromSize = presentedSize;
        var toSize = size;
        var start = clock.GetTimestamp();
        // Chrome.swift:99-104. Reuse Chrome's curves and read the platform's live setting at each settle.
        // Retain the injected platform object across posted observation callbacks, whose context can differ.
        using var motion = Chrome.ReduceMotionPlatform.Override(motionPlatform);
        var animation = Chrome.PanelSlide;
        frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = clock.GetElapsedTime(start).TotalSeconds;
            var complete = seconds >= animation.Duration;
            var p = complete ? 1 : animation.Progress(seconds);
            Present(new Point(from.X + (origin.X - from.X) * p, from.Y + (origin.Y - from.Y) * p),
                new Size(fromSize.Width + (toSize.Width - fromSize.Width) * p, fromSize.Height + (toSize.Height - fromSize.Height) * p));
            if (complete) pending = null;
            else frames.RequestFrame(tick!);
        };
        pending = tick;
        frames.RequestFrame(tick);
    }

    // :329, :516-523, :541-545. The conversation's frame follows the placement, including under its grip overlay.
    private void Present(Point origin, Size value)
    {
        presented = origin;
        presentedSize = value;
        RenderTransform = new TranslateTransform(origin.X, origin.Y);
        Width = Child!.Width = value.Width;
        Height = Child.Height = value.Height;
        frameContent.Width = value.Width;
        frameContent.Height = value.Height;
        PresentSurface?.Invoke(value);
    }

    private void Stop()
    {
        if (pending is { } frame) frames!.CancelFrame(frame);
        pending = null;
    }
}

/// <summary>:549-555 <c>struct PanelResizeAxes: OptionSet</c>.</summary>
public sealed record PanelResizeAxes(long RawValue)
{
    public static PanelResizeAxes Width { get; } = new(1); // :552
    public static PanelResizeAxes Height { get; } = new(2); // :553
    public static PanelResizeAxes Both { get; } = new(1 | 2); // :554

    // :550
    public bool Contains(PanelResizeAxes member) => (RawValue & member.RawValue) == member.RawValue;
}

/// <summary>:557-567 <c>struct PanelResize</c>.</summary>
public sealed record PanelResize
{
    public required PanelDockCorner Corner { get; init; } // :561
    public required Action<Size, PanelResizeAxes> OnResize { get; init; } // :563
    public required Action OnResizeEnd { get; init; } // :564
    public required Action OnReset { get; init; } // :566
}

/// <summary>:569-589 <c>@Observable @MainActor final class PanelResizeState</c>.</summary>
public sealed class PanelResizeState : MainActorModel
{
    private Size? start;
    private Size? liveSize;
    private double? liveUsageHeight;

    // :572
    public Size? Start { get => Read(start); private set => Set(ref start, value); }

    // :575
    public Size? LiveSize { get => Read(liveSize); set => Set(ref liveSize, value); }

    // :577
    public double? LiveUsageHeight { get => Read(liveUsageHeight); set => Set(ref liveUsageHeight, value); }

    // :579
    public bool IsActive => Start is not null;

    // :582-586
    public Size Begin(Size at)
    {
        if (Start is { } current) return current;
        Start = at;
        return at;
    }

    // :588
    public void End() => Start = null;
}

/// <summary>:591-638 <c>private struct PanelResizeGrips: View</c>.</summary>
internal sealed class PanelResizeGrips : Decorator
{
    private const double Edge = 6; // :596
    private const double Corner = 16; // :597

    // :594, :599-615
    public PanelResizeGrips(PanelResize resize)
    {
        var dock = resize.Corner;
        var freeHorizontal = dock.Side == PanelDockSide.Leading
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        var freeVertical = dock.IsTop ? VerticalAlignment.Bottom : VerticalAlignment.Top;

        var panel = new Grid();
        var widthCursor = new Cursor(StandardCursorType.SizeWestEast);
        var heightCursor = new Cursor(StandardCursorType.SizeNorthSouth);
        var cornerCursor = new Cursor(dock switch
        {
            PanelDockCorner.TopLeading => StandardCursorType.BottomRightCorner,
            PanelDockCorner.TopTrailing => StandardCursorType.BottomLeftCorner,
            PanelDockCorner.BottomLeading => StandardCursorType.TopRightCorner,
            PanelDockCorner.BottomTrailing => StandardCursorType.TopLeftCorner,
            _ => throw new UnreachableException(),
        });

        var widthGrip = MakeGrip(PanelResizeAxes.Width, widthCursor);
        widthGrip.Width = Edge;
        widthGrip.HorizontalAlignment = freeHorizontal;
        widthGrip.VerticalAlignment = VerticalAlignment.Stretch;
        panel.Children.Add(widthGrip);

        var heightGrip = MakeGrip(PanelResizeAxes.Height, heightCursor);
        heightGrip.Height = Edge;
        heightGrip.HorizontalAlignment = HorizontalAlignment.Stretch;
        heightGrip.VerticalAlignment = freeVertical;
        panel.Children.Add(heightGrip);

        var cornerGrip = MakeGrip(PanelResizeAxes.Both, cornerCursor);
        cornerGrip.Width = Corner;
        cornerGrip.Height = Corner;
        cornerGrip.HorizontalAlignment = freeHorizontal;
        cornerGrip.VerticalAlignment = freeVertical;
        panel.Children.Add(cornerGrip);

        Child = panel;

        Control MakeGrip(PanelResizeAxes axes, Cursor cursor)
        {
            var handle = new PanelDragHandle(
                travel => resize.OnResize(travel, axes),
                resize.OnResizeEnd,
                cursor,
                cursor,
                resize.OnReset)
            {
                Cursor = cursor,
            };
            ToolTip.SetTip(handle, "Drag to resize \u00B7 Double-click for the automatic size"); // U+00B7 MIDDLE DOT
            AutomationProperties.SetName(handle, "Drag to resize");
            return handle;
        }
    }
}

/// <summary>
/// :640-666 <c>struct PanelDragHandle: View</c>. A thin wrapper over the shared
/// <see cref="Marrow.UI.Chrome.DragHandle"/>/<see cref="DragHandleView"/> (UI/Chrome/DragHandle.swift, B3
/// 52bc792, unit N8). B3 deleted this struct's own B2 body - an <c>NSViewRepresentable</c> around its own
/// <c>PanelDragHandleView: NSView</c> (B2 :640-738), which tracked the press itself - and replaced it with
/// exactly the configuration below (:655-665), the same reduction the Swift diff makes at 52bc792; B3 has no
/// <c>PanelDragHandleView</c> symbol anywhere in the tree. This also fixes a pre-existing port defect (6.2,
/// DragHandle.cs:23-31): the deleted C# <c>PanelDragHandleView.Travel</c> (B2 :908-913) computed
/// <c>pressOrigin.Y - point.Y</c>, the <c>.vertical</c> axis's rule (right for AppKit's own y-up window space,
/// which is what the equivalent Swift code at B2 needed), for what B2's Swift itself called its <c>.free</c>
/// travel; the shared <see cref="DragHandleView"/>'s own <see cref="DragHandleView.Axis.Free"/>
/// (DragHandle.cs:463) reports <c>point.Y - pressOrigin.Y</c> instead, because Avalonia's window space already
/// runs y down, matching the panel's own offset, so <c>.free</c> needs no inversion on this platform.
/// </summary>
public sealed class PanelDragHandle : Decorator
{
    // :647-653, :655-665
    public PanelDragHandle(
        Action<Size> onDrag,
        Action onDragEnd,
        Cursor? cursor = null,
        Cursor? dragCursor = null,
        Action? onDoubleClick = null)
    {
        var view = new DragHandleView
        {
            HandleAxis = DragHandleView.Axis.Free, // :657 axis: .free
            Slop = 2, // :660
            HandleCursor = cursor ?? PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), // :651 .openHand
            DragCursor = dragCursor ?? PointerStyleCursors.Get(PointerStyle.GrabActive, 1), // :652 .closedHand
            OnChange = onDrag, // :661
            OnEnd = onDragEnd, // :662
            OnDoubleClick = onDoubleClick, // :663
        };

        if (cursor is null || dragCursor is null)
        {
            // PointerStyleControl (Marrow.UI.Port, PointerStyle.cs:48) resolves RenderScaling as 1 until the
            // control has a TopLevel; this constructor runs before the caller attaches the handle, so the
            // defaults above are always baked at scale 1 (matches FollowUpQueueTab.cs:525-529's identical
            // note). Re-apply once attached, so a real monitor's RenderScaling gets picked up. A grip (which
            // always passes its own resize cursor for both roles, PanelResizeGrips.MakeGrip below) never runs
            // this branch.
            view.AttachedToVisualTree += (_, _) =>
            {
                var scale = TopLevel.GetTopLevel(view)?.RenderScaling ?? 1;
                if (cursor is null) view.HandleCursor = PointerStyleCursors.Get(PointerStyle.GrabIdle, scale);
                if (dragCursor is null) view.DragCursor = PointerStyleCursors.Get(PointerStyle.GrabActive, scale);
            };
        }

        Child = view;
    }
}
