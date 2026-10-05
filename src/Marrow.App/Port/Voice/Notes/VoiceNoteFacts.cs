// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): what the seams reported when a session ended in a note.
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Port.Voice.Notes;

/// <summary>The session's note kind and cause, the last device event, and what the app knows beside them (a model download under way; a processor without AVX2).</summary>
public sealed record VoiceNoteFacts(VoiceNoteKind Kind, Exception? Cause = null, CaptureDeviceEventKind? DeviceEvent = null, bool ModelDownloading = false, bool ProcessorLacksAvx2 = false);
