namespace Marrow.App.Tests.Nth;

// A captured headless frame (BGRA, top-down) for the Calculi Native tests (NthWorkingLineNativeTests, NthSmallSitesNativeTests); kept out of the test
// files, which hold one top-level test class each (R-TEST-2, R-TEST-3; ED-NEW-calcmerge-2).
internal sealed record NthBeadsShot(int Width, int Height, byte[] Bgra)
{
    public (int B, int G, int R) At(int x, int y) { var i = (y * Width + x) * 4; return (Bgra[i], Bgra[i + 1], Bgra[i + 2]); }
}
