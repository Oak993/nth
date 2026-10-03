// DEVIATION OD-0089 (ED-251): the headless renderer is Skia's software rasterizer, where the composer light draws one still
// unless asked; evidence and view tests that need its motion turn it on for their scope.
using Marrow.UI.Port.ComposerLights;

namespace Marrow.App.Tests.Fixtures;

internal sealed class ComposerLightMotion : IDisposable
{
    public ComposerLightMotion() => ComposerLightHandler.AnimateWithoutGpu = true;
    public void Dispose() => ComposerLightHandler.AnimateWithoutGpu = false;
}
