using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B2 1fbf81e), :1-845.
// Skeleton (c100 group 4): every type the file declares, private ones included, with the records' shapes and the
// members other files use; every view body and every member with behaviour is left to the implementing unit
// (MARROW_STUB), as are the digest's private parse helpers (:86-185) and the views' private members.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a Swift struct is a sealed record
// (R-VAL-1); Color is SwiftColor. ReleaseNoteCategory has no raw values, so, like HydraMergeStage (Hydra.cs), it is a
// plain C# enum whose CaseIterable order is written out in ReleaseNoteCategoryExtensions.AllCases and whose computed
// members are C# 14 extension members there (R-NAME-4). AboutVersionPill's private enum Standing cannot be extended
// from a top-level static class (it is private and nested), so its three computed properties (:592-614) are private
// static functions of AboutVersionPill named after them. Swift `private`/`fileprivate` top-level types are `internal`.

/// <summary>:12-60 <c>enum ReleaseNoteCategory: CaseIterable, Identifiable</c>: the three sections a release's notes
/// are written in.</summary>
public enum ReleaseNoteCategory
{
    NewFeatures, // :13
    BugFixes, // :14
    Refinements, // :15
}

/// <summary>:12-60 the members of <c>ReleaseNoteCategory</c>.</summary>
public static class ReleaseNoteCategoryExtensions
{
    // CaseIterable: declaration order (:13-15).
    public static IReadOnlyList<ReleaseNoteCategory> AllCases { get; } =
    [
        ReleaseNoteCategory.NewFeatures,
        ReleaseNoteCategory.BugFixes,
        ReleaseNoteCategory.Refinements,
    ];

    extension(ReleaseNoteCategory category)
    {
        // :17 var id: Self { self }
        public ReleaseNoteCategory Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:17");

        // :19-25 var assetName: String
        public string AssetName =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:19");

        // :28-34 var cardTitle: String
        public string CardTitle =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:28");

        // :36-42 var sectionTitle: String
        public string SectionTitle =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:36");

        // :44-50 func countSummary(_ count: Int) -> String
        public string CountSummary(long count) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:44");

        // :53-59 var cardTint: Color
        public SwiftColor CardTint =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:53");
    }
}

/// <summary>:67-186 <c>struct UpdateReleaseNotesDigest: Equatable</c>: a release's notes read into the three
/// sections.</summary>
public sealed record UpdateReleaseNotesDigest
{
    /// <summary>:68-77 <c>struct Section: Equatable, Identifiable</c>.</summary>
    public sealed record Section
    {
        public required ReleaseNoteCategory Category { get; init; } // :69
        public required ValueList<string> Bullets { get; init; } // :70
        public string? Rollup { get; init; } // :72
        public required long RollupCount { get; init; } // :73

        // :75 var id: ReleaseNoteCategory { category }
        public ReleaseNoteCategory Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:75");

        // :76 var changeCount: Int
        public long ChangeCount =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:76");
    }

    public required ValueList<Section> Sections { get; init; } // :79

    // :80 var isEmpty: Bool
    public bool IsEmpty =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:80");

    // :82-84 func section(for:) -> Section? (R-NAME-3: named like the nested type Section; Swift: section)
    public Section? UpdateReleaseNotesDigestSection(ReleaseNoteCategory @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:82");

    // :91-98 @MainActor static func parse(_ raw: String?) -> UpdateReleaseNotesDigest
    public static UpdateReleaseNotesDigest Parse(string? raw) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:91");
}

/// <summary>:190-253 <c>struct UpdateReleaseSectionCards: View</c>: the notes as three tappable cards.</summary>
public sealed class UpdateReleaseSectionCards : Decorator
{
    // :191-192 let digest, let version; :196-203 body
    public UpdateReleaseSectionCards(UpdateReleaseNotesDigest digest, string version) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:196");
}

/// <summary>:255-301 <c>private struct UpdateReleaseSectionPopover: View</c>.</summary>
internal sealed class UpdateReleaseSectionPopover : Decorator
{
    // :256-257 let section, let version; :259-300 body
    public UpdateReleaseSectionPopover(UpdateReleaseNotesDigest.Section section, string version) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:259");
}

/// <summary>:307-337 <c>@MainActor private final class FractionChase</c>: a per-frame exponential chase of a target
/// that only moves forward.</summary>
internal sealed class FractionChase
{
    // :312-314 init(initial:)
    public FractionChase(double initial) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:312");

    // :316-336 func advance(to:target:response:minimumSpeed:) -> Double
    public double Advance(double to, double target, double response, double minimumSpeed) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:316");
}

/// <summary>:341-378 <c>struct SmoothedUpdateProgressSlider: View</c>: hosts the slider and chases the raw
/// target every display frame.</summary>
public sealed class SmoothedUpdateProgressSlider : Decorator
{
    // :353-361 init(target:response:minimumSpeed:trackHeight:headDiameter:); :363-377 body
    public SmoothedUpdateProgressSlider(
        UpdateInstallProgress.FractionTarget target,
        double response = 0.3,
        double minimumSpeed = 0,
        double trackHeight = 14,
        double headDiameter = 20) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:353");
}

/// <summary>:382-439 <c>struct UpdateProgressSlider: View</c>: a pill track filling with blues behind a soft white
/// head.</summary>
public sealed class UpdateProgressSlider : Decorator
{
    // :383-385 let fraction, var trackHeight = 14, var headDiameter = 20; :397-423 body
    public UpdateProgressSlider(double fraction, double trackHeight = 14, double headDiameter = 20) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:397");

    // :426-428 fileprivate static func headX(for:width:trackHeight:) -> CGFloat
    internal static double HeadX(double @for, double width, double trackHeight) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:426");

    // :431-438 fileprivate static func fillOpacity(for:) -> Double
    internal static double FillOpacity(double @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:431");
}

/// <summary>:443-491 <c>private struct FillCanvas: View</c>: the blue fill and its sparkles in one canvas.</summary>
internal sealed class FillCanvas : Decorator
{
    // :444-447 let fraction, trackHeight, colors, sparkles; :449-490 body
    public FillCanvas(double fraction, double trackHeight, IReadOnlyList<SwiftColor> colors, IReadOnlyList<SparkleParticle> sparkles) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:449");
}

/// <summary>:494-524 <c>private struct SparkleParticle</c>: one sparkle on the track.</summary>
internal sealed record SparkleParticle
{
    public required double X { get; init; } // :495
    public required double Y { get; init; } // :496
    public required double Size { get; init; } // :497
    public required double Speed { get; init; } // :498
    public required double Phase { get; init; } // :499
    public required double Drift { get; init; } // :500

    // :502-523 static func field(count:seed:) -> [SparkleParticle]
    public static ValueList<SparkleParticle> Field(long count, ulong seed) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:502");
}

/// <summary>:527-535 <c>struct UpdateAvailableDot: View</c>: the unread dot while a release waits.</summary>
public sealed class UpdateAvailableDot : Decorator
{
    // :528-534 body
    public UpdateAvailableDot() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:528");
}

/// <summary>:542-549 <c>struct AboutUpdateChromeAccessory: View</c>: the About page's chrome.</summary>
public sealed class AboutUpdateChromeAccessory : Decorator
{
    // :543-548 body
    public AboutUpdateChromeAccessory() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:543");
}

/// <summary>:551-571 <c>private struct AboutIdentityPill: View</c>.</summary>
internal sealed class AboutIdentityPill : Decorator
{
    // :552-570 body
    public AboutIdentityPill() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:552");
}

/// <summary>:576-704 <c>private struct AboutVersionPill: View</c>: the one control of the update story.</summary>
internal sealed class AboutVersionPill : Decorator
{
    // :623-685 body
    public AboutVersionPill() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:623");

    /// <summary>:587-615 <c>private enum Standing</c>.</summary>
    private enum Standing
    {
        UpToDate, // :588
        Failed, // :589
        Unchecked, // :590
    }

    // :592-598 Standing.symbol: String
    private static string Symbol(Standing standing) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:592");

    // :600-606 Standing.title: String
    private static string Title(Standing standing) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:600");

    // :608-614 Standing.tint: Color
    private static SwiftColor Tint(Standing standing) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:608");
}

/// <summary>:712-819 <c>struct AboutSoftwareUpdateSection: View</c>: what the About page shows of the update.</summary>
public sealed class AboutSoftwareUpdateSection : Decorator
{
    // :720-744 body
    public AboutSoftwareUpdateSection() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:720");
}

/// <summary>:822-845 <c>struct AboutUpdateCheckRow: View</c>: when GitLab was last asked, and ask again.</summary>
public sealed class AboutUpdateCheckRow : Decorator
{
    // :825-834 body
    public AboutUpdateCheckRow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/AboutUpdateViews.swift:825");
}
