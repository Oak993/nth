using System;
using Avalonia.Controls;
using Marrow.App.Runtime;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B2 1fbf81e), :1-443.
// Skeleton (c100 group 5): every type the file declares, their inputs and the members the other files use. Every view
// body and every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View and an NSViewRepresentable are Decorators whose constructor builds `body` or makes the
// hosted view; an NSView is a Control (TerminalResizeHandleView draws its own grip; TerminalHostContainer holds the
// terminal view, so it is a Decorator); CGFloat and CFTimeInterval are double, Float float. Swift `private` top-level
// types are `internal`. `@Environment(AppModel.self)` and `\.colorScheme` are read by the bodies, so they are no
// constructor parameters.
// Not declared yet: TerminalPanel's @State storage (:10-13), the representables' updateNSView plumbing (:131-133,
// :350-365), TerminalResizeHandleView's private layer and state (:155-159, :191) and its AppKit overrides
// (viewDidChangeEffectiveAppearance :181-184, layout :186-189, acceptsFirstMouse :212, mouseDownCanMoveWindow :214,
// resetCursorRects :216-219, mouseDown/Dragged/Up :221-237, updateTrackingAreas :239-245, mouseEntered/Exited
// :247-255), TerminalHost.Coordinator's deinit (:327-329) and TerminalHostContainer's private state (:386-388), deinit
// (:391-394) and overrides (viewDidMoveToWindow :396-412, resizeSubviews :414-426), which become Avalonia overrides and
// handlers with the bodies.

namespace Marrow.App.Views.Terminal;

/// <summary>:4-112 <c>struct TerminalPanel: View</c>: the thread's terminal drawer.</summary>
public sealed class TerminalPanel : Decorator
{
    /// <param name="runtime">:7 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="directory">:8 <c>let directory: String</c>.</param>
    // :15-110 body
    public TerminalPanel(ThreadRuntime runtime, string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:15");
}

/// <summary>:118-140 <c>private struct TerminalResizeHandle: NSViewRepresentable</c>: the drawer's resize grip.</summary>
internal sealed class TerminalResizeHandle : Decorator
{
    /// <param name="onBegin">:119 <c>let onBegin: () -> Void</c>.</param>
    /// <param name="onChange">:122 <c>let onChange: (CGFloat) -> Void</c>, positive upward.</param>
    /// <param name="onEnd">:123 <c>let onEnd: () -> Void</c>.</param>
    // :125-129 makeNSView(context:)
    public TerminalResizeHandle(Action onBegin, Action<double> onChange, Action onEnd) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:125");

    // :135-139 private func apply(to view: TerminalResizeHandleView)
    private void Apply(TerminalResizeHandleView to) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:135");
}

/// <summary>:142-256 <c>final class TerminalResizeHandleView: NSView</c>.</summary>
public sealed class TerminalResizeHandleView : Control
{
    // :143 var onBegin: (() -> Void)?
    public Action? OnBegin { get; set; }

    // :144 var onChange: ((CGFloat) -> Void)?
    public Action<double>? OnChange { get; set; }

    // :145 var onEnd: (() -> Void)?
    public Action? OnEnd { get; set; }

    /// <summary>:147-153 <c>private enum Grip</c>.</summary>
    private static class Grip
    {
        public const double Height = 2.5; // :148
        public const double RestWidth = 12; // :149
        public const double ShownWidth = 28; // :150
        public const float Alpha = 0.6f; // :151
        public const double Duration = 0.18; // :152
    }

    // :161-168 override init(frame:)
    public TerminalResizeHandleView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:161");

    // :175-179 private func applyColors()
    private void ApplyColors() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:175");

    // :193-210 private func updateGrip(animated: Bool)
    private void UpdateGrip(bool animated) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:193");
}

/// <summary>:258-311 <c>private struct TerminalTab: View</c>.</summary>
internal sealed class TerminalTab : Decorator
{
    /// <param name="session">:259 <c>let session: TerminalSession</c>.</param>
    /// <param name="isSelected">:260 <c>let isSelected: Bool</c>.</param>
    /// <param name="select">:261 <c>let select: () -> Void</c>.</param>
    /// <param name="restart">:262 <c>let restart: () -> Void</c>.</param>
    /// <param name="close">:263 <c>let close: () -> Void</c>.</param>
    // :267-310 body
    public TerminalTab(TerminalSession session, bool isSelected, Action select, Action restart, Action close) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:267");
}

/// <summary>:316-376 <c>struct TerminalHost: NSViewRepresentable</c>: hosts a long-lived terminal view.</summary>
public sealed class TerminalHost : Decorator
{
    /// <param name="session">:317 <c>let session: TerminalSession</c>.</param>
    /// <param name="runtime">:318 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="isDark">:319 <c>let isDark: Bool</c>.</param>
    /// <param name="onEscapeAtPrompt">:322 <c>let onEscapeAtPrompt: () -> Void</c>.</param>
    // :334-348 makeNSView(context:)
    public TerminalHost(TerminalSession session, ThreadRuntime runtime, bool isDark, Action onEscapeAtPrompt) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:334");

    /// <summary>:324-330 <c>final class Coordinator</c>.</summary>
    public sealed class Coordinator
    {
        // :325 var keyMonitor: Any?
        public object? KeyMonitor { get; set; }
    }

    // :332 func makeCoordinator() -> Coordinator
    public Coordinator MakeCoordinator() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:332");

    // :367-375 private func attach(to container: NSView)
    private void Attach(Control to) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:367");
}

/// <summary>:382-443 <c>final class TerminalHostContainer: NSView</c>: holds the terminal view while the window
/// resizes.</summary>
public sealed class TerminalHostContainer : Decorator
{
    // :384 weak var hostedView: NSView? (the weak hold is the implementing unit's, R-LIFE)
    public Control? HostedView { get; set; }

    // :430-435 private func finishResize()
    private void FinishResize() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:430");

    // :437-442 private func syncFrame()
    private void SyncFrame() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Terminal/TerminalPanel.swift:437");
}
