// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.2, 11.7, 15.3; unit AS-13; ED-NEW-dextra-as13-7). No Swift twin.
// Keeps the reserve's three living objects in the assistant's store: the owner's rules and switches (written at once, they are
// his words), and the calibration and the turn costs (written at most every 30 seconds, they change with every reading and a
// disk write per token event would be a cost nothing needs). A file this build cannot write (a newer version's) is left alone; a
// write that fails is reported, never thrown into the app.

namespace Marrow.Services.Assistant;

public sealed class ReservePersistence : IDisposable
{
    /// <summary>The calibration and the turn costs are written at most this often.</summary>
    public static readonly TimeSpan CalibrationEvery = TimeSpan.FromSeconds(30);

    private readonly AssistantStore store;
    private readonly ReserveSettings settings;
    private readonly UsageCalibrator calibrator;
    private readonly TurnCostHistory turns;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private ITimer? timer;
    private bool dirty;
    private bool disposed;

    /// <summary>Loads what the store holds into the three objects, then follows their changes.</summary>
    public ReservePersistence(AssistantStore store, ReserveSettings settings, UsageCalibrator calibrator, TurnCostHistory turns, TimeProvider? time = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.calibrator = calibrator ?? throw new ArgumentNullException(nameof(calibrator));
        this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
        clock = time ?? TimeProvider.System;
        settings.Load(store.Reserves());
        var calibration = store.Calibration();
        calibrator.Load(calibration);
        turns.Load(calibration?["turns"]);
        settings.Changed += SaveSettings;
        calibrator.Changed += Touch;
        turns.Changed += Touch;
    }

    /// <summary>A write failed: the line for the board. The app goes on; the next change tries again.</summary>
    public event Action<string>? Faulted;

    private void SaveSettings()
    {
        if (settings.ToJson() is not { } document) return; // a newer version's file: left as it is
        try { store.SaveReserves(document); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Faulted?.Invoke("The reserve rules could not be saved: " + error.Message);
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

    /// <summary>Writes the calibration and the turn costs now, if they changed.</summary>
    public void Flush()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
            if (!dirty) return;
            dirty = false;
        }
        if (calibrator.ToJson(turns.ToJson()) is not { } document) return; // a newer version's file: left as it is
        try { store.SaveCalibration(document); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            lock (gate) dirty = true;
            Faulted?.Invoke("What nth learned about your usage could not be saved: " + error.Message);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        settings.Changed -= SaveSettings;
        calibrator.Changed -= Touch;
        turns.Changed -= Touch;
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
        // The last changes are written before nth lets go of the store.
        bool pending;
        lock (gate) pending = dirty;
        if (pending && calibrator.ToJson(turns.ToJson()) is { } document)
        {
            try { store.SaveCalibration(document); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Faulted?.Invoke("What nth learned about your usage could not be saved: " + error.Message);
            }
        }
    }
}
