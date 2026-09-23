using Marrow.Core.Port;

// Source: DroppyCode/App/AppUpdater.swift (B2 1fbf81e), :1-528. Skeleton (c100 group 1): the milestones (:8-20),
// the Phase cases (:40-55), the observed stored properties (:37, :57, :63) and the constants (:241-244) are
// complete; every method, every computed property, the private creep and reset state (:65-68), AppUpdater's
// install task (:246), the installer script text (:414-442) and the Downloader's delegate callbacks (:496-527,
// URLSession delegate methods with no .NET counterpart in this form) are left to the implementing unit.
// Forms: URL is SwiftURL; a stored Task is a TaskHandle (R-TASK-1); a Swift Int64 is long.

namespace Marrow.App;

// :8-20. A caseless enum of static lets -> static class of constants.
public static class UpdateProgressMilestones
{
    public const double PreparingCeiling = 0.05; // :10
    public const double DownloadEnd = 0.55;      // :11
    public const double VerifyEnd = 0.70;        // :12
    public const double StagingCeiling = 0.82;   // :14
    public const double HandoffEnd = 0.86;       // :16
    public const double InstallDot = 0.96;       // :18
    public static readonly ValueList<double> Dots = new([DownloadEnd, VerifyEnd, HandoffEnd, InstallDot]); // :19
}

// :27-226. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class UpdateInstallProgress : MainActorModel
{
    // :30
    public static UpdateInstallProgress Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:30");

    // :34-38. `fileprivate(set)`: the containing class sets it, so the setter is internal (a private setter of a
    // nested class is out of the outer class's reach in C#).
    public sealed class FractionTarget : MainActorModel
    {
        private double value = 0.0;

        public double Value { get => Read(value); internal set => Set(ref this.value, value); } // :37
    }

    // :40-55
    public enum Phase
    {
        Idle,        // :41
        Preparing,   // :43
        Downloading, // :44
        Verifying,   // :46
        Staging,     // :48
        Handoff,     // :50
        Celebrating, // :52
        Failed,      // :54
    }

    private Phase phase = Phase.Idle;
    private bool hasPendingCelebration = false;

    // :57. Swift: phase (R-NAME-3).
    public Phase UpdateInstallProgressPhase { get => Read(phase); private set => Set(ref phase, value); }

    // :58. Swift: fractionTarget (R-NAME-3).
    public FractionTarget UpdateInstallProgressFractionTarget { get; } = new();

    // :59
    public double Fraction =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:59");

    // :63
    public bool HasPendingCelebration { get => Read(hasPendingCelebration); private set => Set(ref hasPendingCelebration, value); }

    // :70
    private UpdateInstallProgress() { }

    // :73-78
    public bool ShowsProgressUI =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:73");

    // :80-85
    public bool IsInstallRunning =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:80");

    // :89-95
    public void Begin() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:89");

    // :99-113
    public void NoteDownloadProgress(double downloadFraction) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:99");

    // :115-121
    public void BeginVerification() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:115");

    // :123-129
    public void BeginStaging() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:123");

    // :131-136
    public void NoteHandoff() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:131");

    // :139-149
    public void Fail() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:139");

    // :153-155
    public void ArmCelebration() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:153");

    // :158-166
    public bool ConsumePendingCelebration() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:158");

    // :168-171
    public void FinishCelebrationSweep() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:168");

    // :173-176
    public void CompleteCelebration() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:173");
}

// :237-443. @MainActor final class, not observable: a plain class (R-OBS-1).
public sealed class AppUpdater
{
    // :239
    public static AppUpdater Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:239");

    // :241
    public const string TeamID = "NARHG44L48";

    // :242
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    // :244
    private const double HandoffTimeout = 25;

    // :249
    private AppUpdater() { }

    // :254-259
    public bool ConsumeRelaunchMarker() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:254");

    // :261-283
    public void Install(AvailableUpdate update) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:261");

    // :346-368. Swift: verifySignature(of:).
    public static void VerifySignature(SwiftURL appURL) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:346");
}

// :445-452 (R-ERR-3). The Swift `message` (:446) is read through Exception.Message, which SwiftError answers with
// ErrorDescription (the ShellError form, Shell.swift:22-29).
public sealed class UpdateInstallError(string message) : SwiftError
{
    // :447
    public override string? ErrorDescription => message;
}

// :459-528. `private final class` at file scope -> internal (R-NAME-6); `@unchecked Sendable` with a Mutex
// (R-ISO-5) is the implementing unit's.
internal sealed class Downloader
{
    // :460-463
    private sealed record State
    {
        public TaskCompletionSource<SwiftURL>? Continuation { get; init; } // :461
        public SwiftURL? Destination { get; init; }                        // :462
    }

    // :468-470
    public Downloader(Action<double> onProgress) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:468");

    // :472-486. Swift: download(_:to:).
    public Task Download(SwiftURL url, SwiftURL destination) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppUpdater.swift:472");
}
