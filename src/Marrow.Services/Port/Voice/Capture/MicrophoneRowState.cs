// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-19 B; first-release voice typing, VT-13): what the Voice typing page's Microphone row shows.
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// The Microphone row for a stored pick: the list (<see cref="MicrophoneChoices.Items"/>), the item that is checked now and whether the stored pick is a device that is not there.
/// A pick that is not there shows "Windows default", as the capture opens it (VT-5); the stored id is not changed, so the same device is the pick again when it returns.
/// A seam that cannot list (not composed yet, or the system's error) leaves the first item only: the row never throws.
/// </summary>
public sealed record MicrophoneRowState(IReadOnlyList<MicrophoneItem> Items, string? SelectedValue, bool PickIsMissing)
{
    /// <summary>The menu id of "Windows default": the empty string, which no endpoint id is.</summary>
    public const string DefaultId = "";

    /// <summary>The label of the checked item.</summary>
    public string SelectedLabel => Items.First(item => item.Value == SelectedValue).Label;

    /// <summary>The checked item's menu id.</summary>
    public string SelectedId => SelectedValue ?? DefaultId;

    /// <summary>The menu's options: id and text per item.</summary>
    public IReadOnlyList<(string Id, string Text)> MenuOptions => [.. Items.Select(item => (item.Value ?? DefaultId, item.Label))];

    /// <summary>The value a menu id stores in <c>voiceTyping.mic</c>: null for "Windows default".</summary>
    public static string? ValueOf(string menuId) => menuId.Length == 0 ? null : menuId;

    /// <summary>Lists the endpoints and resolves <paramref name="mic"/> against them (no device is opened).</summary>
    public static MicrophoneRowState Of(ICaptureHost host, string? mic)
    {
        try
        {
            var items = MicrophoneChoices.Items(host);
            var resolved = MicrophoneChoices.Resolve(host, mic);
            return new MicrophoneRowState(items, resolved.EndpointId, resolved.FellBack);
        }
        catch (Exception)
        {
            return new MicrophoneRowState([new MicrophoneItem(null, VoiceTypingSettings.WindowsDefaultMicrophone)], null, false);
        }
    }
}
