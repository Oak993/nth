using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The popover seam, recording what each popup held when it was told to show or hide (headless cannot open a Popup).
/// A test that lifts the content out of its popup to lay it out still knows which popup was showing what.
/// </summary>
public sealed class ContentRecordingPresenter : IPopoverPresenter
{
    public List<(Popup Popup, bool Presented, Control? Content)> Calls { get; } = [];

    public void SetPresented(Popup popup, bool presented) => Calls.Add((popup, presented, popup.Child));

    /// <summary>The popups last told to show and not to hide since, with the content each held then.</summary>
    public IReadOnlyList<(Popup Popup, Control? Content)> Shown()
    {
        var shown = new List<(Popup Popup, Control? Content)>();
        foreach (var (popup, presented, content) in Calls)
        {
            shown.RemoveAll(item => ReferenceEquals(item.Popup, popup));
            if (presented) shown.Add((popup, content));
        }
        return shown;
    }
}
