using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Fixtures;

// Holds the old collaborator after cancellation so a fresh retry must wait for its resources.
public sealed class CancellationIgnoringVoiceModels : IVoiceModelSource
{
    private readonly TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    public int Installs { get; private set; }
    public int MaxConcurrent { get; private set; }
    public CancellationToken FirstToken { get; private set; }
    public bool IsInstalled(ModelFile file) => false;
    public string PathFor(ModelFile file) => Path.Combine("scripted-models", file.FileName);
    public async Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken)
    {
        Installs++;
        MaxConcurrent = Math.Max(MaxConcurrent, ++active);
        try
        {
            fraction(.36);
            if (Installs == 1) { FirstToken = cancellationToken; await first.Task; }
            else cancellationToken.ThrowIfCancellationRequested();
            return new ModelDownloadResult(ModelDownloadStatus.Installed, null);
        }
        finally { --active; }
    }
    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken) => Task.FromResult(ModelVerification.Rehashed);
    public void ReleaseFirst() => first.SetResult();
}
