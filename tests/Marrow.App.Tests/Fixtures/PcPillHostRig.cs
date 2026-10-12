// PORT-ONLY: Tactus (ED-NEW-pcctl-24). The pill host and its fakes as one test uses them: the host, the overlays it shows
// the pill on, the fake bands and Win32 calls, the fake band surfaces with their glass, the monitor choice, and the clock.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed record PcPillHostRig(TactusPillHost Host, PillOverlays Overlays, PcFakePillBands Bands, PcFakeBandNative Native, PcFakePillSurfaces Surfaces, PillScreens Screens, ManualTimeProvider Clock);
