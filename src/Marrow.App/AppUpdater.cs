using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/App/AppUpdater.swift (B2 1fbf81e), :1-528. The Windows installer boundary is defined by PA-0014.

namespace Marrow.App;

// :8-20
public static class UpdateProgressMilestones
{
    public const double PreparingCeiling = 0.05; // :10
    public const double DownloadEnd = 0.55; // :11
    public const double VerifyEnd = 0.70; // :12
    public const double StagingCeiling = 0.82; // :14
    public const double HandoffEnd = 0.86; // :16
    public const double InstallDot = 0.96; // :18
    public static readonly ValueList<double> Dots = new([DownloadEnd, VerifyEnd, HandoffEnd, InstallDot]); // :19
}

// :27-226
public sealed class UpdateInstallProgress : MainActorModel
{
    private static readonly Ambient<UpdateInstallProgress> running = new(() => new());

    // :30
    public static UpdateInstallProgress Shared => running.Current;

    // :34-38
    public sealed class FractionTarget : MainActorModel
    {
        private double value;

        public double Value { get => Read(value); internal set => Set(ref this.value, value); } // :37
    }

    // :40-55
    public enum Phase
    {
        Idle,
        Preparing,
        Downloading,
        Verifying,
        Staging,
        Handoff,
        Celebrating,
        Failed,
    }

    private Phase phase = Phase.Idle;
    private bool hasPendingCelebration;
    private TaskHandle? creepTask;
    private double creepCeiling;
    private double creepRate;
    private TaskHandle? failureReset;

    // :57
    public Phase UpdateInstallProgressPhase { get => Read(phase); private set => Set(ref phase, value); }

    // :58
    public FractionTarget UpdateInstallProgressFractionTarget { get; } = new();

    // :59
    public double Fraction => UpdateInstallProgressFractionTarget.Value;

    // :63
    public bool HasPendingCelebration { get => Read(hasPendingCelebration); private set => Set(ref hasPendingCelebration, value); }

    // :70
    private UpdateInstallProgress() { }

    // :73-78
    public bool ShowsProgressUI => UpdateInstallProgressPhase is not (Phase.Idle or Phase.Failed);

    // :80-85
    public bool IsInstallRunning => UpdateInstallProgressPhase is
        Phase.Preparing or Phase.Downloading or Phase.Verifying or Phase.Staging or Phase.Handoff;

    // :89-95
    public void Begin()
    {
        CancelFailureReset();
        UpdateInstallProgressPhase = Phase.Preparing;
        UpdateInstallProgressFractionTarget.Value = 0.004;
        Haptics.Perform(HapticPattern.Generic);
        StartCreep(UpdateProgressMilestones.PreparingCeiling, 0.05);
    }

    // :99-113
    public void NoteDownloadProgress(double downloadFraction)
    {
        if (UpdateInstallProgressPhase == Phase.Preparing)
            UpdateInstallProgressPhase = Phase.Downloading;
        else if (UpdateInstallProgressPhase != Phase.Downloading)
            return;

        if (!(downloadFraction > 0))
        {
            StartCreep(0.50, 0.012);
            return;
        }

        StopCreep();
        Apply(UpdateProgressMilestones.PreparingCeiling +
              (UpdateProgressMilestones.DownloadEnd - UpdateProgressMilestones.PreparingCeiling) *
              Math.Min(downloadFraction, 1));
    }

    // :115-121
    public void BeginVerification()
    {
        if (!IsInstallRunning) return;
        StopCreep();
        UpdateInstallProgressPhase = Phase.Verifying;
        Apply(UpdateProgressMilestones.DownloadEnd);
        StartCreep(UpdateProgressMilestones.VerifyEnd - 0.02, 0.12);
    }

    // :123-129
    public void BeginStaging()
    {
        if (!IsInstallRunning) return;
        StopCreep();
        UpdateInstallProgressPhase = Phase.Staging;
        Apply(UpdateProgressMilestones.VerifyEnd);
        StartCreep(UpdateProgressMilestones.StagingCeiling, 0.10);
    }

    // :131-136
    public void NoteHandoff()
    {
        if (!IsInstallRunning) return;
        StopCreep();
        UpdateInstallProgressPhase = Phase.Handoff;
        Apply(UpdateProgressMilestones.HandoffEnd);
    }

    // :139-149
    public void Fail()
    {
        if (!IsInstallRunning) return;
        StopCreep();
        UpdateInstallProgressPhase = Phase.Failed;
        Haptics.Perform(HapticPattern.LevelChange);
        failureReset = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(600));
            if (SwiftTask.IsCancelled || UpdateInstallProgressPhase != Phase.Failed) return;
            ResetToIdle();
        });
    }

    // :153-155
    public void ArmCelebration() => HasPendingCelebration = true;

    // :158-166
    public bool ConsumePendingCelebration()
    {
        if (!HasPendingCelebration) return false;
        HasPendingCelebration = false;
        StopCreep();
        CancelFailureReset();
        UpdateInstallProgressPhase = Phase.Celebrating;
        UpdateInstallProgressFractionTarget.Value = UpdateProgressMilestones.HandoffEnd;
        return true;
    }

    // :168-171
    public void FinishCelebrationSweep()
    {
        if (UpdateInstallProgressPhase != Phase.Celebrating) return;
        Apply(1, firesHaptics: false);
    }

    // :173-176
    public void CompleteCelebration()
    {
        if (UpdateInstallProgressPhase != Phase.Celebrating) return;
        ResetToIdle();
    }

    // :180-185
    private void ResetToIdle()
    {
        StopCreep();
        CancelFailureReset();
        UpdateInstallProgressPhase = Phase.Idle;
        UpdateInstallProgressFractionTarget.Value = 0;
    }

    // :187-190
    private void CancelFailureReset()
    {
        failureReset?.Cancel();
        failureReset = null;
    }

    // :193-202
    private void Apply(double newValue, bool firesHaptics = true)
    {
        var clamped = 1 < (0 >= newValue ? 0 : newValue) ? 1 : (0 >= newValue ? 0 : newValue);
        if (!(clamped > Fraction)) return;
        var previous = Fraction;
        UpdateInstallProgressFractionTarget.Value = clamped;
        if (!firesHaptics) return;

        foreach (var dot in UpdateProgressMilestones.Dots)
        {
            if (previous < dot && clamped >= dot)
                Haptics.Perform(dot == UpdateProgressMilestones.HandoffEnd
                    ? HapticPattern.LevelChange
                    : HapticPattern.Alignment);
        }
    }

    // :204-220
    private void StartCreep(double ceiling, double rate)
    {
        creepCeiling = ceiling;
        creepRate = rate;
        if (creepTask is not null) return;

        creepTask = MainActor.Spawn(async () =>
        {
            while (!SwiftTask.IsCancelled)
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(125));
                if (SwiftTask.IsCancelled) return;
                var remaining = creepCeiling - Fraction;
                if (remaining <= 0.0005)
                {
                    creepTask = null;
                    return;
                }
                Apply(Fraction + remaining * creepRate);
            }
        });
    }

    // :222-225
    private void StopCreep()
    {
        creepTask?.Cancel();
        creepTask = null;
    }
}

// :237-443
public sealed class AppUpdater
{
    private static readonly Ambient<AppUpdater> running = new(() => new());
    private TaskHandle? installTask = null;

    // :239
    public static AppUpdater Shared => running.Current;

    // :241
    public const string TeamID = "NARHG44L48";

    // :242
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    // :244
    private const double HandoffTimeout = 25;

    // :249
    private AppUpdater() { }

    // :254-259
    public bool ConsumeRelaunchMarker()
    {
        var defaults = UserDefaults.Standard.Current;
        if (defaults.String(RelaunchVersionKey) is not { } pending) return false;
        defaults.RemoveObject(RelaunchVersionKey);
        return pending == AppInfo.Current.Version;
    }

    // :261-283
    public void Install(AvailableUpdate update)
    {
        if (installTask is not null || AppInfo.Current.IsDevelopment) return;

        // DEVIATION PA-0014: the current public release feed has no qualifying Windows release asset (UNK-0009).
        // Do not start an install or present a fabricated failure for an update that cannot qualify on Windows.
        _ = update;
    }

    // :346-368
    public static void VerifySignature(SwiftURL appURL)
    {
        // DEVIATION PA-0014: no Windows release qualifies until the feed publishes a Windows asset and its publisher
        // identity is established; the Apple Developer ID requirement has no Windows meaning.
        _ = appURL;
        throw new PlatformNotSupportedException();
    }
}

// :445-452
public sealed class UpdateInstallError(string message) : SwiftError
{
    // :447
    public override string? ErrorDescription => message;
}

// :459-528
internal sealed class Downloader
{
    private readonly Action<double> onProgress;

    // :468-470
    public Downloader(Action<double> onProgress) => this.onProgress = onProgress;

    // :472-486
    public Task Download(SwiftURL url, SwiftURL destination)
    {
        // DEVIATION PA-0014: this download path is unreachable until a qualifying Windows release asset is published.
        _ = onProgress;
        _ = url;
        _ = destination;
        throw new PlatformNotSupportedException();
    }
}
