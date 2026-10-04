using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: the pass of the draws as on ANGLE, in scope until disposed: its programs go through the program cache, the machine named as a test GPU.</summary>
public sealed class WarmPassScope : IDisposable
{
    private readonly GpuProgramCacheFolder cache = new();
    private readonly Func<string?> source = GpuProgramCache.MachineSource!;
    private readonly TimeSpan cap = GpuWarmup.IdleCap;
    private readonly IDisposable entries;
    public const string Machine = "10de:2704:00000000:a1 Test GPU 1.2.3.4";
    public string Root => cache.Root;

    public WarmPassScope()
    {
        GpuProgramCache.MachineSource = () => Machine;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(50);
        GpuWarmup.PretendGpu = true;
        entries = GpuWarmup.Register(GpuProgram.Draw("review2fix-entries", FirstRunSteps.Strike, _ => Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps)));
        File.WriteAllText(Path.Combine(cache.Folder, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + GpuProgramCache.Build + "\n" + Machine + "\n");
        for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(cache.Folder, k + ".bin"), [1, 2, 3]);
    }

    public void Dispose()
    {
        GpuWarmup.PretendGpu = false;
        GpuWarmup.IdleCap = cap;
        GpuProgramCache.MachineSource = source;
        entries.Dispose();
        cache.Dispose();
    }
}
