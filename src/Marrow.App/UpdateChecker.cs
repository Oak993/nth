using Marrow.Core.Port;

// Source: DroppyCode/App/UpdateChecker.swift (B2 1fbf81e), :1-327. Skeleton (c100 group 1): AppVersion's stored
// components (:8), AvailableUpdate's and GitLabRelease's stored shapes (:41-51, :278-304), the keys (:77-81) and
// the constants (:67, :74-75) are complete; the failable init (:10-22), the comparison (:35-37), every computed
// property, the Codable mappings (AvailableUpdate's synthesized keys, GitLabRelease's CodingKeys :293-296,
// :306-311 and init(from:) :313-321), UpdateChecker's init (:112-126), its URLSession (:101-110) and every method
// are left to the implementing unit. Forms: URL is SwiftURL, Date is a UTC DateTimeOffset (R-TIME-1), a Swift Int
// is long (R-NUM-4), a failable init is a static Create (R-NAME-5), Comparable is IComparable<T> (R-VAL-4).

namespace Marrow.App;

// :7-38 (R-VAL-1, R-VAL-4)
public sealed record AppVersion : IComparable<AppVersion>
{
    private AppVersion(ValueList<long> components) => Components = components;

    public ValueList<long> Components { get; } // :8

    // :10-22 init?(_ raw: String)
    public static AppVersion? Create(string raw) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:10");

    // :24-26 CustomStringConvertible
    public string Description =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:24");

    // :29-33. Swift: text(ofTag:).
    public static string Text(string ofTag) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:29");

    // :35-37
    public int CompareTo(AppVersion? other) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:35");

    // :35-37 and the Comparable operators Swift derives from `<`.
    public static bool operator <(AppVersion lhs, AppVersion rhs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:35");

    public static bool operator >(AppVersion lhs, AppVersion rhs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:35");

    public static bool operator <=(AppVersion lhs, AppVersion rhs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:35");

    public static bool operator >=(AppVersion lhs, AppVersion rhs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:35");
}

// :41-51. Codable (synthesized keys) is the implementing unit's.
public sealed record AvailableUpdate
{
    public required string Version { get; init; }          // :42
    public required string Tag { get; init; }              // :43
    public required SwiftURL DownloadURL { get; init; }    // :44
    public long? Size { get; init; }                       // :45
    public required string Notes { get; init; }            // :47
    public DateTimeOffset? ReleasedAt { get; init; }       // :48
    public required SwiftURL PageURL { get; init; }        // :50
}

// :61-263. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class UpdateChecker : MainActorModel
{
    // :64
    public static UpdateChecker Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:64");

    // :67
    public const string ProjectID = "86415974";

    // :68
    public static SwiftURL ProjectURL =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:68");

    // :69
    public static SwiftURL ReleasesURL =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:69");

    // :74-75
    private const double CheckInterval = 6 * 60 * 60;
    private const double LaunchDelay = 8;

    // :77-81
    private static class Keys
    {
        public const string Update = "updateChecker_release";
        public const string LastChecked = "updateChecker_lastChecked";
        public const string NotifiedVersion = "updateChecker_notifiedVersion";
    }

    private AvailableUpdate? update;
    private bool isChecking;
    private DateTimeOffset? lastCheckedAt;
    private string? lastError;

    // :83
    public string CurrentVersion =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:83");

    public AvailableUpdate? Update { get => Read(update); private set => Set(ref update, value); }             // :85
    public bool IsChecking { get => Read(isChecking); private set => Set(ref isChecking, value); }              // :86
    public DateTimeOffset? LastCheckedAt { get => Read(lastCheckedAt); private set => Set(ref lastCheckedAt, value); } // :87
    public string? LastError { get => Read(lastError); private set => Set(ref lastError, value); }             // :89

    // :93-96
    public bool UpdateAvailable =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:93");

    // :112-126
    private UpdateChecker() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:112");

    // :132-150
    public void StartBackgroundChecks() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:132");

    // :156-187. @discardableResult.
    public Task<bool> Check(bool background = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:157");

    // :199-219. Swift: newestRelease(in:).
    public static AvailableUpdate? NewestRelease(IReadOnlyList<GitLabRelease> releases) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:199");
}

// :265-275 (R-ERR-3). BadResponse's unlabeled Int is named after the binding errorDescription gives it (`let status`).
public sealed class UpdateError(UpdateError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record NoRelease : Case;               // :266
        public sealed record BadResponse(long Status) : Case; // :267
    }

    public Case ErrorCase { get; } = errorCase;

    // :269-274
    public override string? ErrorDescription =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:269");
}

// :278-327. Decodable with a custom init(from:) (:313-321): the decoding is the implementing unit's.
public sealed record GitLabRelease
{
    // :279-281
    public sealed record Assets
    {
        public required ValueList<Link> Links { get; init; } // :280
    }

    // :283-297
    public sealed record Link
    {
        public required string Name { get; init; }      // :284
        public required string Url { get; init; }       // :285
        public string? DirectAssetURL { get; init; }    // :286

        // :288-291
        public bool IsDiskImage =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:288");
    }

    public required string TagName { get; init; }                  // :299
    public string? Name { get; init; }                             // :300
    public string? Description { get; init; }                      // :301
    public DateTimeOffset? ReleasedAt { get; init; }               // :302
    public required bool UpcomingRelease { get; init; }            // :303
    public required Assets GitLabReleaseAssets { get; init; }      // :304 Swift: assets (R-NAME-3)

    // :324-326
    public static DateTimeOffset? ParseDate(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/UpdateChecker.swift:324");
}
