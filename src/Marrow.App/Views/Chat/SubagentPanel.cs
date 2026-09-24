using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B2 1fbf81e), :1-738.
// Skeleton (c100 group 2): every type the file declares and the members the other cycle files use. The data shapes
// (enum cases, record fields, constants) are complete; every member with behaviour, and every view body, is a
// MARROW_STUB left to the implementing unit, as are the views' private members (not declared here).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; an NSView is a Control; an NSViewRepresentable folds into the Control that hosts its
// NSView; CGPoint/CGSize/CGVector are Avalonia Point/Size/Vector; NSCursor is Avalonia.Input.Cursor. Swift `private`
// top-level types are `internal` here: C# has no private top-level type, and `file` cannot appear in another type's
// member signatures (CS9051).

/// <summary>:4-7 <c>enum FloatingPanelID: Hashable</c>. An enum with an associated value is a record union (R-ENUM-2).</summary>
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

/// <summary>:14-45 <c>enum PanelDockCorner: String, Equatable</c> (R-ENUM-1).</summary>
public enum PanelDockCorner
{
    [RawValue("topLeading")] TopLeading, // :16
    [RawValue("topTrailing")] TopTrailing, // :16
    [RawValue("bottomLeading")] BottomLeading, // :16
    [RawValue("bottomTrailing")] BottomTrailing, // :16
}

/// <summary>:18-44 the members declared on <c>PanelDockCorner</c> (R-NAME-4: <c>static class &lt;Enum&gt;Extensions</c>).</summary>
public static class PanelDockCornerExtensions
{
    extension(PanelDockCorner corner)
    {
        // :18-23 var side: PanelDockSide
        public PanelDockSide Side =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:18");

        // :25 var isTop: Bool
        public bool IsTop =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:25");

        // :37-44 var acrossTheColumn: PanelDockCorner
        public PanelDockCorner AcrossTheColumn =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:37");
    }

    extension(PanelDockCorner)
    {
        // :27-34 static func make(side:isTop:) -> PanelDockCorner
        public static PanelDockCorner Make(PanelDockSide side, bool isTop) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:27");
    }
}

/// <summary>:47-52 <c>struct PanelReserve: Equatable</c>.</summary>
public sealed record PanelReserve
{
    public double Leading { get; init; } = 0; // :50
    public double Trailing { get; init; } = 0; // :51
}

/// <summary>:54-97 <c>struct PanelDocks</c>: the four docked spots for the pane as it is.</summary>
public sealed record PanelDocks
{
    public Point TopLeading { get; init; } // :56
    public Point TopTrailing { get; init; } // :57
    public Point BottomLeading { get; init; } // :58
    public Point BottomTrailing { get; init; } // :59

    // :61-66 init(layout:reserve:)
    public PanelDocks(SubagentPanelLayout layout, PanelReserve reserve) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:61");

    // :68-75 subscript(corner: PanelDockCorner) -> CGPoint
    public Point this[PanelDockCorner corner] =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:68");

    // :80-85 func stacked(_:below:slots:layout:) -> CGPoint
    public Point Stacked(PanelDockCorner corner, long below, long slots, SubagentPanelLayout layout) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:80");

    // :90-96 static func corner(forSlot:of:side:) -> (corner: PanelDockCorner, below: Int)
    public static (PanelDockCorner Corner, long Below) Corner(long forSlot, long of, PanelDockSide side) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:90");
}

/// <summary>:99-242 <c>struct SubagentPanelLayout: Equatable</c>: where a floating panel goes and how big it is.</summary>
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

    // :138-140 var panelWidth: CGFloat
    public double PanelWidth =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:138");

    // :143-145 var sitsBesideComposer: Bool
    public bool SitsBesideComposer =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:143");

    // :149-151 var fitsBothSides: Bool
    public bool FitsBothSides =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:149");

    // :154-156 var composerReserve: CGFloat
    public double ComposerReserve =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:154");

    // :160-163 var verticalRoom: CGFloat
    public double VerticalRoom =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:160");

    // :165-170 var panelHeight: CGFloat
    public double PanelHeight =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:165");

    // :172 var panelSize: CGSize
    public Size PanelSize =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:172");

    // :177-181 func fitted(_:) -> CGSize
    public Size Fitted(Size size) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:177");

    // :187-201 func dockedOrigin(_:reserve:) -> CGPoint
    public Point DockedOrigin(PanelDockCorner corner, PanelReserve reserve) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:187");

    // :206-225 func dockCorner(nearest:keeping:docks:) -> PanelDockCorner
    public PanelDockCorner DockCorner(Point nearest, PanelDockCorner keeping, PanelDocks docks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:206");

    // :229-232 func slotIndex(at:slots:docks:) -> Int
    public long SlotIndex(Point at, long slots, PanelDocks docks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:229");

    // :235-241 func clamped(_:) -> CGPoint
    public Point Clamped(Point origin) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:235");
}

/// <summary>:244-357 <c>struct SubagentPanel: View</c>: a helper thread's chat as a small floating glass panel.</summary>
public sealed class SubagentPanel : Decorator
{
    private const double CornerRadius = 22; // :265

    // :266 private static let handleHeight
    private static double HandleHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    // :251-258 the view's inputs; :268-356 body
    public SubagentPanel(
        ChatThread thread,
        Size size,
        string? workingDirectory,
        string? projectName,
        Action<Size> onDrag,
        Action onDragEnd,
        Action close) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:268");
}

/// <summary>:359-413 <c>private struct SubagentWorkingBox: View</c>.</summary>
internal sealed class SubagentWorkingBox : Decorator
{
    // :367-368 the view's inputs; :379-412 body
    public SubagentWorkingBox(string title, bool isRunning) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:379");
}

/// <summary>:415-432 <c>private struct ClosePanelPopover: View</c>.</summary>
internal sealed class ClosePanelPopover : Decorator
{
    // :419-420 the view's inputs; :422-431 body
    public ClosePanelPopover(string title, Action onClose) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:422");
}

/// <summary>:434-496 <c>@Observable @MainActor final class PanelDragState</c> (R-OBS-6).</summary>
public sealed class PanelDragState : MainActorModel
{
    private Point? position;

    // :441 private(set) var position: CGPoint?
    public Point? Position { get => Read(position); private set => Set(ref position, value); }

    private const double FlickCarry = 0.12; // :450
    private const double RestBeforeDrop = 0.08; // :452

    // :456-474 func move(by:from:in:) -> CGPoint
    public Point Move(Size by, Point from, SubagentPanelLayout @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:456");

    // :478-482 func reclamp(in:)
    public void Reclamp(SubagentPanelLayout @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:478");

    // :486-495 func release() -> CGPoint?
    public Point? Release() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:486");
}

/// <summary>:498-525 <c>struct PlacedPanel&lt;Content: View&gt;: View</c>. <c>Content</c> is a Control (ED-095), so the
/// type takes no generic parameter.</summary>
public sealed class PlacedPanel : Decorator
{
    // :505-514 the view's inputs; :516-524 body
    public PlacedPanel(
        PanelDragState drag,
        Point rest,
        Size size,
        Control content,
        PanelResize? resize = null,
        bool isResizing = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:516");
}

/// <summary>:527-547 <c>private struct PanelPlacement: ViewModifier</c>.</summary>
internal sealed record PanelPlacement(PanelDragState Drag, Point Rest, Size Size, bool IsResizing)
{
    // :537-546 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:537");
}

/// <summary>:549-555 <c>struct PanelResizeAxes: OptionSet</c>: a Swift struct is a record (R-VAL-1).</summary>
public sealed record PanelResizeAxes(long RawValue)
{
    public static PanelResizeAxes Width { get; } = new(1); // :552
    public static PanelResizeAxes Height { get; } = new(2); // :553
    public static PanelResizeAxes Both { get; } = new(1 | 2); // :554 [.width, .height]

    // :550 OptionSet's contains(_:), the SetAlgebra member the call sites read
    public bool Contains(PanelResizeAxes member) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:550");
}

/// <summary>:557-567 <c>struct PanelResize</c>.</summary>
public sealed record PanelResize
{
    public required PanelDockCorner Corner { get; init; } // :561
    public required Action<Size, PanelResizeAxes> OnResize { get; init; } // :563
    public required Action OnResizeEnd { get; init; } // :564
    public required Action OnReset { get; init; } // :566
}

/// <summary>:569-589 <c>@Observable @MainActor final class PanelResizeState</c> (R-OBS-6).</summary>
public sealed class PanelResizeState : MainActorModel
{
    private Size? start;
    private Size? liveSize;
    private double? liveUsageHeight;

    // :572 private(set) var start: CGSize?
    public Size? Start { get => Read(start); private set => Set(ref start, value); }

    // :575 var liveSize: CGSize?
    public Size? LiveSize { get => Read(liveSize); set => Set(ref liveSize, value); }

    // :577 var liveUsageHeight: CGFloat?
    public double? LiveUsageHeight { get => Read(liveUsageHeight); set => Set(ref liveUsageHeight, value); }

    // :579 var isActive: Bool
    public bool IsActive =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:579");

    // :582-586 func begin(at:) -> CGSize
    public Size Begin(Size at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:582");

    // :588 func end()
    public void End() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:588");
}

/// <summary>:591-638 <c>private struct PanelResizeGrips: View</c>.</summary>
internal sealed class PanelResizeGrips : Decorator
{
    private const double Edge = 6; // :596
    private const double Corner = 16; // :597

    // :594 the view's input; :599-615 body
    public PanelResizeGrips(PanelResize resize) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:599");
}

/// <summary>:640-675 <c>struct PanelDragHandle: NSViewRepresentable</c>: hosts its <see cref="PanelDragHandleView"/>.
/// The Swift defaults <c>.openHand</c> and <c>.closedHand</c> are not compile-time constants in C#, so a null
/// <paramref name="cursor"/> or <paramref name="dragCursor"/> stands for them (resolved by the implementing unit).</summary>
public sealed class PanelDragHandle : Decorator
{
    // :647-653 the inputs; :655-674 makeNSView, updateNSView, update(_:)
    public PanelDragHandle(
        Action<Size> onDrag,
        Action onDragEnd,
        Cursor? cursor = null,
        Cursor? dragCursor = null,
        Action? onDoubleClick = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/SubagentPanel.swift:655");
}

/// <summary>:677-738 <c>final class PanelDragHandleView: NSView</c>. Swift's <c>cursor</c> (:681, the cursor rect of
/// :695-698) is the inherited <see cref="InputElement.Cursor"/>.</summary>
public sealed class PanelDragHandleView : Control
{
    public Action<Size>? OnDrag { get; set; } // :678
    public Action? OnDragEnd { get; set; } // :679
    public Action? OnDoubleClick { get; set; } // :680
    public Cursor? DragCursor { get; set; } // :682 = .closedHand

    private const double Slop = 2; // :685
}
