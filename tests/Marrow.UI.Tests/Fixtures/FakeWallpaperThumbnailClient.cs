using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Fixtures;

// The ticker's stand-in for a live wallpaper thumbnail (WallpaperThumbnailTickerTests; WL-2, ED-NEW-wltwo-1): it counts its admissions and the time it was advanced.
public sealed class FakeWallpaperThumbnailClient(int rank) : IWallpaperThumbnailClient
{
    public bool InView = true, Animated = true, Reduced, Live;
    public double Moved;
    public int Admissions;
    public bool WantsAdmission => !Live && InView;
    public void Admit() { Live = true; Admissions++; }
    public bool Animating => Live && InView && Animated && !Reduced;
    public bool Easing => false;
    public int Rank { get; set; } = rank;
    public void Advance(double seconds) => Moved += seconds;
}
