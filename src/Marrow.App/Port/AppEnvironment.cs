using Avalonia;
using Avalonia.Controls;
using Marrow.App.Window;

namespace Marrow.App.Port;

// PORT-ONLY: SwiftUI `@Environment(AppModel.self)` and `@Environment(WindowLiveResize.self)`, read by a view nested
// below wherever the root attaches the running model and the window's live-resize object, rather than taken as
// constructor parameters (App.Views.Usage.UsageViews's PlanLimitsView and CreditsView are the first callers of the
// model; RootView.swift:122 and the chat views read the live-resize object). HostedEnvironment
// (App/Window/WindowManager.swift:170-172) attaches both with `.environment(model).environment(liveResize)`. Mirrors
// UI.Port.ViewLifetime.EnvironmentValues' inherited-attached-property pattern for the one other environment key this
// port carries (accessibilityReduceMotion); these live in Marrow.App, not Marrow.UI, because AppModel and
// WindowLiveResize are App-layer types and UI must not depend on App (R-FILE layering).
public sealed class AppEnvironment
{
    private AppEnvironment() { }

    public static readonly AttachedProperty<AppModel?> AppModelProperty =
        AvaloniaProperty.RegisterAttached<AppEnvironment, Control, AppModel?>("AppModel", null, inherits: true);

    public static AppModel GetAppModel(Control control) =>
        control.GetValue(AppModelProperty) ?? throw new System.InvalidOperationException(
            "No AppModel is attached to this control's visual tree (AppEnvironment.SetAppModel was never called on an ancestor).");

    public static void SetAppModel(Control control, AppModel value) => control.SetValue(AppModelProperty, value);

    // SwiftUI hands a popover's content the environment of the view that presents it. A Popup that is not itself added to the
    // tree hosts its content in a window of its own, outside the main window's tree, so the content inherits no AppModel; the
    // presenting view carries it over with this (the same as ModelEffortButton and ContextMeter do for their panels).
    public static T Carry<T>(T content, Control presenter) where T : Control
    {
        SetAppModel(content, GetAppModel(presenter));
        return content;
    }

    public static readonly AttachedProperty<WindowLiveResize?> WindowLiveResizeProperty =
        AvaloniaProperty.RegisterAttached<AppEnvironment, Control, WindowLiveResize?>("WindowLiveResize", null, inherits: true);

    public static WindowLiveResize GetWindowLiveResize(Control control) =>
        control.GetValue(WindowLiveResizeProperty) ?? throw new System.InvalidOperationException(
            "No WindowLiveResize is attached to this control's visual tree (AppEnvironment.SetWindowLiveResize was never called on an ancestor).");

    public static void SetWindowLiveResize(Control control, WindowLiveResize value)
    {
        control.SetValue(WindowLiveResizeProperty, value);
        // ED-218: UI collection layout reads this live callback; UI never references WindowLiveResize/App.
        Marrow.UI.Port.EnvironmentValues.SetWindowIsLiveResizing(control, () => value.IsReshaping);
    }
}
