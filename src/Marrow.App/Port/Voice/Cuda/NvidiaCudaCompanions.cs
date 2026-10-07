// PORT-ONLY: OD-0104 authorizes downloading pinned NVIDIA redistributables; delegates extraction/atomic publication to Services.
using Marrow.Services.Port.Voice.Models;
namespace Marrow.App.Port.Voice.Cuda;

public sealed class NvidiaCudaCompanions(NvidiaCompanionInstaller installer) : ICudaCompanions
{
    public bool Available => true;
    public long Bytes => installer.Bytes;
    public bool IsInstalled(string folder) => installer.IsInstalled(folder);
    public async Task<CudaCompanionOutcome> InstallAsync(string folder, Action<double> progress, CancellationToken cancellationToken) =>
        await installer.InstallAsync(folder, progress, cancellationToken) switch
        {
            NvidiaCompanionResult.Installed => CudaCompanionOutcome.Installed,
            NvidiaCompanionResult.Cancelled => CudaCompanionOutcome.Cancelled,
            _ => CudaCompanionOutcome.Failed,
        };
}
