// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): download, verify and unpack the CUDA runtime pack (Q-V-9 C).
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed class CudaPackInstaller
{
    public CudaPackInstaller(string dataRoot, Downloader downloader, Func<CudaRuntimePack, Uri>? urlFor = null) => throw new NotImplementedException();

    public string FolderFor(CudaRuntimePack pack) => throw new NotImplementedException();

    public bool IsInstalled(CudaRuntimePack pack) => throw new NotImplementedException();

    public DownloadJob? Job => throw new NotImplementedException();

    public Task<CudaInstallResult> Install(CudaRuntimePack pack, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public bool Remove(CudaRuntimePack pack) => throw new NotImplementedException();
}
