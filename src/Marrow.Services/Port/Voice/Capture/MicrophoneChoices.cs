// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-19 B; first-release voice typing, VT-5): the Microphone row's list and the choice's fallback.
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>Lists the input choices and resolves a stored pick against the endpoints that are there now. Neither opens a device.</summary>
public static class MicrophoneChoices
{
    /// <summary>
    /// "Windows default" first (Q-V-19 B), then every active capture endpoint in the system's order. A menu id must be unique and the empty id is "Windows default"'s (review of VT-13), so an
    /// endpoint with no id, with the id "Windows default" (which a stored pick reads as the default, <see cref="Resolve"/>), or with an id already listed (compared as <see cref="Resolve"/> does) is left out.
    /// </summary>
    public static IReadOnlyList<MicrophoneItem> Items(ICaptureHost host)
    {
        var items = new List<MicrophoneItem> { new(null, VoiceTypingSettings.WindowsDefaultMicrophone) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in host.CaptureEndpoints())
        {
            if (string.IsNullOrEmpty(endpoint.Id) || string.Equals(endpoint.Id, VoiceTypingSettings.WindowsDefaultMicrophone, StringComparison.Ordinal) || !seen.Add(endpoint.Id)) continue;
            items.Add(new MicrophoneItem(endpoint.Id, endpoint.Name));
        }
        return items;
    }

    /// <summary>
    /// What a stored <c>mic</c> opens: absent, empty or "Windows default" is the default; an id that is an active endpoint is that endpoint (with the system's own
    /// spelling of the id); an id that is not there falls back to the default and says so.
    /// </summary>
    public static MicrophoneResolution Resolve(ICaptureHost host, string? mic)
    {
        if (string.IsNullOrEmpty(mic) || string.Equals(mic, VoiceTypingSettings.WindowsDefaultMicrophone, StringComparison.Ordinal)) return new MicrophoneResolution(null, false);
        foreach (var endpoint in host.CaptureEndpoints())
        {
            if (string.Equals(endpoint.Id, mic, StringComparison.OrdinalIgnoreCase)) return new MicrophoneResolution(endpoint.Id, false);
        }
        return new MicrophoneResolution(null, true);
    }
}
