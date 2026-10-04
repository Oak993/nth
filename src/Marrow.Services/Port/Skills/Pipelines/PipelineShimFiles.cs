using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Port.Skills.Pipelines;
// SK-8/SK-6 bridge: fixed executable wrappers; each invocation selects the atomic installed-pin snapshot.
public static class PipelineShimFiles
{
    public static string Write(string bin, string name, string nativeLauncher, string stateFile)
    {
        var definition = PipelineRoutingReader.ResolveShim(stateFile, name);
        return ShimFiles.Write(bin, name, nativeLauncher, definition with { RoutingStateFile = stateFile, RoutingName = name });
    }
}
