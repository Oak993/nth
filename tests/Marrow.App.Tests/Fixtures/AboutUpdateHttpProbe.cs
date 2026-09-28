using System.Diagnostics;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: observe .NET's real HTTP transport, scoped to this test's W3C trace. No transport is replaced.
// The positive control uses the same checker/row and a loopback feed to prove the observer is live.
public sealed class AboutUpdateHttpProbe : IDisposable
{
    private readonly Activity root = new Activity("about-update-proof").SetIdFormat(ActivityIdFormat.W3C);
    private readonly ActivityListener listener;
    private int requests;
    public int Requests => Volatile.Read(ref requests);

    public AboutUpdateHttpProbe()
    {
        root.Start();
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => { if (activity.TraceId == root.TraceId) Interlocked.Increment(ref requests); },
        };
        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose() { listener.Dispose(); root.Dispose(); }
}
