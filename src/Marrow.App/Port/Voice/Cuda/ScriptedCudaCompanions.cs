// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the companions double.
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.App.Port.Voice.Cuda;

/// <summary>
/// Stands in for NVIDIA's libraries: it fetches nothing. <see cref="InstallAsync"/> reports <paramref name="steps"/> and then answers <see cref="Outcome"/>, writing the three
/// library names (the runtime inventory's, empty files) into the folder when that is Installed; <see cref="Hold"/> makes it wait, and a cancel then answers Cancelled.
/// </summary>
public sealed class ScriptedCudaCompanions(long bytes = 4_000, double[]? steps = null) : ICudaCompanions
{
    private readonly double[] reported = steps ?? [0.5, 1.0];

    public CudaCompanionOutcome Outcome { get; set; } = CudaCompanionOutcome.Installed;

    public TaskCompletionSource? Hold { get; set; }

    public bool Started { get; private set; }

    public int Cancelled { get; private set; }

    public List<string> Installs { get; } = [];

    public bool Available => true;

    public long Bytes => bytes;

    /// <summary>Puts the library files in <paramref name="folder"/>, as a finished install does.</summary>
    public void Place(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in FileRuntimeInventory.CudaCompanions) File.WriteAllBytes(Path.Combine(folder, name), []);
    }

    public bool IsInstalled(string folder) => FileRuntimeInventory.CudaCompanions.All(name => File.Exists(Path.Combine(folder, name)));

    public async Task<CudaCompanionOutcome> InstallAsync(string folder, Action<double> progress, CancellationToken cancellationToken)
    {
        Started = true;
        if (Hold is { } hold)
        {
            try
            {
                await hold.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled += 1;
                return CudaCompanionOutcome.Cancelled;
            }
        }
        foreach (var step in reported) progress(step);
        if (Outcome == CudaCompanionOutcome.Installed)
        {
            Place(folder);
            Installs.Add(folder);
        }
        return Outcome;
    }
}
