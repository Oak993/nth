using System.Globalization;

namespace Marrow.Setup.Install;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, unit U3, ED-NEW-inst-u3-1). The sizes the packaging script measures and stamps into the
/// setup as assembly metadata (unit U7, Directory.Build.targets): <c>T_pkg</c> (the full nupkg, <c>InstallerPackageBytes</c>),
/// <c>T_app</c> (what <c>current\</c> holds, <c>InstallerAppBytes</c>), <c>T_disk</c> (the whole footprint with Update.exe and
/// the stub, <c>InstallerDiskBytes</c>; the one size shown everywhere) and Velopack's own free-space threshold
/// (<c>InstallerVelopackNeedBytes</c>, VELOPACK_CHECK.md "Also found"). A build outside the script carries none of them: it
/// then uses the plan's figures (about 118, 302 and 420 MB) and says so in <see cref="Measured"/>.
/// </summary>
/// <param name="PackageBytes">T_pkg: the full nupkg Velopack keeps in <c>packages\</c>.</param>
/// <param name="AppBytes">T_app: the uncompressed files that go to <c>current\</c>.</param>
/// <param name="DiskBytes">T_disk: everything the install puts on disk.</param>
/// <param name="VelopackNeedBytes">Velopack's own check: every package entry compressed and uncompressed plus 50,000,000 bytes (0 when unknown).</param>
/// <param name="Measured">True when the numbers came from the packaging script.</param>
public sealed record InstallSizes(long PackageBytes, long AppBytes, long DiskBytes, long VelopackNeedBytes, bool Measured)
{
    public const string PackageKey = "InstallerPackageBytes";
    public const string AppKey = "InstallerAppBytes";
    public const string DiskKey = "InstallerDiskBytes";
    public const string VelopackNeedKey = "InstallerVelopackNeedBytes";

    /// <summary>One megabyte as Windows counts it in Explorer and in Settings › Apps (1,048,576 bytes), so "420 MB" here is the Apps list's figure.</summary>
    public const long Megabyte = 1024 * 1024;

    /// <summary>The plan's figures (PLAN.md 4), used only by a build the packaging script did not measure.</summary>
    public static InstallSizes Estimate { get; } = new(118 * Megabyte, 302 * Megabyte, 420 * Megabyte, 0, Measured: false);

    /// <summary>What the Copying step counts: the bytes that arrive in <c>packages\</c> and <c>current\</c> (T_pkg + T_app).</summary>
    public long CopyBytes => PackageBytes + AppBytes;

    /// <summary>T_disk in whole megabytes, the figure Welcome, the Copying step and the free-space message show.</summary>
    public long DiskMegabytes => Megabytes(DiskBytes);

    /// <summary>The stamped sizes, or <see cref="Estimate"/> when any of the three is missing, not a positive number, or T_disk is below T_pkg + T_app.</summary>
    public static InstallSizes FromMetadata(Func<string, string?> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (Read(metadata(PackageKey)) is not { } package || Read(metadata(AppKey)) is not { } app || Read(metadata(DiskKey)) is not { } disk) return Estimate;
        if (disk < package + app) return Estimate;
        return new InstallSizes(package, app, disk, Read(metadata(VelopackNeedKey)) ?? 0, Measured: true);
    }

    /// <summary>Bytes as whole megabytes, rounded to the nearest (half away from zero).</summary>
    public static long Megabytes(long bytes) => (long)Math.Round(Math.Max(0, bytes) / (double)Megabyte, MidpointRounding.AwayFromZero);

    /// <summary>Bytes as whole megabytes, rounded down: what is free is never overstated.</summary>
    public static long MegabytesDown(long bytes) => Math.Max(0, bytes) / Megabyte;

    private static long? Read(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
