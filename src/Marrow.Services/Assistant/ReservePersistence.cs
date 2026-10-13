// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.2, 11.7, 15.3; unit AS-13; ED-NEW-dextra-as13-7). No Swift twin.
// Keeps the reserve's three living objects (and, from AS-15, the day's spend that caps are checked against and the duties' health)
// in the assistant's store: the owner's rules and switches (written as soon as he changes
// them, off the thread that changed them: the store's atomic write is disk I/O and the change comes from his click), and the
// calibration and the turn costs (written at most every 30 seconds, since they change with every reading and a disk write per
// token event would be a cost nothing needs). A file this build cannot write (a newer version's) is left alone; a write that
// fails is reported, never thrown into the app. Flush and Dispose write whatever is pending before they return.
namespace Marrow.Services.Assistant;

public sealed class ReservePersistence : IDisposable
{
    /// <summary>The calibration and the turn costs are written at most this often.</summary>
    public static readonly TimeSpan CalibrationEvery = TimeSpan.FromSeconds(30);

    private readonly AssistantStore store;
    private readonly ReserveSettings settings;
    private readonly UsageCalibrator calibrator;
    private readonly TurnCostHistory turns;
    private readonly CostLedger? costs;
    private readonly DutyHealthBook? duties;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly object writing = new();     // one write at a time, so an older document never lands after a newer one
    private ITimer? timer;
    private bool dirty;
    private bool costsDirty;
    private bool dutiesDirty;
    private bool settingsDirty;
    private bool disposed;

    /// <summary>
    /// Loads what the store holds into the three objects, then follows their changes. <paramref name="costs"/> (a day's spend, written
    /// like the calibration, at most every 30 seconds) and <paramref name="duties"/> (written as soon as a duty's health changes) are AS-15's.
    /// </summary>
    public ReservePersistence(AssistantStore store, ReserveSettings settings, UsageCalibrator calibrator, TurnCostHistory turns, TimeProvider? time = null,
        CostLedger? costs = null, DutyHealthBook? duties = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.calibrator = calibrator ?? throw new ArgumentNullException(nameof(calibrator));
        this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
        clock = time ?? TimeProvider.System;
        this.costs = costs;
        this.duties = duties;
        settings.Load(store.Reserves());
        costs?.Load(store.Costs());
        duties?.Load(store.DutyHealth());
        var calibration = store.Calibration();
        calibrator.Load(calibration);
        turns.Load(calibration?["turns"]);
        settings.Changed += SettingsChanged;
        calibrator.Changed += Touch;
        turns.Changed += Touch;
        if (costs is not null) costs.Changed += TouchCosts;
        if (duties is not null) duties.Changed += DutiesChanged;
    }

    /// <summary>A write failed: the line for the board. The app goes on; the next change tries again.</summary>
    public event Action<string>? Faulted;

    // The change came from the owner's click: the write is handed to the pool, and it writes the rules as they are by then.
    private void SettingsChanged()
    {
        lock (gate)
        {
            if (disposed) return;
            settingsDirty = true;
        }
        _ = Task.Run(WriteSettings);
    }

    private void WriteSettings()
    {
        lock (writing)
        {
            lock (gate)
            {
                if (!settingsDirty) return;
                settingsDirty = false;
            }
            if (settings.ToJson() is not { } document) return; // a newer version's file: left as it is
            try { store.SaveReserves(document); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Faulted?.Invoke("The reserve rules could not be saved: " + error.Message);
            }
        }
    }

    private void Touch()
    {
        lock (gate)
        {
            if (disposed) return;
            dirty = true;
            timer ??= clock.CreateTimer(_ => Flush(), null, CalibrationEvery, Timeout.InfiniteTimeSpan);
        }
    }

    // A day's spend changes with every token event: like the calibration, it is written at most every 30 seconds.
    private void TouchCosts()
    {
        lock (gate)
        {
            if (disposed) return;
            costsDirty = true;
            timer ??= clock.CreateTimer(_ => Flush(), null, CalibrationEvery, Timeout.InfiniteTimeSpan);
        }
    }

    // A duty paused by the breaker must stay paused after a restart, so this is written at once, off the thread that changed it.
    private void DutiesChanged()
    {
        lock (gate)
        {
            if (disposed) return;
            dutiesDirty = true;
        }
        _ = Task.Run(WriteDuties);
    }

    private void WriteDuties()
    {
        lock (writing)
        {
            lock (gate)
            {
                if (!dutiesDirty) return;
                dutiesDirty = false;
            }
            if (duties?.ToJson() is not { } document) return; // a newer version's file: left as it is
            try { store.SaveDutyHealth(document); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                lock (gate) dutiesDirty = true;
                Faulted?.Invoke("The duties' health could not be saved: " + error.Message);
            }
        }
    }

    /// <summary>Writes the rules and the calibration and the turn costs now, if they changed, and returns when they are written.</summary>
    public void Flush()
    {
        WriteSettings();
        WriteDuties();
        lock (writing)
        {
            bool writeCalibration, writeCosts;
            lock (gate)
            {
                timer?.Dispose();
                timer = null;
                writeCalibration = dirty;
                writeCosts = costsDirty;
                dirty = false;
                costsDirty = false;
            }
            if (writeCalibration && calibrator.ToJson(turns.ToJson()) is { } document) // a newer version's file: left as it is
            {
                try { store.SaveCalibration(document); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    lock (gate) dirty = true;
                    Faulted?.Invoke("What nth learned about your usage could not be saved: " + error.Message);
                }
            }
            if (writeCosts && costs?.ToJson() is { } spend)
            {
                try { store.SaveCosts(spend); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    lock (gate) costsDirty = true;
                    Faulted?.Invoke("What each assistant spent today could not be saved: " + error.Message);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
        }
        // The last changes are written before nth lets go of the store; after that nothing is listened to.
        Flush();
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
        settings.Changed -= SettingsChanged;
        calibrator.Changed -= Touch;
        turns.Changed -= Touch;
        if (costs is not null) costs.Changed -= TouchCosts;
        if (duties is not null) duties.Changed -= DutiesChanged;
    }
}
