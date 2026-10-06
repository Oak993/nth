// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the fail-closed default of the companions seam (V-2.5).
namespace Marrow.App.Port.Voice.Cuda;

public sealed class FailClosedCudaCompanions : ICudaCompanions
{
    public bool Available => false;

    public long Bytes => CudaCompanionPins.Bytes;

    public bool IsInstalled(string folder) => false;

    public Task<CudaCompanionOutcome> InstallAsync(string folder, Action<double> progress, CancellationToken cancellationToken) => Task.FromResult(CudaCompanionOutcome.Failed);
}
