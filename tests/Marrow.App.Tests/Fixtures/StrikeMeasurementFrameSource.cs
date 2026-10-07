using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Counts main delivery without allocating a new wrapper on each request.
public sealed class StrikeMeasurementFrameSource(IFrameSource inner) : IFrameSource, IRenderFrameSourceProvider
{
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> callbacks = new();
    public List<RenderFrameSource> RenderSources { get; } = new();
    public long MainFrames { get; private set; }
    public int Pending => inner.Pending;

    public RenderFrameSource? RenderFrames(Control host)
    {
        var source = (inner as IRenderFrameSourceProvider)?.RenderFrames(host);
        if (source is not null) RenderSources.Add(source);
        return source;
    }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        if (!callbacks.TryGetValue(frame, out var wrapped))
        {
            wrapped = stamp => { MainFrames++; frame(stamp); };
            callbacks.Add(frame, wrapped);
        }
        inner.RequestFrame(wrapped);
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        if (callbacks.TryGetValue(frame, out var wrapped)) inner.CancelFrame(wrapped);
    }
}
