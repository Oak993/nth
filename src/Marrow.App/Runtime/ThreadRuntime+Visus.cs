using Marrow.Core.Web;

// PORT-ONLY: web surfaces W8a, a thread's Visus tabs (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Per thread: ... browser
// tabs (URL, title, scroll, zoom, device preset) ... Stored with the thread in Library", "Restart restores the exact layout. Web
// views come back as snapshots and go live on first sight"; ED-NEW-web-w8a-2). No Swift counterpart. The pane keeps the live
// tabs (Views/Browser/VisusSession); this is their saveable form, private tabs never in it. W3c saves it with the thread.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private VisusTabsState visusTabs = VisusTabsState.Empty;

    /// <summary>The thread's Visus tabs as they are saved (<see cref="VisusTabsState.Normalized"/> on every write).</summary>
    public VisusTabsState VisusTabs
    {
        get => Read(visusTabs);
        set => Set(ref visusTabs, (value ?? VisusTabsState.Empty).Normalized());
    }

    /// <summary>
    /// The thread's folder in Library (where its hand-off files live). DESIGN 2.3: the agent profile is "persistent per thread
    /// folder", named from this path (WebProfiles.AgentProfileName).
    /// </summary>
    internal string VisusThreadFolder => HandoffFolder;
}
