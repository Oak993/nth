using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The app's chats for the Tactus chat mirror: a fixed set of fake chats and the runtimes' hooks raised by hand.</summary>
public sealed class TactusFakeChats : ITactusChats
{
    public Dictionary<string, TactusFakeChat> Chats { get; } = new(StringComparer.Ordinal);

    public event Action<string>? Interrupted;

    public event Action<string>? UserWrote;

    public event Action<string, string, PcAnswer>? QuestionAnswered;

    public TactusFakeChat Add(string thread) => Chats[thread] = new TactusFakeChat(thread);

    public ITactusChat? Find(string thread) => Chats.GetValueOrDefault(thread);

    public void Interrupt(string thread) => Interrupted?.Invoke(thread);

    public void Write(string thread) => UserWrote?.Invoke(thread);

    public void Answer(string thread, string id, PcAnswer answer) => QuestionAnswered?.Invoke(thread, id, answer);
}
