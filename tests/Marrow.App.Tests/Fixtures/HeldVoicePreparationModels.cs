using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Fixtures;

// RR-11: a measured in-memory transfer; no file/network/model/native operation.
public sealed class HeldVoicePreparationModels : IVoiceModelSource
{
    private Action<double>? report;
    private readonly List<Action<double>> reports = [];
    private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool installed;
    public int Installs { get; private set; }
    public bool CancellationObserved { get; private set; }
    public ModelFile? File { get; private set; }
    public bool IsInstalled(ModelFile file) => installed;
    public string PathFor(ModelFile file) => Path.Combine("scripted-models", file.FileName);
    public async Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken)
    {
        Installs++;
        File = file;
        report = fraction;
        reports.Add(fraction);
        fraction(.36);
        try { await gate.Task.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { CancellationObserved = true; throw; }
        installed = true;
        return new ModelDownloadResult(ModelDownloadStatus.Installed, null);
    }
    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken) => Task.FromResult(ModelVerification.Rehashed);
    public void Report(double fraction) => report!(fraction);
    public void ReportPrevious(double fraction) => reports[^2](fraction);
    public void Complete() => gate.SetResult();
    public void Retry() { gate = new(TaskCreationOptions.RunContinuationsAsynchronously); CancellationObserved = false; }
}
