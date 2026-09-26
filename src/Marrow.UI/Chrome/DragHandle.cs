using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/DragHandle.swift (B3 52bc792), :1-304.
// STUB for the tests-first RED phase (unit UI.Chrome.DragHandle, N8): the public shape exists so the test project
// compiles, but no method carries the Swift behaviour yet. Every test below must fail against this shape.
public sealed class DragHandleView : Canvas
{
    public enum Axis { Horizontal, Vertical, Free }

    public sealed record Grip(
        double RestLength,
        double ShownLength,
        double Thickness,
        double Alpha = 0.6,
        double? GlowInset = null,
        bool RefreshesHoverOnLayout = false,
        double HoverGlow = 0.05,
        double DragGlow = 0.10);

    public static Ambient<ICursorPlatform> CursorPlatform { get; } = new(() => new WindowsCursorPlatform());

    public Action? OnBegin { get; set; }
    public Action<Size>? OnChange { get; set; }
    public Action? OnEnd { get; set; }
    public Action? OnDoubleClick { get; set; }
    public Axis HandleAxis { get; set; } = Axis.Horizontal;
    public Grip? HandleGrip { get; private set; }
    public Cursor HandleCursor { get; set; } = Cursor.Default;
    public Cursor? DragCursor { get; set; }
    public double Slop { get; set; }
    public bool IsActive { get; set; } = true;

    public void Configure(Grip? grip) => HandleGrip = grip; // STUB: no side effects yet

    public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;
    public bool MouseDownCanMoveWindow => false;

    internal bool IsHoveringForTest => false;
    internal bool IsDraggingForTest => false;
    internal Border GripLayerForTest { get; } = new();
    internal Border GlowLayerForTest { get; } = new();
}

public sealed class DragHandle
{
    public DragHandleView.Axis HandleAxis { get; set; } = DragHandleView.Axis.Horizontal;
    public DragHandleView.Grip? HandleGrip { get; set; }
    public Cursor HandleCursor { get; set; } = Cursor.Default;
    public Cursor? DragCursor { get; set; }
    public double Slop { get; set; }
    public bool IsActive { get; set; } = true;
    public Action? OnBegin { get; set; }
    public Action<Size>? OnChange { get; set; }
    public Action? OnEnd { get; set; }
    public Action? OnDoubleClick { get; set; }

    public DragHandleView MakeNSView() => new();
    public void UpdateNSView(DragHandleView view) { }
}
