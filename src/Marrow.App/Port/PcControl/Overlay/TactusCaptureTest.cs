// PORT-ONLY: Tactus's capture-test switch (P30; ED-NEW-pcctl-30; DESIGN.md 10.11 "Native"). Debug builds started with
// `--tactus-capture-test` leave every Tactus overlay capturable (no WDA_EXCLUDEFROMCAPTURE), so the owner-scheduled native
// frame capture can see the pill and its card. Release builds never read the switch: it is always off there.
namespace Marrow.App.Port.PcControl.Overlay;

public static class TactusCaptureTest
{
    public const string Flag = "--tactus-capture-test";

    /// <summary>This process's switch, read once from its command line.</summary>
    public static bool Enabled { get; } = From(Environment.GetCommandLineArgs());

    /// <summary>Whether <paramref name="args"/> turn the capture test on: only in a debug build, only with the flag.</summary>
    public static bool From(IEnumerable<string> args)
    {
#if DEBUG
        return args.Contains(Flag, StringComparer.Ordinal);
#else
        _ = args;
        return false;
#endif
    }
}
