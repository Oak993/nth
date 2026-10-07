// PORT-ONLY: OD-0104 / OD-0106 CUDA A. Production construction is lazy; tests override the factory and never bind downloads/hardware.
using Marrow.Core.Port;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;
namespace Marrow.App.Port.Voice.Cuda;

internal static class CudaOfferComposition
{
    internal static Ambient<Func<CudaOffer?>> Factory { get; } = new((Func<CudaOffer?>)(() => null));

    public static void Install(string dataRoot)
    {
        var shared = new Lazy<CudaOffer>(() => Build(dataRoot));
        Factory.SetProcessDefault(() => shared.Value);
    }

    internal static CudaOffer Build(string dataRoot)
    {
        var downloader = ModelDownloader.CreateDownloader(Path.Combine(dataRoot, "runtimes", "downloads", "partials"), ["api.nuget.org", "developer.download.nvidia.com"]);
        return new CudaOffer(HardwareProbeSeams.Probe.Current, new CudaPackInstaller(dataRoot, downloader), CudaRuntimePack.Cuda12,
            new NvidiaCudaCompanions(new NvidiaCompanionInstaller(dataRoot, downloader)));
    }
}
