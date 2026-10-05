using Marrow.App.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-14 test double: one download that moves only when the test says so (no transfer, no clock).</summary>
internal sealed class ScriptedSpeechModelDownload : ISpeechModelDownload
{
    private readonly TaskCompletionSource<ModelDownloadStatus> end = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public double Fraction { get; private set; }

    public bool Cancelled { get; private set; }

    /// <summary>Told, before the end is told to the waiting code, that the download is over (the backend forgets it).</summary>
    public Action<ModelDownloadStatus>? Ended { get; set; }

    public event Action<double>? FractionChanged;

    public Task<ModelDownloadStatus> Completion => end.Task;

    public void Cancel()
    {
        Cancelled = true;
        End(ModelDownloadStatus.Cancelled);
    }

    public void Move(double fraction)
    {
        Fraction = fraction;
        FractionChanged?.Invoke(fraction);
    }

    public void End(ModelDownloadStatus status)
    {
        if (end.Task.IsCompleted) return;
        Ended?.Invoke(status);
        end.TrySetResult(status);
    }
}
