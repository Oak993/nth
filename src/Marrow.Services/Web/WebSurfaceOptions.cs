namespace Marrow.Services.Web;

/// <summary>The numbers <see cref="WebSurface"/> runs on (DESIGN 2.3, 2.5, 2.7, 9). Planning values are marked.</summary>
public sealed class WebSurfaceOptions
{
    public const int MinLiveViews = 2;
    public const int MaxLiveViewsLimit = 8;

    private int maxLiveViews = 4;

    /// <summary>
    /// At most this many live web views across every window, pane and widget (DESIGN 2.3: 4; Settings › Visus "Keep at
    /// most N live tabs", 2 to 8; the W0 memory gate may set 3).
    /// </summary>
    public int MaxLiveViews
    {
        get => maxLiveViews;
        set => maxLiveViews = Math.Clamp(value, MinLiveViews, MaxLiveViewsLimit);
    }

    /// <summary>The gesture-start capture budget (DESIGN 2.5: planning value 30 ms; spike b sets it from its p95).</summary>
    public TimeSpan CaptureBudget { get; set; } = TimeSpan.FromMilliseconds(30);

    /// <summary>Tabs not shown this long are discarded to a snapshot plus URL (DESIGN 2.3: 10 minutes).</summary>
    public TimeSpan DiscardAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A pre-warmed environment unused this long is closed (DESIGN 2.3: 10 minutes).</summary>
    public TimeSpan PrewarmIdleClose { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Pre-warm this long after first paint for people who used Visus lately (DESIGN 2.3: 3 s).</summary>
    public TimeSpan PrewarmAfterFirstPaint { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How recently Visus must have been opened for the first-paint pre-warm (DESIGN 2.3: 7 days).</summary>
    public TimeSpan PrewarmRecentUse { get; set; } = TimeSpan.FromDays(7);

    /// <summary>No <c>requestAnimationFrame</c> reply in this long means the page hangs (DESIGN 2.7: 5 s).</summary>
    public TimeSpan HangTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A shown page is pinged for a frame at most this often (ED-NEW-web-W7-6): often enough that a hang shows within
    /// 5 s, seldom enough that an idle page costs next to nothing.
    /// </summary>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>A model step on a hung page fails after this long (DESIGN 2.7: 25 s).</summary>
    public TimeSpan StepHangTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>The settle wait for page frames before a view shows again (DESIGN 2.5: two frames, 120 ms).</summary>
    public TimeSpan SettleFrameTimeout { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// How long a suspend waits for the host to put a shown view's snapshot on screen and hide the live view before it
    /// hides the view itself (ED-NEW-web-W7-3: the host needs one composed frame; this bounds a host that is not rendering).
    /// </summary>
    public TimeSpan HideWait { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Crashes of one page within <see cref="CrashWindow"/> that stop automatic reloads (DESIGN 2.7: 3 in 5 minutes).</summary>
    public int CrashLimit { get; set; } = 3;

    public TimeSpan CrashWindow { get; set; } = TimeSpan.FromMinutes(5);
}
