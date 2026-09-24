using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift (B2 1fbf81e), :1-91.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Form (ED-095, R-VIEW-1): a SwiftUI View is a Decorator whose constructor builds `body`. A `[Gradient.Stop]`
// (:47, :66, :82) has no port type yet; it is written as a list of (SwiftColor Color, double Location) pairs, the
// two fields of a SwiftUI Gradient.Stop, pending the implementing unit's choice of drawing (NEEDS DECISION in the
// group report). Not declared yet, private in Swift and read by no other file: the @Environment and @State storage
// (:14-15), halfWidth (:19) and half(of:stops:alignment:) (:47-60), which the implementing unit adds with the body.

namespace Marrow.App.Views.Composer;

/// <summary>:7-91 <c>struct HydraFusedPairIcon: View</c>.</summary>
public sealed class HydraFusedPairIcon : Decorator
{
    // :18 private let gap: CGFloat = 1
    private const double Gap = 1;

    /// <param name="lead">:8 <c>let lead: ProviderKind</c>.</param>
    /// <param name="heads">:9 <c>let heads: ProviderKind</c>.</param>
    /// <param name="size">:10 <c>var size: CGFloat = 15</c>.</param>
    /// <param name="animates">:12 <c>var animates: Bool = true</c>.</param>
    // :21-44 body
    public HydraFusedPairIcon(ProviderKind lead, ProviderKind heads, double size = 15, bool animates = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift:21");

    // :66-74 static func gradient(lead:heads:) -> [Gradient.Stop]
    public static IReadOnlyList<(SwiftColor Color, double Location)> Gradient(ProviderKind lead, ProviderKind heads) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift:66");

    // :76-79 private static func brandColors(_:) -> [Color]
    private static IReadOnlyList<SwiftColor> BrandColors(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift:76");

    // :82-90 private static func spread(_:from:to:) -> [Gradient.Stop]
    private static IReadOnlyList<(SwiftColor Color, double Location)> Spread(IReadOnlyList<SwiftColor> colors, double from, double to) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift:82");
}
