using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-15: the model source double. No transfer happens: an install is a scripted outcome. With <see cref="ThrowOnAnyTransfer"/> every member throws, which is the downloader that
/// proves nothing is fetched before consent (D3).
/// </summary>
public sealed class VoicePrepModelSource : IVoiceModelSource
{
    public HashSet<string> Installed { get; } = new(StringComparer.Ordinal);

    public List<string> Installs { get; } = [];

    public List<string> Verifies { get; } = [];

    public bool ThrowOnAnyTransfer { get; set; }

    public double[] Fractions { get; set; } = [0.5, 1.0];

    public ModelDownloadStatus Outcome { get; set; } = ModelDownloadStatus.Installed;

    public ModelVerification Verification { get; set; } = ModelVerification.Rehashed;

    /// <summary>Releases an install that waits on it (null: installs finish at once).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public bool IsInstalled(ModelFile file)
    {
        Guard();
        return Installed.Contains(file.Id);
    }

    public string PathFor(ModelFile file) => Path.Combine("models", file.FileName);

    public async Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken)
    {
        Guard();
        Installs.Add(file.Id);
        foreach (var f in Fractions) fraction(f);
        if (Gate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        if (Outcome == ModelDownloadStatus.Installed) Installed.Add(file.Id);
        return new ModelDownloadResult(Outcome, Outcome == ModelDownloadStatus.Installed ? null : new DownloadFailure(DownloadFailureKind.Unreachable, "scripted"));
    }

    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken)
    {
        Guard();
        Verifies.Add(file.Id);
        return Task.FromResult(Verification);
    }

    private void Guard()
    {
        if (ThrowOnAnyTransfer) throw new InvalidOperationException("the model source was touched: a transfer happened before consent");
    }
}
