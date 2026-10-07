// PORT-ONLY: connects the independently mounted VT-17 bridge to the existing polite live region without modifying the pill/controller owner's files.
using System.Runtime.CompilerServices;
using Marrow.Core.Port.Voice.Session;
namespace Marrow.App.Port.Voice.Notes;
internal sealed class VoiceErrorAnnouncements
{
    private static readonly ConditionalWeakTable<VoiceSession, VoiceErrorAnnouncements> Regions = new();
    internal static VoiceErrorAnnouncements For(VoiceSession session) => Regions.GetValue(session, _ => new());
    internal event Action<string>? Announced;
    internal void Say(string text) => Announced?.Invoke(text);
}
