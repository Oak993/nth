using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Common/RenderBudget.swift (B2 1fbf81e), :1-99.
// Cycle c35 is implemented below: scrolling/reporters, watchdog and panel control fill.
// Two namespaces in one file: ScrollActivity, ScrollActivityReporter and the @Entry holder live in Marrow.UI.Common
// (R-FILE-3); `extension Chrome` (:89-99) is a partial of Marrow.UI.Chrome.Chrome and must share its namespace
// (R-FILE-1).
// `static let shared` (:14) is an Ambient<ScrollActivity>, the port's form of a shared singleton (R-LIFE-2,
// ShortcutStore.Shared precedent), so a test replaces it for its own flow.
// :17-23, :25-27: the @ObservationIgnored storage and WeakReporter struct are private implementation details.

namespace Marrow.UI.Common
{
    /// <summary>:5-69 <c>@MainActor @Observable final class ScrollActivity</c> (R-OBS-6).</summary>
    public sealed class ScrollActivity : MainActorModel
    {
        // :25-27 private struct WeakReporter { weak var object: (any ScrollActivityReporter)? }
        private struct WeakReporter
        {
            public WeakReference<ScrollActivityReporter>? Object { get; set; }
        }

        // :14 static let shared = ScrollActivity()
        public static Ambient<ScrollActivity> Shared { get; } = new(() => new ScrollActivity());

        private bool isScrolling; // :16 = false

        // :16 private(set) var isScrolling = false
        public bool IsScrolling { get => Read(isScrolling); private set => Set(ref isScrolling, value); }

        // :17 @ObservationIgnored private var scrolling: Set<UUID> = []
        private HashSet<Guid> scrolling = new();

        // :18-22 @ObservationIgnored private var reporters: [UUID: WeakReporter] = [:]
        private Dictionary<Guid, WeakReporter> reporters = new();

        // :23 @ObservationIgnored private var watch: Task<Void, Never>?
        private TaskHandle? watch;

        // :29-39 func setScrolling(_ active: Bool, timeline: UUID, reporter: (any ScrollActivityReporter)? = nil)
        public void SetScrolling(bool active, Guid timeline, ScrollActivityReporter? reporter = null)
        {
            if (active)
            {
                // :31-33 scrolling.insert(timeline); reporters[timeline] = WeakReporter(object: reporter)
                scrolling.Add(timeline);
                reporters[timeline] = new WeakReporter
                {
                    Object = reporter != null ? new WeakReference<ScrollActivityReporter>(reporter) : null
                };
            }
            else
            {
                // :35-36 scrolling.remove(timeline); reporters[timeline] = nil
                scrolling.Remove(timeline);
                reporters.Remove(timeline);
            }

            Update();
        }

        // :41-68 private func update()
        private void Update()
        {
            // :42 let now = !scrolling.isEmpty
            var now = scrolling.Count > 0;

            // :43 if now != isScrolling { isScrolling = now }
            if (now != IsScrolling)
            {
                IsScrolling = now;
            }

            // :44-63 if now, watch == nil { watch = Task { ... } }
            if (now && watch == null)
            {
                // :45 watch = Task { @MainActor [weak self] in
                var weakSelf = new WeakReference<ScrollActivity>(this); // [weak self]: the task must not keep the activity alive
                watch = MainActor.Spawn(() => WatchdogTask(weakSelf));
            }
            // :64-67 } else if !now { watch?.cancel(); watch = nil }
            else if (!now)
            {
                watch?.Cancel();
                watch = null;
            }
        }

        // :44-63 The watchdog task that polls every 2 seconds and drops dead reporters
        private static async Task WatchdogTask(WeakReference<ScrollActivity> weakSelf)
        {
            // :46 while !Task.isCancelled
            while (!SwiftTask.IsCancelled)
            {
                // :47 try? await Task.sleep(for: .seconds(2))
                if (!await SwiftTask.TrySleep(TimeSpan.FromSeconds(2)))
                {
                    return; // Cancelled during sleep
                }

                // :48 guard let self, !Task.isCancelled else { return }
                if (SwiftTask.IsCancelled || !weakSelf.TryGetTarget(out var self))
                {
                    return;
                }

                if (self.WatchdogPass())
                {
                    return;
                }
            }
        }

        // :49-63 one pass of the watchdog body; true when nothing is scrolling any more and the task ends.
        private bool WatchdogPass()
        {
            {
                // :49-55 for id in Array(scrolling) { ... }
                // Make a copy to iterate safely while mutating
                var ids = new List<Guid>(scrolling);
                foreach (var id in ids)
                {
                    // :50 guard let reporter = reporters[id]?.object, reporter.isScrollingNow else
                    if (!reporters.TryGetValue(id, out var weakReporter))
                    {
                        continue;
                    }

                    ScrollActivityReporter? reporter = null;
                    if (weakReporter.Object != null)
                    {
                        weakReporter.Object.TryGetTarget(out reporter);
                    }

                    // :50-53 if reporter is gone or not scrolling, remove both id and reporter entry
                    if (reporter == null || !reporter.IsScrollingNow)
                    {
                        // :51-52 scrolling.remove(id); reporters[id] = nil
                        scrolling.Remove(id);
                        reporters.Remove(id);
                    }
                }

                // :56-57 let still = !scrolling.isEmpty; if still != isScrolling { isScrolling = still }
                var still = scrolling.Count > 0;
                if (still != IsScrolling)
                {
                    IsScrolling = still;
                }

                // :58-61 if !still { watch = nil; return }
                if (!still)
                {
                    watch = null;
                    return true;
                }
            }

            return false;
        }
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
        /// <summary>
        /// The fill of a control drawn flat on a glass panel: the theme's tint over the panel's scrim,
        /// lifted toward white or black by an exact amount and opacity applied based on appearance.
        /// Used inside floating glass panels to avoid expensive second-pass rendering.
        /// Mixes Chrome.glassTint with white (dark mode) or black (light mode) by the lift,
        /// then applies opacity exactly 0.55 in dark mode and 0.42 in light mode. (:93-98)
        /// </summary>
        public static SwiftColor PanelControlFill(bool isDark, bool hovered = false)
        {
            // :94 let lift = hovered ? (isDark ? 0.24 : 0.12) : (isDark ? 0.12 : 0.04)
            var lift = hovered ? (isDark ? 0.24 : 0.12) : (isDark ? 0.12 : 0.04);

            // :95-96 return Chrome.glassTint.mix(with: isDark ? .white : .black, by: lift)
            var tint = GlassTint;
            var targetColor = isDark ? SwiftColor.White : SwiftColor.Black;
            var mixed = tint.Mix(targetColor, lift);

            // :97 .opacity(isDark ? 0.55 : 0.42)
            var opacity = isDark ? 0.55 : 0.42;
            return mixed.Opacity(opacity);
        }
    }
}
