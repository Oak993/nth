using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B2 1fbf81e), :1-547.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; CGSize is Avalonia.Size; a parameter without a default that follows one with a
// default moves before it and callers name their arguments. `[UUID: CGFloat]` is ValueMap<Guid, double> (R-VAL-2).
// The Swift-private view types are public, as the App views ported so far do (ShortcutsSettingsPage.cs).
// Not declared yet, private in Swift and read by no other file: the @State and @GestureState storage (:35-44,
// :321-329), isCollapsed (:31), dragChanged(_:translation:) (:229-283), dragEnded() (:285-298),
// syncEditor(_:) (:510-514) and attachmentOnlyLabel (:516-524), which the implementing unit adds with the bodies.

namespace Marrow.App.Views.Composer;

/// <summary>:8-299 <c>struct FollowUpQueueTab: View</c>.</summary>
public sealed class FollowUpQueueTab : Decorator
{
    // :9 static let overlap: CGFloat = ThreadChangesTab.overlap
    public const double Overlap = ThreadChangesTab.Overlap;

    // :13 static let maxRows = 7
    public const int MaxRows = 7;

    /// <param name="runtime">:27 <c>let runtime: ThreadRuntime</c>.</param>
    // :46-185 body
    public FollowUpQueueTab(ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:46");

    // :20-25 static func cappedHeight(_:rowHeights:count:)
    public static double CappedHeight(double listHeight, ValueMap<Guid, double> rowHeights, int count) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:20");

    // :204 private static let slide = Animation.spring(response: 0.28, dampingFraction: 0.82)
    private static Animation Slide => Animation.Spring(response: 0.28, dampingFraction: 0.82);

    // :208 private static let pairBand: CGFloat = 0.25
    private const double PairBand = 0.25;

    // :212-226 private static func numbers(for:) -> [UUID: Int]
    private static ValueMap<Guid, int> Numbers(IReadOnlyList<FollowUpPrompt> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:212");

    /// <summary>:190-199 <c>private struct RowHeightReporter: ViewModifier</c>.</summary>
    private sealed record RowHeightReporter(Guid Id, Binding<ValueMap<Guid, double>> Heights) // :191-192
    {
        // :194-198 func body(content:)
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:194");
    }
}

/// <summary>:301-525 <c>private struct FollowUpRow: View</c>.</summary>
public sealed class FollowUpRow : Decorator
{
    /// <summary>:331-336 <c>nonisolated private static let proseLift: CGFloat</c>, from the 12 pt system font's cap
    /// height and x-height.</summary>
    private static double ProseLift =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:333");

    /// <param name="position">:302 <c>let position: Int</c>.</param>
    /// <param name="prompt">:303 <c>let prompt: FollowUpPrompt</c>.</param>
    /// <param name="runtime">:304 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="isDragged">:306 <c>let isDragged: Bool</c>.</param>
    /// <param name="showsRule">:308 <c>let showsRule: Bool</c>.</param>
    /// <param name="isBundledWithPrevious">:310 <c>let isBundledWithPrevious: Bool</c>.</param>
    /// <param name="isBundledWithNext">:312 <c>let isBundledWithNext: Bool</c>.</param>
    /// <param name="isPairTarget">:314 <c>let isPairTarget: Bool</c>.</param>
    /// <param name="isPairing">:316 <c>let isPairing: Bool</c>.</param>
    /// <param name="onDragChanged">:317 <c>let onDragChanged: (CGSize) -> Void</c>.</param>
    /// <param name="onDragEnded">:318 <c>let onDragEnded: () -> Void</c>.</param>
    // :338-508 body
    public FollowUpRow(
        int position,
        FollowUpPrompt prompt,
        ThreadRuntime runtime,
        bool isDragged,
        bool showsRule,
        bool isBundledWithPrevious,
        bool isBundledWithNext,
        bool isPairTarget,
        bool isPairing,
        Action<Size> onDragChanged,
        Action onDragEnded) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:338");
}

/// <summary>:527-547 <c>private struct QueueIconButton: View</c>.</summary>
public sealed class QueueIconButton : Decorator
{
    /// <param name="symbol">:528 <c>let symbol: String</c>.</param>
    /// <param name="help">:529 <c>let help: String</c>.</param>
    /// <param name="action">:531 <c>let action: () -> Void</c>; no default, so it moves before isEnabled (ED-095).</param>
    /// <param name="isEnabled">:530 <c>var isEnabled = true</c>.</param>
    // :533-546 body
    public QueueIconButton(string symbol, string help, Action action, bool isEnabled = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpQueueTab.swift:533");
}
