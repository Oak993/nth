using Avalonia;
using Avalonia.Controls;

namespace Marrow.App.Port;

// PORT-ONLY: SwiftUI `@Environment(AppModel.self)`, read by a view nested below wherever the root attaches the
// running model, rather than taken as a constructor parameter (App.Views.Usage.UsageViews's PlanLimitsView and
// CreditsView are the first callers). Mirrors UI.Port.ViewLifetime.EnvironmentValues' inherited-attached-property
// pattern for the one other environment key this port carries (accessibilityReduceMotion); this one lives in
// Marrow.App, not Marrow.UI, because AppModel is an App-layer type and UI must not depend on App (R-FILE layering).
public sealed class AppEnvironment
{
    private AppEnvironment() { }

    public static readonly AttachedProperty<AppModel?> AppModelProperty =
        AvaloniaProperty.RegisterAttached<AppEnvironment, Control, AppModel?>("AppModel", null, inherits: true);

    public static AppModel GetAppModel(Control control) =>
        control.GetValue(AppModelProperty) ?? throw new System.InvalidOperationException(
            "No AppModel is attached to this control's visual tree (AppEnvironment.SetAppModel was never called on an ancestor).");

    public static void SetAppModel(Control control, AppModel value) => control.SetValue(AppModelProperty, value);
}
