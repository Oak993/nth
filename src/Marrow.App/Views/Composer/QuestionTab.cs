using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/QuestionTab.swift (B2 1fbf81e), :1-332.
// Skeleton (c100 group 4): every type is declared; the bodies (:10-20, :49-194, :208-312) and QuestionAnswers'
// functions (:317-331) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095, R-VIEW-2): a SwiftUI View is a Decorator whose constructor builds `body`; a Swift `private`
// top-level type is `internal`; a case-less enum used as a namespace is a static class.

/// <summary>:5-21 <c>struct QuestionBadgeRow: View</c>: the agent's questions as a badge.</summary>
public sealed class QuestionBadgeRow : Decorator
{
    // :6-7 let request, let runtime; :10-20 body
    public QuestionBadgeRow(QuestionRequest request, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:10");
}

/// <summary>:30-195 <c>struct QuestionSheet: View</c>: the questions, one per page, in the badge's popover.</summary>
public sealed class QuestionSheet : Decorator
{
    // :32 private static let maxHeight: CGFloat = 360
    private const double QuestionSheetMaxHeight = 360; // Swift: maxHeight (clashes with Layoutable.MaxHeight)
    // :33 private static let width: CGFloat = 460
    private const double QuestionSheetWidth = 460; // Swift: width (clashes with Layoutable.Width)

    // :49-66 init(request:runtime:); :76-170 body
    public QuestionSheet(QuestionRequest request, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:49");

    // :69-74 private static func recommended(_:)
    private static ValueList<string> Recommended(QuestionRequest.Question question) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:69");
}

/// <summary>:199-313 <c>private struct QuestionPage: View</c>: one question.</summary>
internal sealed class QuestionPage : Decorator
{
    // :200-206 let question, @Binding selections, @Binding written, let onSubmit, let onPick; :208-245 body
    public QuestionPage(
        QuestionRequest.Question question,
        Binding<ValueMap<string, ValueList<string>>> selections,
        Binding<ValueMap<string, string>> written,
        Action onSubmit,
        Action onPick) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:208");

    // :248-254 private static func bareLabel(_:) -> (label: String, isRecommended: Bool)
    private static (string Label, bool IsRecommended) BareLabel(string label) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:248");
}

/// <summary>:316-332 <c>private enum QuestionAnswers</c>: what the sheet sends.</summary>
internal static class QuestionAnswers
{
    // :317-327 static func collect(_:selections:written:)
    public static ValueMap<string, ValueList<string>> Collect(
        QuestionRequest request,
        IReadOnlyDictionary<string, ValueList<string>> selections,
        IReadOnlyDictionary<string, string> written) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:317");

    // :329-331 static func unansweredCount(_:answers:) -> Int
    public static long UnansweredCount(QuestionRequest request, IReadOnlyDictionary<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/QuestionTab.swift:329");
}
