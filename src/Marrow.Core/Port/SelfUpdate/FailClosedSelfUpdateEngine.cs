namespace Marrow.Core.Port.SelfUpdate;

/// <summary>The default engine until one is bound: it is not an install, finds nothing, downloads nothing and applies nothing (fail closed).</summary>
public sealed class FailClosedSelfUpdateEngine : ISelfUpdateEngine
{
    public bool IsInstalled => false;

    public Task<SelfUpdateOffer?> CheckAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No self-update engine is bound.");

    public Task<string> DownloadAsync(string version, Action<int> percent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No self-update engine is bound.");

    public void WaitExitThenApplyAndRestart() => throw new InvalidOperationException("No self-update engine is bound.");
}
