using Marrow.App.Port;
using Velopack;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-1): what VelopackSelfUpdateEngineTests builds for one case: the production engine over a fake
// "marrow-releases" feed, the packages "Restart Marrow" was asked to apply, and the packages folder of the test's Velopack locator.
// Moved out of the test class by the Windows review (R-TEST-3: helper types live in Fixtures, never inside a test class).
public sealed record VelopackEngineRig(VelopackSelfUpdateEngine Engine, FakeFeedDownloader Feed, List<VelopackAsset> Applied, string PackagesDir);
