using System;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B2 1fbf81e), :1-396.
// Skeleton (c100 group 5): every type the file declares, their inputs, the record shapes and the members the other
// files use. Every view body and every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; an enum with associated values is an
// abstract record (R-ENUM-2), an unlabelled payload named Value; Date is DateTimeOffset, a Swift Int long (R-NUM-4),
// Color SwiftColor. Swift `private`/`fileprivate` top-level types are `internal`. `@Environment(AppModel.self)` is
// read by the bodies, so it is no constructor parameter. LimitRow.resetText's `now: Date = .now` default is the
// one-argument overload.
// Not declared yet (private, read only by the bodies the implementing unit writes): BankedResetsView's @State
// storage (:217-218) and its private views and helpers (rows :220-224, statusLine :260-283, countText :285-291),
// BankedResetRow's expiryText (:339-343), control (:345-366) and isApplying (:368-370).

namespace Marrow.App.Views.Usage;

/// <summary>:7-80 <c>struct PlanLimitsView: View</c>: a provider's plan usage limits as the registry last read them.</summary>
public sealed class PlanLimitsView : Decorator
{
    /// <param name="provider">:9 <c>let provider: ProviderKind</c>.</param>
    // :11-79 body
    public PlanLimitsView(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:11");
}

/// <summary>:86-139 <c>struct CreditsView: View</c>: a pay-as-you-go balance as one settings row.</summary>
public sealed class CreditsView : Decorator
{
    /// <param name="provider">:88 <c>let provider: ProviderKind</c>.</param>
    // :90-126 body
    public CreditsView(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:90");

    // :130-138 private func note(for credits: ProviderCredits?, isLoading: Bool) -> String?
    private string? Note(ProviderCredits? @for, bool isLoading) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:130");
}

/// <summary>:141-192 <c>private struct LimitRow: View</c>.</summary>
internal sealed class LimitRow : Decorator
{
    /// <param name="window">:142 <c>let window: PlanLimits.Window</c>.</param>
    // :144-171 body
    public LimitRow(PlanLimits.Window window) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:144");

    // :173-177 private static func tint(for percent: Double) -> Color
    private static SwiftColor Tint(double @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:173");

    // :180-191 static func resetText(_ date: Date, now: Date = .now) -> String, with the default
    public static string ResetText(DateTimeOffset date) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:180");

    // :180-191 static func resetText(_ date: Date, now: Date) -> String
    public static string ResetText(DateTimeOffset date, DateTimeOffset now) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:180");
}

/// <summary>:195-198 <c>fileprivate struct BankedResetRowModel: Identifiable</c>: one banked reset.</summary>
internal sealed record BankedResetRowModel
{
    public required string Id { get; init; } // :196
    public PlanLimits.ResetCredit? Credit { get; init; } // :197
}

/// <summary>:204-308 <c>private struct BankedResetsView: View</c>: a provider's banked resets.</summary>
internal sealed class BankedResetsView : Decorator
{
    /// <summary>:209-215 <c>fileprivate enum Phase: Equatable</c> (R-ENUM-2).</summary>
    public abstract record Phase
    {
        private Phase() { }

        public sealed record Idle : Phase; // :210

        public sealed record Confirming(string Value) : Phase; // :211

        public sealed record Applying(string Value) : Phase; // :212

        public sealed record Done(PlanLimits.ResetOutcome Value) : Phase; // :213

        public sealed record Failed(string Value) : Phase; // :214
    }

    /// <param name="provider">:206 <c>let provider: ProviderKind</c>.</param>
    /// <param name="credits">:207 <c>let credits: PlanLimits.ResetCredits</c>.</param>
    // :226-257 body
    public BankedResetsView(ProviderKind provider, PlanLimits.ResetCredits credits) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:226");

    // :293-307 private func spend(_ row: BankedResetRowModel)
    private void Spend(BankedResetRowModel row) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:293");
}

/// <summary>:313-371 <c>private struct BankedResetRow: View</c>: one banked reset and the button that spends it.</summary>
internal sealed class BankedResetRow : Decorator
{
    /// <param name="row">:314 <c>let row: BankedResetRowModel</c>.</param>
    /// <param name="index">:315 <c>let index: Int</c>.</param>
    /// <param name="several">:316 <c>let several: Bool</c>.</param>
    /// <param name="phase">:317 <c>let phase: BankedResetsView.Phase</c>.</param>
    /// <param name="onConfirm">:318 <c>let onConfirm: () -> Void</c>.</param>
    /// <param name="onCancel">:319 <c>let onCancel: () -> Void</c>.</param>
    /// <param name="onUse">:320 <c>let onUse: () -> Void</c>.</param>
    // :322-337 body
    public BankedResetRow(BankedResetRowModel row, long index, bool several, BankedResetsView.Phase phase, Action onConfirm, Action onCancel, Action onUse) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:322");
}

/// <summary>:374-396 <c>struct UsageBar: View</c>: a thin capsule filled to a fraction.</summary>
public sealed class UsageBar : Decorator
{
    /// <param name="fraction">:375 <c>let fraction: Double</c>.</param>
    /// <param name="tint">:376 <c>let tint: Color</c>.</param>
    /// <param name="label">:379 <c>var label = "Used"</c>.</param>
    // :381-395 body
    public UsageBar(double fraction, SwiftColor tint, string label = "Used") =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageViews.swift:381");
}
