using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// ED-NEW-glass-seams-1: on Skia's CPU rasterizer (the headless tests') a glass piece is redrawn whole whenever a frame redraws any of it, while on the
// GPU renderer the app ships on it is redrawn only as far as a change reaches (ED-320 15 and 16). A test that pins the reach holds one of these
// for each test, so it keeps pinning what the GPU path draws.
internal sealed class GlassReachOnly : IDisposable
{
    public GlassReachOnly() => GlassMaterial.ReachOnlyForTests = true;

    public void Dispose() => GlassMaterial.ReachOnlyForTests = false;
}
