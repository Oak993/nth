// PORT-ONLY: V-3.2 / OD-0106 Notes A. .NET's AVX2 fact includes CPU and OS support; no vendor/name heuristic.
using System.Runtime.Intrinsics.X86;
using Marrow.Core.Port;
namespace Marrow.App.Port.Voice.Notes;

public sealed class RuntimeProcessorFeatures(Func<bool>? supports = null) : IProcessorFeatures
{
    public bool Avx2Supported => (supports ?? (() => Avx2.IsSupported))();
}

internal static class ProcessorFeatureSeams
{
    internal static Ambient<IProcessorFeatures> Current { get; } = new(new FailClosedProcessorFeatures());
    public static void Install() => Current.SetProcessDefault(new RuntimeProcessorFeatures());
}
