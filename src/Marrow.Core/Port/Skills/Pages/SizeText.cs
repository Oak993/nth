// PORT-ONLY: first-release feature unit SK-15 (DL-17). Sizes in the words of the design page (skills-downloads, words S1): decimal megabytes, as the plan prints them.
using System.Globalization;

namespace Marrow.Core.Port.Skills.Pages;

/// <summary>Free space on the data root's volume: the drive's label ("C:") and its free bytes.</summary>
public sealed record DriveSpace(string Label, long FreeBytes);

public static class SizeText
{
    /// <summary>
    /// The owner's line (OD-0099 review of version 3, 2026-10-04): "anything above 100 MB, you can say large download"; a download of 100 MB or under gets plain words. So the
    /// warning shows strictly above this many decimal megabytes.
    /// </summary>
    public const double LargeAboveMb = 100;

    public static bool IsLarge(double mb) => mb > LargeAboveMb;

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    /// <summary>"0.24 MB", "3.5 MB", "32 MB", "356 MB", "1.04 GB" (the design's fmtMb).</summary>
    public static string Short(double mb)
    {
        if (mb >= 1000) return (mb / 1000).ToString("0.00", Invariant) + " GB";
        if (mb < 1) return mb.ToString("0.00", Invariant) + " MB";
        if (mb < 10)
        {
            var tenth = Math.Round(mb, 1, MidpointRounding.AwayFromZero);
            return tenth >= 10 ? "10 MB" : tenth.ToString("0.0", Invariant) + " MB";
        }
        return Math.Round(mb, MidpointRounding.AwayFromZero).ToString("0", Invariant) + " MB";
    }

    /// <summary>The confirmation's exact figure: "32.2 MB", "1.04 GB (1,040.2 MB)".</summary>
    public static string Exact(double mb)
    {
        var megabytes = mb < 1 ? mb.ToString("0.00", Invariant) : mb.ToString("#,##0.0", Invariant);
        return mb >= 1000 ? (mb / 1000).ToString("0.00", Invariant) + " GB (" + megabytes + " MB)" : megabytes + " MB";
    }

    /// <summary>"about 32 MB", "about 2.8 GB".</summary>
    public static string Disk(double mb) => "about " + (mb >= 1000 ? (mb / 1000).ToString("0.0", Invariant) + " GB" : Short(mb));

    /// <summary>Free space in whole units: "212 GB", "4.5 GB", "640 MB".</summary>
    public static string Free(long bytes)
    {
        var gb = bytes / 1e9;
        if (gb >= 10) return Math.Round(gb, MidpointRounding.AwayFromZero).ToString("0", Invariant) + " GB";
        if (gb >= 1) return gb.ToString("0.0", Invariant) + " GB";
        return Math.Round(bytes / 1e6, MidpointRounding.AwayFromZero).ToString("0", Invariant) + " MB";
    }

    /// <summary>Required space always rounds upward, never advertising less than the admission check needs.</summary>
    public static string Required(long bytes)
    {
        var gb = bytes / 1e9;
        if (gb >= 10) return Math.Ceiling(gb).ToString("0", Invariant) + " GB";
        if (gb >= 1) return (Math.Ceiling(gb * 10) / 10).ToString("0.0", Invariant) + " GB";
        return Math.Ceiling(bytes / 1e6).ToString("0", Invariant) + " MB";
    }
}
