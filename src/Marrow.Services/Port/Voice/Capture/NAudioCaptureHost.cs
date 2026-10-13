// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): the production endpoint seam (voice-SPEC V-4.1, V-4.4).
using NAudio.CoreAudioApi;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// The Windows capture endpoints through NAudio: <c>MMDeviceEnumerator</c> for the list, the default (Console role) and a device by id, and NAudio's notification client
/// (IMMNotificationClient) for the changes of V-4.4. Listing and watching open no stream; only <see cref="Open"/> does, through <see cref="NAudioCaptureDevice"/>.
/// </summary>
public sealed class NAudioCaptureHost : ICaptureHost
{
    // PKEY_AudioEngine_DeviceFormat: the endpoint's shared-mode mix format.
    private static readonly Guid DeviceFormatKey = new("f19f064d-082c-4e27-bc73-6882a1bb8e4c");

    public IReadOnlyList<AudioEndpoint> CaptureEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        var endpoints = new List<AudioEndpoint>(devices.Count);
        for (var i = 0; i < devices.Count; i++)
        {
            using var device = devices[i];
            endpoints.Add(new AudioEndpoint(device.ID, NameOf(device)));
        }
        return endpoints;
    }

    public string? DefaultEndpointId()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var device)) return null;
        using (device) return device.ID;
    }

    public ICaptureDevice Open(string? endpointId) => OpenEndpoint(endpointId, communications: false);

    public ICaptureDevice OpenCommunications(string? endpointId) => OpenEndpoint(endpointId, communications: true);

    private static NAudioCaptureDevice OpenEndpoint(string? endpointId, bool communications)
    {
        MMDevice device;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (endpointId is null)
            {
                if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out device)) throw new CaptureDeviceException(CaptureDeviceFailure.NotFound, "There is no default capture endpoint.");
            }
            else
            {
                device = enumerator.GetDevice(endpointId);
            }
        }
        catch (CaptureDeviceException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new CaptureDeviceException(CaptureDeviceFailure.NotFound, "The capture endpoint was not found.", error);
        }
        if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Capture)
        {
            device.Dispose();
            throw new CaptureDeviceException(CaptureDeviceFailure.NotFound, "The endpoint is not an active capture endpoint.");
        }
        return new NAudioCaptureDevice(device, communications);
    }

    public IDisposable Watch(Action<EndpointNotice> handler)
    {
        var enumerator = new MMDeviceEnumerator();
        var client = enumerator.CreateNotificationClient(useSynchronizationContext: false);
        client.DeviceAdded += (_, e) => Tell(handler, EndpointNoticeKind.Added, e.DeviceId);
        client.DeviceRemoved += (_, e) => Tell(handler, EndpointNoticeKind.Removed, e.DeviceId);
        client.DeviceStateChanged += (_, e) => Tell(handler, e.NewState == DeviceState.Active ? EndpointNoticeKind.Added : EndpointNoticeKind.Removed, e.DeviceId);
        client.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Capture && e.Role == Role.Console) Tell(handler, EndpointNoticeKind.DefaultChanged, e.DeviceId);
        };
        client.PropertyValueChanged += (_, e) =>
        {
            if (e.PropertyKey.formatId == DeviceFormatKey && e.PropertyKey.propertyId == 0) Tell(handler, EndpointNoticeKind.FormatChanged, e.DeviceId);
        };
        return new Subscription(enumerator, client);
    }

    private static string NameOf(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch (Exception)
        {
            return device.ID; // an endpoint whose properties cannot be read is still listed, by its id
        }
    }

    // A notification runs on a COM thread: whatever the handler does, nothing may escape into Windows.
    private static void Tell(Action<EndpointNotice> handler, EndpointNoticeKind kind, string? id)
    {
        try
        {
            handler(new EndpointNotice(kind, id));
        }
        catch (Exception)
        {
            // The subscriber's failure is the subscriber's.
        }
    }

    private sealed class Subscription(MMDeviceEnumerator enumerator, MMDeviceNotificationClient client) : IDisposable
    {
        public void Dispose()
        {
            client.Dispose();
            enumerator.Dispose();
        }
    }
}
