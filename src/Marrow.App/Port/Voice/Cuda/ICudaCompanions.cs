// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the seam for the NVIDIA libraries the pack does not carry.
namespace Marrow.App.Port.Voice.Cuda;

/// <summary>
/// cuBLAS 12.4.2.65 and the CUDA runtime 12.4.99 from NVIDIA's own servers (voice-pins.json "cuda.nvidiaCompanions"; OD-0104). The pack alone cannot load without them. The
/// production installer is NOT DONE (see ED-NEW-cloud-vt-17): <see cref="Available"/> is false until one is bound, and the offer is not shown while it is.
/// </summary>
public interface ICudaCompanions
{
    /// <summary>True when an installer is bound; false (fail closed) offers nothing.</summary>
    bool Available { get; }

    /// <summary>The bytes of the files fetched (the pins' sizes, for the offer's size).</summary>
    long Bytes { get; }

    /// <summary>True when the libraries are in <paramref name="folder"/> (the pack's folder).</summary>
    bool IsInstalled(string folder);

    /// <summary>Fetches and places them into <paramref name="folder"/>, reporting 0 to 1.</summary>
    Task<CudaCompanionOutcome> InstallAsync(string folder, Action<double> progress, CancellationToken cancellationToken);
}
