using Marrow.App.Jobs;
using Marrow.Core.Models;
using Marrow.Services.Port.Downloads;

// PORT-ONLY: web surfaces W5, the app's one list of background jobs (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.10;
// ED-NEW-web-w5-1). No Swift counterpart. Made on first use (the pane opening, or a publisher), so a build that never opens
// the pane never reads a thread for it.

namespace Marrow.App;

public sealed partial class AppModel
{
    private BackgroundJobs? backgroundJobs;
    private PublishedJobSource? publishedJobs;

    /// <summary>
    /// Where Tactus streams, Visus agent tabs, workflows and the assistant's missions publish their jobs (<see cref="PublishedJobSource.Publish"/>,
    /// <see cref="PublishedJobSource.Retire"/>), each with the handles that stop, pause and resume it. Thread-safe.
    /// </summary>
    public PublishedJobSource PublishedJobs => publishedJobs ??= new PublishedJobSource("Published");

    /// <summary>
    /// Every background job of the app: Hydra heads, helpers and subagents, terminals and dev servers, downloads, and whatever was published.
    /// The Background tasks pane and the assistant's mission board read this one list.
    /// </summary>
    public BackgroundJobs BackgroundJobs
    {
        get
        {
            if (backgroundJobs is not null) return backgroundJobs;
            var jobs = new BackgroundJobs();
            jobs.AddSource(new HydraHeadJobSource(this));
            jobs.AddSource(new HelperJobSource(this));
            jobs.AddSource(new TerminalJobSource(this));
            jobs.AddSource(new DownloadJobSource(DownloadJobRegistry.Shared));
            jobs.AddSource(new Marrow.App.Views.Browser.VisusDownloadJobSource(() => Marrow.App.Views.Browser.VisusHost.Downloads)); // web W8b
            jobs.AddSource(PublishedJobs);
            return backgroundJobs = jobs;
        }
    }
}
