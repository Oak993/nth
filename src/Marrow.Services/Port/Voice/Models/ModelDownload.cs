// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): one model download in progress (V-9.5).
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

/// <summary>A started model download: its fraction (V-16.3.3's progress), pause, resume and cancel (the row's x keeps the partial), and its end.</summary>
public sealed class ModelDownload
{
    internal ModelDownload(DownloadJob job, Func<Task<ModelDownloadResult>> complete)
    {
        Job = job;
        job.ProgressChanged += p => FractionChanged?.Invoke(Ratio(p));
        Completion = complete();
    }

    public DownloadJob Job { get; }

    public double Fraction => Ratio(Job.Progress);

    public Task<ModelDownloadResult> Completion { get; }

    public event Action<double>? FractionChanged;

    public void Pause() => Job.Pause();

    public void Resume() => Job.Resume();

    public void Cancel() => Job.Cancel();

    private static double Ratio(DownloadProgress p) => p.BytesTotal == 0 ? 0 : (double)p.BytesDone / p.BytesTotal;
}
