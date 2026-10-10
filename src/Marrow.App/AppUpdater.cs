using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;

// Source: DroppyCode/App/AppUpdater.swift (B3 52bc792), :1-528. The Windows installer boundary is defined by PA-0014.

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
    // PORT-ONLY: an Ambient (not a bare Lazy singleton) so a test can give itself a fresh, isolated instance
    // the way AppInfo.Running and MainActor.Executor already do; production code never overrides it.
    internal static readonly Ambient<UpdateInstallProgress> Running = new(() => new());

    // :30
    public static UpdateInstallProgress Shared => Running.Current;

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
    // UE-1 (OD-0062, OD-0100): the running app's own updater installs through Velopack (SelfUpdateEngine.Shared); an AppUpdater made
    // without an engine keeps the installer-script pipeline of :287-410 over its boundary (the pipeline every earlier test drives).
    private static readonly Ambient<AppUpdater> running = new(() => new(new WindowsUpdateInstallBoundary(), SelfUpdateEngine.Shared));
    private TaskHandle? installTask = null;
    private readonly IUpdateInstallBoundary boundary;
    private readonly ISelfUpdateEngine? engine;

    // :239
    public static AppUpdater Shared => running.Current;

    // :241
    public const string TeamID = "NARHG44L48";

    // :242
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    // :244
    private const double HandoffTimeout = 25;

    // :412-442. PR-UPDATE-056. ENGINEERING DECISION: PowerShell over powershell.exe (WindowsUpdateInstallBoundary.
    // LaunchDetachedInstaller) is the Windows analogue of /bin/sh running install.sh; the three positional
    // arguments ($1 PID, $2 NEW, $3 DEST) are the same argv the source passes, in the same order (:387-391),
    // plus an optional fourth test-only override of the 0.25 s poll interval so a test can drive the "gives
    // up" ceiling (:424, roughly 60 s of real wall-clock polling by an out-of-process script cannot use
    // TestRuntime's manual clock, R-TEST-5) without a multi-second sleep; production never passes it.
    // - trap '' HUP (:417) has no Windows analogue: a detached PowerShell child is not sent a HUP-equivalent
    //   signal when its parent's console closes, so nothing is ignored here (PLATFORM BOUNDARY, no-op).
    // - kill -0 "$PID" (:421) -> Get-Process -Id, an existence probe raising nothing on absence.
    // - xattr -dr com.apple.quarantine (:439) has no analogue on Windows (no quarantine attribute); the
    //   record's own windows_path says so. Nothing runs in its place.
    // - open "$DEST" (:440) relaunches the installed app. DEVIATION (platform gap, PA-0014/UNK-0009): the
    //   Windows release artifact's shape (a single executable at DEST, or a directory) is not decided until
    //   a Windows release exists, so this mirrors the source structurally -- one unconditional launch call,
    //   no error check -- rather than inventing a specific executable name inside DEST.
    // - PA-0015: Remove-Item/Move-Item retry with backoff, because a just-exited process's file handles can
    //   still linger on Windows.
    // internal (not private): so a test can write this exact text to its own script file and run it as a
    // real process, the way AppUpdaterInstallerScriptTests does, without going through the blocked Stage step.
    internal const string InstallerScript = """
        param(
            [Parameter(Mandatory = $true)][int]$PID_,
            [Parameter(Mandatory = $true)][string]$New,
            [Parameter(Mandatory = $true)][string]$Dest,
            [double]$PollIntervalSeconds = 0.25
        )
        $ErrorActionPreference = 'Stop'
        $Staging = Split-Path -Path $New -Parent

        function Remove-Tree($Path) {
            for ($attempt = 0; $attempt -lt 5; $attempt++) {
                try {
                    if (Test-Path -LiteralPath $Path) {
                        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                            ForEach-Object { $_.Attributes = 'Normal' }
                        Remove-Item -LiteralPath $Path -Recurse -Force
                    }
                    return
                } catch {
                    Start-Sleep -Milliseconds (100 * ($attempt + 1))
                }
            }
        }

        function Move-Tree($From, $To) {
            for ($attempt = 0; $attempt -lt 5; $attempt++) {
                try {
                    Move-Item -LiteralPath $From -Destination $To -Force
                    return $true
                } catch {
                    Start-Sleep -Milliseconds (100 * ($attempt + 1))
                }
            }
            return $false
        }

        $i = 0
        while (Get-Process -Id $PID_ -ErrorAction SilentlyContinue) {
            Start-Sleep -Seconds $PollIntervalSeconds
            $i++
            if ($i -gt 240) {
                Write-Output 'The app did not quit'
                Remove-Tree $Staging
                exit 1
            }
        }

        $Backup = Join-Path (Split-Path -Path $Dest -Parent) ('.' + (Split-Path -Path $Dest -Leaf) + '.previous')
        Remove-Tree $Backup
        if (Test-Path -LiteralPath $Dest) {
            if (-not (Move-Tree $Dest $Backup)) {
                Write-Output 'Could not move the installed app aside'
                Remove-Tree $Staging
                exit 1
            }
        }

        if (Move-Tree $New $Dest) {
            Remove-Tree $Backup
        } else {
            Write-Output 'Could not move the update into place'
            if (Test-Path -LiteralPath $Backup) {
                Move-Tree $Backup $Dest | Out-Null
            }
            Remove-Tree $Staging
            exit 1
        }

        try {
            # macOS `open "$DEST"` (:440) launches the app bundle at $Dest directly: Launch Services resolves a
            # .app bundle (a directory) to its Info.plist executable. Windows has no bundle concept: $Dest is
            # only the install folder, so the executable inside it must be found and launched explicitly, or
            # Start-Process falls back to the shell's default "open" action for a plain folder (Explorer) (ED-192).
            $exe = Get-ChildItem -LiteralPath $Dest -Filter '*.exe' -File | Select-Object -First 1
            if ($exe) { Start-Process -FilePath $exe.FullName }
        } catch {
            # :440's `open` result is unchecked; a relaunch failure does not undo a completed swap.
        }
        Remove-Tree $Staging
        exit 0
        """;

    // :249. PORT-ONLY: this overload (ED-188) lets tests inject a fake IUpdateInstallBoundary without an
    // InternalsVisibleTo edit (ED-119 already declined that need for this unit).
    private AppUpdater() : this(new WindowsUpdateInstallBoundary()) { }

    public AppUpdater(IUpdateInstallBoundary boundary, ISelfUpdateEngine? engine = null)
    {
        this.boundary = boundary;
        this.engine = engine;
    }

    private static UpdateInstallProgress Progress => UpdateInstallProgress.Shared;

    // :254-259
    public bool ConsumeRelaunchMarker()
    {
        var defaults = UserDefaults.Standard.Current;
        if (defaults.String(RelaunchVersionKey) is not { } pending) return false;
        defaults.RemoveObject(RelaunchVersionKey);
        return string.Equals(pending, AppInfo.Current.Version, StringComparison.Ordinal);
    }

    // :261-283
    public void Install(AvailableUpdate update)
    {
        if (installTask is not null || AppInfo.Current.IsDevelopment) return; // :262

        installTask = MainActor.Spawn(async () =>
        {
            try
            {
                Progress.Begin(); // :265
                var image = engine is null ? await Download(update.DownloadURL) : await DownloadPackage(update.Version); // :267
                Progress.BeginVerification(); // :268
                var staged = await Stage(image, update.Version); // :269
                UpdateChecker.RememberDownloadedRelease(update); // UE-6: the verified package carries this captured offer, even if the checker has moved on
                Progress.BeginStaging(); // :270
                if (engine is null) HandOff(staged, update.Version); // :271
                else HandOffToVelopack(update.Version);
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                await ShowInstallFailure(error);
            }
            finally
            {
                installTask = null; // :264 defer { installTask = nil }
            }
        });
    }

    // :273-280. PR-UPDATE-057 / UI-UPDATE-034: the modal "Update Failed" alert, run modally
    // (await, not fire-and-forget) so the install task does not finish and clear installTask until the
    // alert is dismissed, matching runModal's block within the same MainActor.run closure.
    private static async Task ShowInstallFailure(Exception error)
    {
        Progress.Fail(); // :273
        await AlertPresentation.Presenter.Current.Show(
            "Update Failed", // :276, title case, no full stop
            SwiftErrors.LocalizedDescription(error), // :277
            AlertStyle.Warning, // :278
            [new AlertButton("OK", () => { })]); // :279, the single default dismiss button, no retry affordance
    }

    // UE-5 (OD-0100 "Restart Marrow"): Update everything has already downloaded the package through the engine (MarrowUpdateItem), so
    // this is :265-272 from the verify step on: verify, stage, hand off to Velopack's updater and quit. The same single-flight guard, the
    // same development-build refusal and the same "Update Failed" alert as Install; an AppUpdater made without an engine does nothing.
    public void ApplyDownloaded(string package, string version)
    {
        if (installTask is not null || AppInfo.Current.IsDevelopment || engine is null) return;
        installTask = MainActor.Spawn(async () =>
        {
            try
            {
                Progress.Begin();
                Progress.BeginVerification(); // :268
                await Stage(package, version); // :269
                Progress.BeginStaging(); // :270
                HandOffToVelopack(version);
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                await ShowInstallFailure(error);
            }
            finally
            {
                installTask = null;
            }
        });
    }

    // :299-341 stage(image:expectedVersion:). PORT-ONLY: internal (not private) so a test can drive it
    // directly against a FakeUpdateInstallBoundary without going through the whole install() pipeline.
    internal async Task<string> Stage(string image, string expectedVersion)
    {
        var staged = await boundary.Stage(image, expectedVersion); // :299-330
        boundary.Verify(staged); // :337-340, :346-368 (collapsed per PR-UPDATE-051 windows_path)
        var version = boundary.ReadVersion(staged); // :332-333
        if (version is null ||
            AppVersion.Create(version) is not { } actual ||
            AppVersion.Create(expectedVersion) is not { } expected ||
            actual != expected)
        {
            throw new UpdateInstallError($"The downloaded app is version {version ?? "unknown"}, not {expectedVersion}."); // :334-336
        }
        return staged;
    }

    // :287-295
    private static async Task<string> Download(SwiftURL url)
    {
        // :288-289. DEVIATION OD-0012 F1 ("Droppy Code" -> "Marrow"). No extension: UNK-0009 means the
        // Windows artifact format, and so its extension, is not decided.
        var destination = Path.Combine(Path.GetTempPath(), $"MarrowUpdate-{SwiftUUID.UuidString(Guid.NewGuid())}");
        var downloader = new Downloader(fraction =>
            MainActor.Spawn(() => { UpdateInstallProgress.Shared.NoteDownloadProgress(fraction); return Task.CompletedTask; })); // :290-291
        await downloader.Download(url, destination); // :293
        return destination; // :294
    }

    // UE-1 :287-295 through Velopack: its UpdateManager downloads the full package (a delta when it can) and verifies the feed's
    // checksum; its percent is the byte ratio the pill takes (AppUpdater.swift:290-291, :496-502). A failure is worded as :523-527.
    private async Task<string> DownloadPackage(string version)
    {
        try
        {
            return await engine!.DownloadAsync(version, percent =>
                MainActor.Spawn(() => { UpdateInstallProgress.Shared.NoteDownloadProgress(SelfUpdateProgress.Fraction(percent)); return Task.CompletedTask; }),
                CancellationToken.None);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error) && error is not (UpdateError or UpdateInstallError))
        {
            // :523-527. Per-site decision (R-ERR-2, OD-0056: the platform's own error text, not Apple's): Velopack's and Windows'
            // exceptions have no Swift text, so their own message follows the sentence.
            throw new UpdateInstallError($"The update could not be downloaded. {error.Message}");
        }
    }

    // UE-1 :372-410 over Velopack: the same writability gate and the same hand-off timing, with Velopack's Update.exe in place of
    // install.ps1: it waits for this process to exit, swaps the install and starts the new Marrow ("Restart Marrow").
    internal void HandOffToVelopack(string version, string? destinationOverride = null)
    {
        var destination = destinationOverride ?? InstalledAppDirectory(); // :375
        var parent = SwiftPath.DeletingLastPathComponent(destination); // :376
        if (!IsWritableDirectory(parent))
            throw new UpdateInstallError($"nth cannot replace itself in {parent}. Reinstall it with the installer and try again."); // :377-379
        try { engine!.WaitExitThenApplyAndRestart(); } // :385-395
        catch (Exception error) when (SwiftTry.IsThrownError(error) && error is not UpdateInstallError)
        {
            throw new UpdateInstallError(error.Message); // per-site decision as DownloadPackage: the platform's own text (OD-0056)
        }
        FinishHandOff(version);
    }

    // :372-410. PORT-ONLY: internal with an optional destinationOverride (not part of the Swift, which reads
    // Bundle.main.bundleURL directly) so a test can point the writability gate at a directory it controls.
    internal void HandOff(string staged, string version, string? destinationOverride = null)
    {
        var destination = destinationOverride ?? InstalledAppDirectory(); // :375
        var parent = SwiftPath.DeletingLastPathComponent(destination); // :376
        if (!IsWritableDirectory(parent))
        {
            // :377-379, DEVIATION OD-0008 B15; OD-0012 H, F1.
            throw new UpdateInstallError($"nth cannot replace itself in {parent}. Reinstall it with the installer and try again.");
        }

        var stagingDirectory = SwiftPath.DeletingLastPathComponent(staged); // :380
        var script = SwiftPath.AppendingPathComponent(stagingDirectory, "install.ps1"); // :380-381, ENGINEERING DECISION: PowerShell; see WindowsUpdateInstallBoundary
        File.WriteAllText(script, InstallerScript); // :381
        var log = SwiftPath.AppendingPathComponent(stagingDirectory, "install.log"); // :382-384

        boundary.LaunchDetachedInstaller(Environment.ProcessId, staged, destination, script, log); // :385-395
        FinishHandOff(version);
    }

    // :396-409, shared by both pipelines.
    private static void FinishHandOff(string version)
    {
        UserDefaults.Standard.Current.Set(RelaunchVersionKey, version); // :396
        Progress.NoteHandoff(); // :397

        // :399-404. If the quit is refused the installer gives up on its own; the pill goes back too.
        MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(SwiftTask.Seconds(HandoffTimeout)); // :400
            if (UpdateInstallProgress.Shared.UpdateInstallProgressPhase != UpdateInstallProgress.Phase.Handoff) return; // :401
            UserDefaults.Standard.Current.RemoveObject(RelaunchVersionKey); // :402
            UpdateInstallProgress.Shared.Fail(); // :403
        });

        // :405-409. Reuse the shell's bound termination request (AppShell.FinishLaunching), including
        // ApplicationShouldTerminate and the lifetime's shutdown; no separate shutdown path.
        MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(350)); // :407
            WindowManager.Shared.MainWindowClose?.Invoke(); // :408 NSApp.terminate(nil)
        });
    }

    // :375. ENGINEERING DECISION: the Windows analogue of Bundle.main.bundleURL, the running app's own
    // install directory, is the folder holding its executable.
    private static string InstalledAppDirectory() =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, '/');

    // :377. FileManager.default.isWritableFile(atPath:). ENGINEERING DECISION: Windows ACL inspection
    // alone can be wrong under deny rules and UAC virtualization, so this probes with a real temporary file.
    private static bool IsWritableDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        var probe = Path.Combine(path, $".marrow-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

// :445-452
public sealed class UpdateInstallError(string message) : SwiftError
{
    // :447
    public override string? ErrorDescription => message;
}

// :459-528. ED-119 keeps AppUpdater's downloadTask-with-delegate surface out of F.HTTP (PA-0014 replaces
// this install path); this streams the same ephemeral-session request over URLSession.Bytes instead,
// reporting the same byte ratio (:496-502) while writing chunks straight to the destination file.
internal sealed class Downloader
{
    private readonly Action<double> onProgress;

    // :468-470
    public Downloader(Action<double> onProgress) => this.onProgress = onProgress;

    // :472-475. PORT-ONLY: split out so a test can assert the literal timeout and connectivity values
    // without needing a real, possibly slow, network round trip to observe them.
    internal static URLSessionConfiguration BuildConfiguration()
    {
        var configuration = URLSessionConfiguration.Ephemeral; // :472
        configuration.TimeoutIntervalForRequest = 30; // :473
        configuration.TimeoutIntervalForResource = 1800; // :474
        configuration.WaitsForConnectivity = false; // :475
        return configuration;
    }

    // :481, DEVIATION OD-0012 F1. PORT-ONLY: split out for the same reason as BuildConfiguration above.
    internal static URLRequest BuildRequest(SwiftURL url) =>
        new URLRequest(new Uri(url.AbsoluteString)).SetValue($"nth/{AppInfo.Current.Version}", "User-Agent");

    // :472-486, :496-502, :504-521, :523-527
    public async Task Download(SwiftURL url, string destination)
    {
        var configuration = BuildConfiguration();
        var session = new URLSession(configuration);
        var request = BuildRequest(url);

        URLSession.AsyncBytes bytes;
        URLResponse response;
        try
        {
            (bytes, response) = await session.Bytes(request); // :472, one request-shaped fetch replacing the delegate-driven downloadTask
        }
        catch (CancellationError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new UpdateInstallError($"The update could not be downloaded. {SwiftErrors.LocalizedDescription(error)}"); // :523-527
        }

        var http = response as HTTPURLResponse;
        var expected = http?.ExpectedContentLength ?? -1;
        var buffer = new byte[81_920];
        var index = 0;
        var file = File.Create(destination);
        try
        {
            long written = 0;
            await foreach (var b in bytes)
            {
                buffer[index++] = b;
                if (index < buffer.Length) continue;
                await file.WriteAsync(buffer.AsMemory(0, index));
                written += index;
                index = 0;
                onProgress(expected > 0 ? (double)written / expected : 0); // :497-502
            }
            if (index > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, index));
                written += index;
            }
            onProgress(expected > 0 ? (double)written / expected : 0); // :497-502
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            await file.DisposeAsync();
            try { File.Delete(destination); } catch (IOException) { }
            throw new UpdateInstallError($"The update could not be downloaded. {SwiftErrors.LocalizedDescription(error)}"); // :523-527
        }
        finally
        {
            await file.DisposeAsync();
        }

        if (http is null)
            throw new UpdateInstallError("The download had nowhere to go."); // :508-509
        if (http.StatusCode is < 200 or > 299)
            throw new UpdateInstallError($"The download failed with HTTP {http.StatusCode}."); // :510-512
    }
}
