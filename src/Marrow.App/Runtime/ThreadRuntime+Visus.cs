using Marrow.Core.Models;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W8a, a thread's Visus tabs (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Per thread: ... browser
// tabs (URL, title, scroll, zoom, device preset) ... Stored with the thread in Library", "Restart restores the exact layout. Web
// views come back as snapshots and go live on first sight"; ED-NEW-web-w8a-2). No Swift counterpart. The pane keeps the live
// tabs (Views/Browser/VisusSession); this is their saveable form, private tabs never in it, saved with the thread as the
// pane's data (W3c's SetPaneState).

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    /// <summary>
    /// The thread's Visus tabs as they are saved (<see cref="VisusTabsState.Normalized"/> on every write), kept with the thread
    /// as the Browser pane's data (<see cref="SetPaneState"/>, web W3c); no tabs keep nothing.
    /// </summary>
    public VisusTabsState VisusTabs
    {
        get => VisusTabsState.FromJson(GetPaneState(PaneKind.Browser));
        set
        {
            var next = (value ?? VisusTabsState.Empty).Normalized();
            SetPaneState(PaneKind.Browser, next.Tabs.Count == 0 ? null : next.ToJson());
        }
    }

    /// <summary>
    /// The thread's folder in Library (where its hand-off files live). DESIGN 2.3: the agent profile is "persistent per thread
    /// folder", named from this path (WebProfiles.AgentProfileName).
    /// </summary>
    internal string VisusThreadFolder => HandoffFolder;
}
