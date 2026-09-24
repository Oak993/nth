using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Chrome;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraPanel.swift (B2 1fbf81e), :1-553.
// Skeleton (c100 group 2): every type the file declares, the views' inputs and the members the other cycle files use.
// The constants are complete; every view body, strip(selected:) (:126-200) and every member with behaviour are left to
// the implementing unit (MARROW_STUB), as are HydraStatusText's private steps (:542-545) and elapsed (:547-552).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a parameter without a default that
// follows one with a default moves before it, so HydraPanel's `isPoppedOut` and `popOut` come last and callers name
// their arguments; a caseless enum used as a namespace is a static class. Swift `private` top-level types are
// `internal` here: C# has no private top-level type, and `file` cannot appear in another type's member signatures.

/// <summary>:4-201 <c>struct HydraPanel: View</c>: the lead's team as a floating glass panel.</summary>
public sealed class HydraPanel : Decorator
{
    public const double CornerRadius = 22; // :33

    // :37 static let stripHeight: CGFloat
    public static double StripHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    public const double FinishHold = 2.0; // :41

    // :18-31 the view's inputs (isPoppedOut :24 and popOut :29 moved last, ED-095); :46-122 body
    public HydraPanel(
        ThreadRuntime runtime,
        IReadOnlyList<ChatThread> heads,
        Size size,
        string? workingDirectory,
        string? projectName,
        Action<Size> onDrag,
        Action onDragEnd,
        Action dismiss,
        bool isPoppedOut = false,
        Action<Guid>? popOut = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:46");
}

/// <summary>:203-208 <c>private struct StageHold: Hashable</c>.</summary>
internal sealed record StageHold
{
    public required Guid Id { get; init; } // :206
    public required DateTimeOffset StartedAt { get; init; } // :207
}

/// <summary>:210-214 <c>private struct StageMark: Equatable</c>.</summary>
internal sealed record StageMark
{
    public Guid? Id { get; init; } // :212
    public HydraHeadInfo.Status? Status { get; init; } // :213
}

/// <summary>:216-288 <c>private struct HydraHeadsButton: View</c>.</summary>
internal sealed class HydraHeadsButton : Decorator
{
    // :223-225 the view's inputs; :230-287 body
    public HydraHeadsButton(ThreadRuntime runtime, IReadOnlyList<ChatThread> heads, Action dismiss) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:230");
}

/// <summary>:290-297 <c>private struct HydraSpokesMark: View</c>.</summary>
internal sealed class HydraSpokesMark : Decorator
{
    // :292-296 body
    public HydraSpokesMark() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:292");
}

/// <summary>:299-363 <c>private struct HydraHeadRow: View</c>.</summary>
internal sealed class HydraHeadRow : Decorator
{
    // :304-306 the view's inputs; :310-362 body
    public HydraHeadRow(ChatThread head, bool isOnStage, Action select) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:310");
}

/// <summary>:365-383 <c>private struct HydraStopButton: View</c>.</summary>
internal sealed class HydraStopButton : Decorator
{
    // :368-369 the view's inputs; :371-382 body
    public HydraStopButton(string name, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:371");
}

/// <summary>:385-437 <c>private struct HydraHeadTranscript: View</c>.</summary>
internal sealed class HydraHeadTranscript : Decorator
{
    // :388-391 the view's inputs; :396-436 body
    public HydraHeadTranscript(ChatThread head, double height, string? workingDirectory, string? projectName) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:396");
}

/// <summary>:439-473 <c>private struct HydraHeadFooter: View</c>.</summary>
internal sealed class HydraHeadFooter : Decorator
{
    // :443 let info: HydraHeadInfo; :445-472 body
    public HydraHeadFooter(HydraHeadInfo info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:445");
}

/// <summary>:475-489 <c>@MainActor enum HydraLiveInfo</c>, a caseless enum used as a namespace.</summary>
public static class HydraLiveInfo
{
    // :482-488 static func shown(_:_:) -> HydraHeadInfo
    public static HydraHeadInfo Shown(HydraHeadInfo stored, ThreadRuntime? runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:482");
}

/// <summary>:491-553 <c>enum HydraStatusText</c>, a caseless enum used as a namespace: the words for a head's state.</summary>
public static class HydraStatusText
{
    // :493-500 static func short(_:) -> String
    public static string Short(HydraHeadInfo info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:493");

    // :502-513 static func long(_:) -> String
    public static string Long(HydraHeadInfo info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:502");

    // :515-526 static func footer(_:) -> String
    public static string Footer(HydraHeadInfo info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:515");

    // :530-540 static func landed(_:) -> String?
    public static string? Landed(HydraHeadInfo info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraPanel.swift:530");
}
