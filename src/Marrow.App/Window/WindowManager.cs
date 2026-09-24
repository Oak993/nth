using System;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B2 1fbf81e), :1-222.
// Skeleton (c100 group 5): every type the file declares, the size constants and the members the other files use.
// Every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): NSWindow is an Avalonia Window (ThreadWindow.cs's `AvaloniaWindow` alias, since this namespace is
// Marrow.App.Window); NSSize is Avalonia.Size; a ViewModifier is a sealed record with `Control Body(Control content)`;
// a generic SwiftUI View (HostedRoot<Content>) is a Decorator taking its content as a Control; NSHostingView<AnyView>
// (FrameHostingView) is a Decorator holding the root view. The windowClass parameter of makeWindow (:90) is a factory
// for the window. DCDisplayCycleGuardedWindow is macOS-only and not re-implemented (dead_code.md 41; ThreadWindow.cs).
// NOT PORTED: makeCaptureWindow(size:) (:74-78) is reachable only from the website capture run (dead_code.md 39;
// ED-001), so it is not declared.
// Not declared yet: the private window fields (:15-16), FrameHostingView's AppKit overrides acceptsFirstResponder
// (:214) and performKeyEquivalent(with:) (:219-221), which become Avalonia focus and key handling with the bodies,
// and HostedRoot's `onChange(of:initial:)` appearance switch (:152-159), part of its body.

namespace Marrow.App.Window;

/// <summary>:9-111 <c>@MainActor final class WindowManager</c>: builds the app's windows.</summary>
public sealed class WindowManager
{
    // :11 static let shared = WindowManager()
    public static WindowManager Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:11");

    // :13 let model = AppModel()
    public AppModel Model { get; }

    /// <summary>:18-22 <c>private enum Size</c>.</summary>
    private static class Size
    {
        public static readonly Avalonia.Size Main = new(1320, 860); // :19
        public static readonly Avalonia.Size MainMinimum = new(860, 560); // :20
        public static readonly Avalonia.Size Settings = new(780, 580); // :21
    }

    // :24 private static let mainFrameName = "DroppyCodeMainWindow" (a frame-autosave key, a wire name kept verbatim)
    private const string MainFrameName = "DroppyCodeMainWindow";

    // The Swift singleton's implicit init: `model` (:13) is made here.
    private WindowManager() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:13");

    // :26-30 func showMain()
    public void ShowMain() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:26");

    // :32-38 func showSettings(page: SettingsPage? = nil)
    public void ShowSettings(SettingsPage? page = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:32");

    // :41-55 private func centerOverMainWindow(_ window: NSWindow)
    private void CenterOverMainWindow(AvaloniaWindow window) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:41");

    // :57-60 private func present(_ window: NSWindow)
    private void Present(AvaloniaWindow window) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:57");

    // :62-72 private func makeMainWindow() -> NSWindow
    private AvaloniaWindow MakeMainWindow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:62");

    // :80-84 private func makeSettingsWindow() -> NSWindow
    private AvaloniaWindow MakeSettingsWindow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:80");

    // :86-110 private func makeWindow<Content: View>(title:size:resizable:windowClass:content:) -> NSWindow
    private AvaloniaWindow MakeWindow(string title, Avalonia.Size size, bool resizable, Func<Control> content, Func<AvaloniaWindow>? windowClass = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:86");
}

/// <summary>:118-140 <c>@MainActor @Observable final class WindowLiveResize</c> (R-OBS-6): whether the window is
/// being dragged to a new size right now.</summary>
public sealed class WindowLiveResize : MainActorModel
{
    private bool isActive; // :120
    private bool isPaneResizing; // :125

    // :120 private(set) var isActive = false
    public bool IsActive
    {
        get => Read(isActive);
        private set => Set(ref isActive, value);
    }

    // :125 var isPaneResizing = false
    public bool IsPaneResizing
    {
        get => Read(isPaneResizing);
        set => Set(ref isPaneResizing, value);
    }

    // :127 var isReshaping: Bool { isActive || isPaneResizing }
    public bool IsReshaping =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:127");

    // :131-139 init(window: NSWindow)
    public WindowLiveResize(AvaloniaWindow window) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:131");
}

/// <summary>:143-161 <c>private struct HostedRoot&lt;Content: View&gt;: View</c>: the shared environment for every
/// hosted window, plus the app-wide appearance choice.</summary>
internal sealed class HostedRoot : Decorator
{
    /// <param name="model">:144 <c>let model: AppModel</c>.</param>
    /// <param name="liveResize">:145 <c>let liveResize: WindowLiveResize</c>.</param>
    /// <param name="content">:146 <c>let content: Content</c>.</param>
    // :148-160 body
    public HostedRoot(AppModel model, WindowLiveResize liveResize, Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:148");
}

/// <summary>:165-179 <c>struct HostedEnvironment: ViewModifier</c>: the environment every hosted view tree starts
/// from.</summary>
/// <param name="Model">:166 <c>let model: AppModel</c>.</param>
/// <param name="LiveResize">:167 <c>let liveResize: WindowLiveResize</c>.</param>
public sealed record HostedEnvironment(AppModel Model, WindowLiveResize LiveResize)
{
    // :169-178 func body(content: Content) -> some View
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:169");
}

/// <summary>:183-193 <c>private struct ThemeTint: ViewModifier</c>: tints prominent controls with the theme's
/// accent.</summary>
/// <param name="Theme">:184 <c>let theme: AppTheme</c>.</param>
internal sealed record ThemeTint(AppTheme Theme)
{
    // :186-192 func body(content: Content) -> some View
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:186");
}

/// <summary>:197-222 <c>final class FrameHostingView: NSHostingView&lt;AnyView&gt;</c>: lets the frame it is given
/// define the hosting view's size (sizingOptions = [], ED-150).</summary>
public sealed class FrameHostingView : Decorator
{
    // :198-204 required init(rootView: AnyView)
    public FrameHostingView(Control rootView) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Window/WindowManager.swift:198");
}
