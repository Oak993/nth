// Source: DroppyCode/App/Views/Settings/SettingsScrollTracker.swift (B2 1fbf81e), :1-56.

using Marrow.Core.Port;
using Marrow.UI.Common;

namespace Marrow.App.Views.Settings;

/// Whether the reader is scrolling a settings pane right now, reported to
/// ScrollActivity under one fixed pane id so every effect that has no business
/// running under a moving finger rests while the pane scrolls, and declared to the
/// rows so they stop hovering (and animating) as they slide under the pointer.
public sealed class SettingsScrollTracker : MainActorModel, ScrollActivityReporter
{
    // :9-10
    private static readonly Guid Pane = Guid.NewGuid();

    // :11 private(set) var isScrolling
    private bool isScrolling;
    public bool IsScrolling { get => Read(isScrolling); private set => Set(ref isScrolling, value); }

    // :15 private(set) var isFrozen
    private bool isFrozen;
    /// Whether the pane's rows are held away from the pointer: true through the drag and
    /// the wheel's movement, false again once the flick coasts or the scroll settles, so a
    /// click that stops a flick always reaches its target (the chat timeline's rule).
    public bool IsFrozen { get => Read(isFrozen); private set => Set(ref isFrozen, value); }

    // :16 @ObservationIgnored private var isCoasting
    private bool isCoasting;

    // :17 @ObservationIgnored private var settle: Task<Void, Never>?
    private TaskHandle? settle;

    // :19 var isScrollingNow: Bool { isScrolling }
    public bool IsScrollingNow => IsScrolling;

    // :22-53
    /// A frame of movement: scrolling from now, and until 150ms have passed with none.
    public void Note()
    {
        // :23-26
        if (!isScrolling)
        {
            IsScrolling = true;
            ScrollActivity.Shared.Current.SetScrolling(true, Pane, this);
        }

        // :27 if !isCoasting, !isFrozen { isFrozen = true }
        if (!isCoasting && !isFrozen)
        {
            IsFrozen = true;
        }

        // :28-36 settle?.cancel(); settle = Task { ... }
        settle?.Cancel();
        settle = MainActor.Spawn(async () =>
        {
            // :30 try? await Task.sleep(for: .milliseconds(150))
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(150));

            // :31 guard let self, !Task.isCancelled else { return }
            if (SwiftTask.IsCancelled)
            {
                return;
            }

            // :32-35
            IsScrolling = false;
            isCoasting = false;
            if (isFrozen)
            {
                IsFrozen = false;
            }

            // :35 ScrollActivity.shared.setScrolling(false, timeline: Self.pane)
            ScrollActivity.Shared.Current.SetScrolling(false, Pane);
        });
    }

    // :41-44
    /// The drag is over and the flick is coasting: the rows take the pointer again, while
    /// everything that rests on a scroll still rests.
    public void Coast()
    {
        // :42
        isCoasting = true;

        // :43
        if (isFrozen)
        {
            IsFrozen = false;
        }
    }

    // :46-55
    /// The pane is going away: nothing of it is reported any more.
    public void End()
    {
        // :48-49
        settle?.Cancel();
        settle = null;

        // :50
        if (isFrozen)
        {
            IsFrozen = false;
        }

        // :51
        isCoasting = false;

        // :52-54
        if (!isScrolling)
        {
            return;
        }

        // :53-54
        IsScrolling = false;
        ScrollActivity.Shared.Current.SetScrolling(false, Pane);
    }
}
