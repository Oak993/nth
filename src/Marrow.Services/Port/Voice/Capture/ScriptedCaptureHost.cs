// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): the endpoint enumerator double (voice-SPEC V-2.4; the Microphone row's
// "scripted enumerator"). It lists what a test sets and opens scripted devices only.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>An <see cref="ICaptureHost"/> over a scripted list of endpoints. Opens no hardware; records every open.</summary>
public sealed class ScriptedCaptureHost : ICaptureHost
{
    public static readonly DeviceFormat DefaultFormat = new(48000, 2, SampleEncoding.Float32);

    private readonly List<AudioEndpoint> endpoints = [];
    private readonly Dictionary<string, DeviceFormat> formats = [];
    private readonly Dictionary<string, CaptureDeviceFailure> openFailures = [];
    private readonly List<Action<EndpointNotice>> watchers = [];
    private readonly Lock gate = new();

    public ScriptedCaptureHost(params (string Id, string Name)[] initial)
    {
        foreach (var (id, name) in initial) endpoints.Add(new AudioEndpoint(id, name));
        DefaultId = endpoints.Count > 0 ? endpoints[0].Id : null;
    }

    /// <summary>The default endpoint's id; null for a PC with no microphone.</summary>
    public string? DefaultId { get; set; }

    /// <summary>Ids passed to <see cref="Open"/> in order (null for the default), including those that failed.</summary>
    public List<string?> OpenRequests { get; } = [];

    /// <summary>Ids passed to <see cref="OpenCommunications"/> in order (they are in <see cref="OpenRequests"/> too).</summary>
    public List<string?> CommunicationsRequests { get; } = [];

    /// <summary>The devices opened and returned, in order.</summary>
    public List<ScriptedCaptureDevice> Devices { get; } = [];

    /// <summary>How many times <see cref="CaptureEndpoints"/> was asked (enumerating is not opening).</summary>
    public int Enumerations { get; private set; }

    public int WatcherCount
    {
        get { lock (gate) return watchers.Count; }
    }

    /// <summary>When set, every device opened afterwards throws it from <c>Start</c>.</summary>
    public Exception? StartFailure { get; set; }

    public ScriptedCaptureDevice? LastDevice => Devices.Count == 0 ? null : Devices[^1];

    /// <summary>Adds an active endpoint (and tells the watchers).</summary>
    public void Add(string id, string name, DeviceFormat? format = null)
    {
        endpoints.Add(new AudioEndpoint(id, name));
        if (format is { } f) formats[id] = f;
        DefaultId ??= id;
        Notify(new EndpointNotice(EndpointNoticeKind.Added, id));
    }

    /// <summary>Removes an endpoint (unplugged) and tells the watchers; the default moves to the next one when it was the default.</summary>
    public void Remove(string id)
    {
        endpoints.RemoveAll(e => e.Id == id);
        if (DefaultId == id) DefaultId = endpoints.Count > 0 ? endpoints[0].Id : null;
        Notify(new EndpointNotice(EndpointNoticeKind.Removed, id));
    }

    public void SetFormat(string id, DeviceFormat format) => formats[id] = format;

    /// <summary>The next opens of <paramref name="id"/> throw this failure.</summary>
    public void FailOpen(string id, CaptureDeviceFailure failure) => openFailures[id] = failure;

    public void Notify(EndpointNotice notice)
    {
        Action<EndpointNotice>[] snapshot;
        lock (gate) snapshot = [.. watchers];
        foreach (var watcher in snapshot) watcher(notice);
    }

    public IReadOnlyList<AudioEndpoint> CaptureEndpoints()
    {
        Enumerations += 1;
        return [.. endpoints];
    }

    public string? DefaultEndpointId() => DefaultId;

    public ICaptureDevice Open(string? endpointId)
    {
        OpenRequests.Add(endpointId);
        var id = endpointId ?? DefaultId;
        if (id is null || endpoints.All(e => e.Id != id)) throw new CaptureDeviceException(CaptureDeviceFailure.NotFound, "No such capture endpoint.");
        if (openFailures.TryGetValue(id, out var failure)) throw new CaptureDeviceException(failure, "The capture endpoint cannot be opened.");
        var device = new ScriptedCaptureDevice(id, formats.GetValueOrDefault(id, DefaultFormat)) { FailStart = StartFailure };
        Devices.Add(device);
        return device;
    }

    public ICaptureDevice OpenCommunications(string? endpointId)
    {
        CommunicationsRequests.Add(endpointId);
        return Open(endpointId);
    }

    public IDisposable Watch(Action<EndpointNotice> handler)
    {
        lock (gate) watchers.Add(handler);
        return new Subscription(this, handler);
    }

    private sealed class Subscription(ScriptedCaptureHost host, Action<EndpointNotice> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (host.gate) host.watchers.Remove(handler);
        }
    }
}
