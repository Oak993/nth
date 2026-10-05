// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10): what a composer gives its voice controller.
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The composer as its voice controller sees it: the app's settings (read when a session begins and for the notes), the draft's text and selection, the replacement of a
/// range of the draft (one edit; the caret ends after it), and the composer's own Return (<c>ComposerView.Send</c>, ComposerView.swift:370-382).
/// </summary>
public sealed record ComposerVoiceHost(
    Func<AppSettings> Settings,
    Func<DraftSelection> Selection,
    Action<int, int, string> Replace,
    Action Send);
