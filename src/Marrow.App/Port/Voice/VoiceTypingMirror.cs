// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-14 review): the in-memory voiceTyping for readers that are not the settings object.
using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>
/// <see cref="AppSettings"/> writes voiceTyping to the store through the write coalescer (CZ-13.5), which holds a second change for 250 ms, so the stored form can be a write behind. A reader on another
/// thread that has no settings object (the capture's microphone pick, the model store's model in use, both bound at start-up) reads the value the settings object holds instead: it publishes it here, per
/// settings store (so a test's own store has its own value and the app has one). A reader finds nothing published only before any settings object was made, and then reads the store.
/// </summary>
internal static class VoiceTypingMirror
{
    private static readonly ConditionalWeakTable<ISettingsStore, StrongBox<VoiceTypingSettings>> Values = new();

    /// <summary>The settings object's current value (any thread may read it).</summary>
    public static void Publish(ISettingsStore store, VoiceTypingSettings value) => Values.GetValue(store, _ => new StrongBox<VoiceTypingSettings>(value)).Value = value;

    /// <summary>The published value for this store, or null.</summary>
    public static VoiceTypingSettings? Read(ISettingsStore store) => Values.TryGetValue(store, out var box) ? box.Value : null;
}
