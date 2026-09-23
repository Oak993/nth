using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Common/RenderBudget.swift (B2 1fbf81e), :1-99.
// SKELETON (cycle c35). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Two namespaces in one file: ScrollActivity, ScrollActivityReporter and the @Entry holder live in Marrow.UI.Common
// (R-FILE-3); `extension Chrome` (:89-99) is a partial of Marrow.UI.Chrome.Chrome and must share its namespace
// (R-FILE-1).
// `static let shared` (:14) is an Ambient<ScrollActivity>, the port's form of a shared singleton (R-LIFE-2,
// ShortcutStore.Shared precedent), so a test replaces it for its own flow.
// Not declared yet, private in Swift and read by no other file: the @ObservationIgnored storage (:17-23) and
// the private WeakReporter (:25-27), which the implementing unit adds with the bodies.

namespace Marrow.UI.Common
{
    /// <summary>:5-69 <c>@MainActor @Observable final class ScrollActivity</c> (R-OBS-6).</summary>
    public sealed class ScrollActivity : MainActorModel
    {
        // :14 static let shared = ScrollActivity()
        public static Ambient<ScrollActivity> Shared { get; } = new(() => new ScrollActivity());

        private bool isScrolling; // :16 = false

        // :16 private(set) var isScrolling = false
        public bool IsScrolling { get => Read(isScrolling); private set => Set(ref isScrolling, value); }

        // :29-39 func setScrolling(_:timeline:reporter:)
        public void SetScrolling(bool active, Guid timeline, ScrollActivityReporter? reporter = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Common/RenderBudget.swift:30");

        // :41-68 private func update()
        private void Update() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Common/RenderBudget.swift:41");
    }

    /// <summary>:71-75 <c>@MainActor protocol ScrollActivityReporter: AnyObject</c>. The name is Swift's (R-NAME-1).</summary>
    public interface ScrollActivityReporter
    {
        // :74 var isScrollingNow: Bool { get }
        bool IsScrollingNow { get; }
    }

    /// <summary>The file's extension of a library type (R-NAME-4, ED-095): <c>extension EnvironmentValues</c> (:79-87).</summary>
    public static class RenderBudget
    {
        // :86 @Entry var isOnGlassPanel = false, an inherited environment value (R-VIEW-6).
        public static readonly AttachedProperty<bool> IsOnGlassPanelProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsOnGlassPanel", typeof(RenderBudget), false, inherits: true);

        public static bool GetIsOnGlassPanel(Control control) => control.GetValue(IsOnGlassPanelProperty);

        public static void SetIsOnGlassPanel(Control control, bool value) => control.SetValue(IsOnGlassPanelProperty, value);
    }
}

namespace Marrow.UI.Chrome
{
    /// <summary>:89-99 <c>extension Chrome</c> (R-FILE-1: a partial of Chrome in this file's twin).</summary>
    public static partial class Chrome
    {
        // :93-98 static func panelControlFill(isDark:hovered:) -> Color
        public static SwiftColor PanelControlFill(bool isDark, bool hovered = false) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Common/RenderBudget.swift:93");
    }
}
