using Marrow.Setup.Install;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 10, the fake runner's scenarios; unit U3, ED-NEW-inst-u3-1). The timelines the progress
/// engine is tested against. <see cref="Default"/> is PROVISIONAL: it follows the order of writes read from Velopack
/// 1.2.161's source (ED-247's reading as corrected by VELOPACK_CHECK.md check 5: lock, Update.exe, the stub, the nupkg,
/// <c>current\</c> extracted in place, shortcuts, the install hook, the uninstall key, exit), with made-up durations, until
/// the first clean-machine run records the real timeline (unit U11), which then replaces it as a fixture and is recorded
/// again on every Velopack bump. Sizes are scaled down (the engine reads only their ratios).
/// </summary>
public static class VelopackScenarios
{
    public static InstallSizes Sizes { get; } = new(PackageBytes: 1_180_000, AppBytes: 3_020_000, DiskBytes: 4_270_000, VelopackNeedBytes: 4_700_000, Measured: true);

    public const string Package = @"packages\NthApp-1.1.0-full.nupkg";

    /// <summary>The scenario names <see cref="Build"/> knows, for theories.</summary>
    public static IEnumerable<object[]> Names() =>
    [
        ["default"], ["fast"], ["slow"], ["stall25"], ["filesInUse"], ["accessDenied"], ["hookTimeout"], ["crashMidCopy"],
        ["restoreFailed"], ["restoreOk"], ["velopackNoSpace"], ["upgrade"], ["extractToTemp"], ["killedAfterExit"],
    ];

    /// <summary>Scenarios that run on an older install (the root holds 1.0.0 first).</summary>
    public static bool IsUpgrade(string name) => name is "upgrade" or "restoreFailed" or "restoreOk" or "filesInUse";

    public static VelopackTimeline Build(string name) => name switch
    {
        "default" => Default(),
        "fast" => Default("fast", copySeconds: 2),
        "slow" => Slow(),
        "stall25" => Default("stall25", copySeconds: 6, stallSeconds: 25),
        "filesInUse" => FilesInUse(),
        "accessDenied" => AccessDenied(),
        "hookTimeout" => HookTimeout(),
        "crashMidCopy" => CrashMidCopy(),
        "restoreFailed" => FailedUpgrade("restoreFailed", restored: false),
        "restoreOk" => FailedUpgrade("restoreOk", restored: true),
        "velopackNoSpace" => VelopackNoSpace(),
        "upgrade" => Default("upgrade", copySeconds: 5, update: true),
        "extractToTemp" => Default("extractToTemp", copySeconds: 5, throughTemp: true),
        "killedAfterExit" => Default("killedAfterExit", copySeconds: 2),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such scenario."),
    };

    /// <summary>The provisional default: Velopack 1.2.161's order of writes (see the type's summary).</summary>
    public static VelopackTimeline Default(string name = "default", double copySeconds = 9, double stallSeconds = 0, bool update = false, bool throughTemp = false)
    {
        var t = Opening(new VelopackTimeline(name), update);
        var share = Sizes.PackageBytes / (double)Sizes.CopyBytes;
        var package = copySeconds * share;
        t.Log(0.6, "Copying nupkg to packages directory...").Grow(0.6, package, Package, Sizes.PackageBytes);
        var at = 0.6 + package + stallSeconds; // a stall: Defender scanning the package before extraction goes on
        var app = copySeconds - package;
        var folder = throughTemp ? @"packages\VelopackTemp\tmp_k3j2" : "current";
        foreach (var (file, part) in AppFiles)
        {
            t.Grow(at, app * part, Path.Combine(folder, file), (long)(Sizes.AppBytes * part));
            at += app * part;
        }
        if (throughTemp) t.MoveFolder(at + 0.05, folder, "current");
        return Closing(t, at + 0.1, update);
    }

    private static readonly (string File, double Part)[] AppFiles = [(@"nth.exe", 0.3), (@"lib\voice.dll", 0.45), (@"sq.version", 0.0), (@"lib\skia.dll", 0.25)];

    private static VelopackTimeline Opening(VelopackTimeline t, bool update)
    {
        t.Log(0, "Starting Velopack Setup (1.2.161)").Log(0.1, "There is 120.00 GB free space available at destination, this package requires 470.00 MB.");
        if (update) t.Log(0.15, "User chose to overwrite existing installation.").RenameRootAway(0.35);
        return t.Log(0.4, "Preparing and cleaning installation directory...").Log(0.42, "Acquiring lock...").File(0.42, @"packages\.velopack_lock", 0)
            .Log(0.45, "Starting installation!").Log(0.46, "Extracting Update.exe...").File(0.47, "Update.exe", 40_000).File(0.5, "nth.exe", 30_000);
    }

    private static VelopackTimeline Closing(VelopackTimeline t, double at, bool update)
    {
        t.Shortcuts(at).HookStart(at + 0.1).Result(at + 0.8).HookEnd(at + 0.9).Key(at + 1.0).Completed(at + 1.05);
        if (update) t.DeleteRollback(at + 1.1);
        return t.Exit(at + 1.2, 0);
    }

    /// <summary>A slow PC: bytes arrive in bursts with 8 s pauses (never 20 s), about 50 s of copying.</summary>
    private static VelopackTimeline Slow()
    {
        var t = Opening(new VelopackTimeline("slow"), update: false);
        t.Log(0.6, "Copying nupkg to packages directory...").Grow(0.6, 10, Package, Sizes.PackageBytes);
        var at = 18.6;
        foreach (var (file, part) in AppFiles)
        {
            t.Grow(at, 30 * part, Path.Combine("current", file), (long)(Sizes.AppBytes * part));
            at += 30 * part + (part > 0 ? 8 : 0);
        }
        return Closing(t, at, update: false);
    }

    /// <summary>An upgrade Velopack cannot start: it cannot stop nth (a sharing violation); the old root is never touched.</summary>
    private static VelopackTimeline FilesInUse() => new VelopackTimeline("filesInUse")
        .Log(0, "Starting Velopack Setup (1.2.161)").Log(0.15, "User chose to overwrite existing installation.")
        .Log(0.3, "An error has occurred: Failed to stop application (The process cannot access the file because it is being used by another process. (os error 32)), please close the application and try running the installer again.", "ERROR")
        .Exit(0.4, 1);

    /// <summary>A fresh install refused at Update.exe (access denied), before anything is in the root.</summary>
    private static VelopackTimeline AccessDenied() => new VelopackTimeline("accessDenied")
        .Log(0, "Starting Velopack Setup (1.2.161)").Log(0.4, "Preparing and cleaning installation directory...").Log(0.45, "Starting installation!")
        .Log(0.46, "Extracting Update.exe...").Log(0.5, "Installation failed!", "ERROR")
        .Log(0.52, "An error has occurred: Access is denied. (os error 5)\n\nCaused by:\n    Access is denied. (os error 5)", "ERROR")
        .Exit(0.6, 1);

    /// <summary>The install hook overruns its 30 s and is killed; the install still finishes, with no result file.</summary>
    private static VelopackTimeline HookTimeout()
    {
        var t = Opening(new VelopackTimeline("hookTimeout"), update: false);
        t.Log(0.6, "Copying nupkg to packages directory...").Grow(0.6, 0.6, Package, Sizes.PackageBytes).Grow(1.2, 1.4, @"current\nth.exe", Sizes.AppBytes);
        return t.Shortcuts(2.7).HookStart(2.8).HookTimeout(32.8).Key(32.9).Completed(33).Exit(33.1, 0);
    }

    /// <summary>Setup.exe crashes halfway through the package (an access violation), leaving what it wrote.</summary>
    private static VelopackTimeline CrashMidCopy()
    {
        var t = Opening(new VelopackTimeline("crashMidCopy"), update: false);
        return t.Log(0.6, "Copying nupkg to packages directory...").Grow(0.6, 2, Package, Sizes.PackageBytes / 2).Exit(2.7, unchecked((int)0xC0000005));
    }

    /// <summary>An upgrade fails while extracting; Velopack restores the old root, or its restore fails silently.</summary>
    private static VelopackTimeline FailedUpgrade(string name, bool restored)
    {
        var t = Opening(new VelopackTimeline(name), update: true);
        t.Log(0.6, "Copying nupkg to packages directory...").Grow(0.6, 1, Package, Sizes.PackageBytes).Grow(1.6, 1, @"current\nth.exe", Sizes.AppBytes / 3)
            .Log(2.7, "Installation failed!", "ERROR");
        if (restored) t.RestoreRoot(2.8);
        else t.RestoreFails(2.8);
        return t.Log(2.9, "An error has occurred: Failed to extract lib/app/voice.dll: invalid Zip archive: unexpected end of file", "ERROR").Exit(3, 1);
    }

    /// <summary>Velopack's own free-space check refuses (it counts more than the setup's rule).</summary>
    private static VelopackTimeline VelopackNoSpace() => new VelopackTimeline("velopackNoSpace")
        .Log(0, "Starting Velopack Setup (1.2.161)")
        .Log(0.2, "An error has occurred: nth requires at least 470.00 MB disk space to be installed. There is only 112.00 MB available.", "ERROR")
        .Exit(0.3, 1);
}
