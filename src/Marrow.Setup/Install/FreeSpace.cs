using System.Runtime.InteropServices;
using Marrow.Setup.Text;

namespace Marrow.Setup.Install;

/// <summary>Free space on the volume that holds a folder.</summary>
public interface IDiskSpace
{
    /// <summary>The bytes this user may still write on the volume holding <paramref name="folder"/> (the folder need not exist yet), or null when Windows cannot say.</summary>
    long? FreeBytes(string folder);
}

/// <summary>
/// <c>GetDiskFreeSpaceExW</c>'s free bytes available to the caller (quotas included), asked of the nearest folder that
/// exists on the way up from the install root, so a root that is not there yet, a share root and a mounted folder all work.
/// </summary>
public sealed partial class WindowsDiskSpace : IDiskSpace
{
    public long? FreeBytes(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!OperatingSystem.IsWindows()) return null;
        for (var probe = Path.GetFullPath(folder); probe is not null; probe = Path.GetDirectoryName(probe))
        {
            if (!Directory.Exists(probe)) continue;
            var asked = Path.EndsInDirectorySeparator(probe) ? probe : probe + Path.DirectorySeparatorChar;
            return GetDiskFreeSpaceExW(asked, out var available, out _, out _) ? (long)Math.Min(available, long.MaxValue) : null;
        }
        return null;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
}

/// <summary>The free-space check's answer.</summary>
/// <param name="Enough">True when the install may start (also when Windows could not say how much is free: Velopack checks again).</param>
/// <param name="NeededBytes">The threshold: the plan's rule or Velopack's own, whichever is larger.</param>
/// <param name="FreeBytes">What Windows reported, or null.</param>
/// <param name="Drive">The drive as the message names it ("C:", or a share's <c>\\server\share</c>).</param>
/// <param name="ShownMegabytes">The "nth needs about … MB" figure.</param>
public sealed record FreeSpaceVerdict(bool Enough, long NeededBytes, long? FreeBytes, string Drive, long ShownMegabytes)
{
    /// <summary>The free bytes in whole megabytes, rounded down (0 when unknown).</summary>
    public long FreeMegabytes => InstallSizes.MegabytesDown(FreeBytes ?? 0);

    /// <summary>Problem's sentence when there is not enough: "There isn't enough free space on drive C:. nth needs about 420 MB, and 112 MB is free."</summary>
    public string? Message => Enough ? null : SetupStrings.ProblemSpace(Drive, ShownMegabytes, FreeMegabytes);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, "Free space"; VELOPACK_CHECK.md "Also found"; unit U3, ED-NEW-inst-u3-1). Before
/// anything runs, the target drive must hold T_disk plus 10 % for a fresh install, or T_pkg + T_app of the new version plus
/// 10 % for an update (the old folder is kept until the end), and never less than Velopack's own threshold (every package
/// entry compressed and uncompressed plus 50,000,000 bytes, install.rs:52-67), which Velopack applies to upgrades too. The
/// message names T_disk ("about 420 MB"), the one size shown everywhere; only when that much is free, which would make the
/// sentence contradict itself, it names the threshold instead.
/// </summary>
public static class FreeSpaceCheck
{
    /// <summary>The plan's margin over the footprint.</summary>
    public const double Margin = 0.10;

    public static long Needed(InstallSizes sizes, bool update)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var footprint = update ? sizes.CopyBytes : sizes.DiskBytes;
        var ours = (long)Math.Ceiling(footprint * (1 + Margin));
        return Math.Max(ours, sizes.VelopackNeedBytes);
    }

    public static FreeSpaceVerdict Check(InstallSizes sizes, bool update, string root, IDiskSpace disk)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(disk);
        var needed = Needed(sizes, update);
        long? free;
        try { free = disk.FreeBytes(root); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { free = null; }
        var enough = free is not { } bytes || bytes >= needed;
        var shown = sizes.DiskMegabytes;
        if (!enough && InstallSizes.MegabytesDown(free!.Value) is var freeShown && freeShown >= shown)
            shown = Math.Max(InstallSizes.Megabytes(needed), freeShown + 1); // the needed figure always reads above the free one
        return new FreeSpaceVerdict(enough, needed, free, Drive(root), shown);
    }

    /// <summary>"C:" for <c>C:\…</c>, <c>\\server\share</c> for a share.</summary>
    public static string Drive(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var path = Path.GetPathRoot(Path.GetFullPath(root)) ?? root;
        return Path.TrimEndingDirectorySeparator(path);
    }
}
