// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the offer's model.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice.Cuda;

/// <summary>
/// The CUDA offer of Settings &gt; Voice typing &gt; Speech model (OD-0106 CUDA A, OD-0099 Q-V-9 C): shown only on a PC with an NVIDIA graphics card whose hardware probe finished,
/// while CUDA is not already the working backend and while an installer for NVIDIA's libraries is bound; one Download starts the pack through VT-7's installer (on the shared
/// downloader) and then the libraries; progress counts both by their bytes; the x cancels and keeps the partial; the end states are Installed (the next start uses it: the page's
/// "Restart nth to use it", ED-351) or Failed (Download again). <see cref="Changed"/> may be raised from a download thread: the view marshals it. The row's drawing in
/// the hardware card is mounted by CudaOfferComposition and VoiceHardwareCard.
/// </summary>
public sealed class CudaOffer : IDisposable
{
    private readonly IHardwareProbe probe;
    private readonly CudaPackInstaller installer;
    private readonly CudaRuntimePack pack;
    private readonly ICudaCompanions companions;
    private readonly Lock gate = new();
    private CudaOfferRow row = CudaOfferRow.Hidden;
    private string size = "";
    private CancellationTokenSource? cancel;
    private Task running = Task.CompletedTask;
    private double progress;

    public CudaOffer(IHardwareProbe probe, CudaPackInstaller installer, CudaRuntimePack pack, ICudaCompanions companions)
    {
        this.probe = probe;
        this.installer = installer;
        this.pack = pack;
        this.companions = companions;
    }

    public CudaOfferRow Row
    {
        get
        {
            lock (gate) return row;
        }
    }

    /// <summary>The row changed (a new state or a new percentage).</summary>
    public event Action<CudaOfferRow>? Changed;

    /// <summary>Runs the hardware probe and decides whether, and in which state, the row shows. A probe that fails hides the row; a cancelled one leaves it as it was.</summary>
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        lock (gate) { if (cancel is not null) return; }
        if (!companions.Available)
        {
            SetEvaluation(CudaOfferRow.Hidden);
            return;
        }
        HardwareProfile profile;
        try
        {
            profile = await probe.RunAsync(null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            SetEvaluation(CudaOfferRow.Hidden); // fail closed: nothing is offered on a PC the probe could not read
            return;
        }
        if (profile.GpuVendor != GpuVendor.Nvidia || profile.Backend == GpuBackend.Cuda)
        {
            SetEvaluation(CudaOfferRow.Hidden);
            return;
        }
        size = CudaOfferStates.SizeText(pack, companions);
        try
        {
            var installed = await Task.Run(() => installer.IsInstalled(pack) && companions.IsInstalled(installer.FolderFor(pack)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetEvaluation(installed ? CudaOfferStates.Installed(size) : CudaOfferStates.Offer(size));
        }
        catch (OperationCanceledException) { /* A cancelled evaluation leaves the previous row. */ }
    }

    /// <summary>The Download capsule. While a download runs, a second press is that same download. Nothing happens while the row is hidden or installed.</summary>
    public Task StartAsync()
    {
        Task started;
        var beginning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CudaOfferRow shown;
        lock (gate)
        {
            if (row.State == CudaOfferState.Downloading) return running;
            if (row.State is not (CudaOfferState.Offer or CudaOfferState.Failed)) return Task.CompletedTask;
            var source = cancel = new CancellationTokenSource();
            progress = 0;
            shown = row = CudaOfferStates.Downloading(size, 0);
            started = running = Task.Run(async () =>
            {
                await beginning.Task;
                await Run(source); // Receipt/native hashing and extraction never block the UI thread.
            });
        }
        try { Raise(shown); } // A handler is never called under the lock; no worker event can overtake this one.
        finally { beginning.TrySetResult(); }
        return started;
    }

    /// <summary>The x: stops the download, keeps the partial and returns to the offer.</summary>
    public void Cancel()
    {
        lock (gate) cancel?.Cancel();
    }

    public void Dispose()
    {
        Cancel();
    }

    private async Task Run(CancellationTokenSource source)
    {
        var cancellationToken = source.Token;
        await Task.Yield(); // never run on inside StartAsync's lock
        var total = (double)(pack.Bytes + companions.Bytes);
        try
        {
            var install = installer.Install(pack, cancellationToken);
            var job = installer.Job;
            if (job is not null) job.ProgressChanged += p => Report(p.BytesDone / total);
            var packResult = await install;
            switch (packResult.Status)
            {
                case CudaInstallStatus.Installed:
                    break;
                case CudaInstallStatus.Cancelled:
                    Set(CudaOfferStates.Offer(size));
                    return;
                default:
                    Set(CudaOfferStates.Failed(size));
                    return;
            }
            Report(pack.Bytes / total);
            var outcome = await companions.InstallAsync(installer.FolderFor(pack), share => Report((pack.Bytes + share * companions.Bytes) / total), cancellationToken);
            Set(outcome switch
            {
                CudaCompanionOutcome.Installed => CudaOfferStates.Installed(size),
                CudaCompanionOutcome.Cancelled => CudaOfferStates.Offer(size),
                _ => CudaOfferStates.Failed(size),
            });
        }
        catch (OperationCanceledException)
        {
            Set(CudaOfferStates.Offer(size));
        }
        catch (Exception)
        {
            Set(CudaOfferStates.Failed(size));
        }
        finally
        {
            lock (gate)
            {
                // Only this run's own source: a Download pressed as the cancel was announced has already made the next one (and the x must cancel that).
                source.Dispose();
                if (ReferenceEquals(cancel, source)) cancel = null;
            }
        }
    }

    // A percentage only ever moves forward within one download.
    private void Report(double share)
    {
        CudaOfferRow next;
        lock (gate)
        {
            if (row.State != CudaOfferState.Downloading || share <= progress) return;
            progress = Math.Min(1, share);
            next = CudaOfferStates.Downloading(size, progress);
            row = next;
        }
        Raise(next);
    }

    private void SetEvaluation(CudaOfferRow next)
    {
        lock (gate)
        {
            if (cancel is not null || row == next) return;
            row = next;
        }
        Raise(next);
    }

    private void Set(CudaOfferRow next)
    {
        lock (gate)
        {
            if (row == next) return;
            row = next;
        }
        Raise(next);
    }

    private void Raise(CudaOfferRow shown) => Changed?.Invoke(shown);
}
