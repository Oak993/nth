// PORT-ONLY: Tactus, the chat mirror (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.7). No Swift twin. The app's chats for
// TactusChatBridge: a thread id finds its open runtime, and the runtimes' Tactus hooks (the chat's Stop, the user writing,
// a pc_ask question answered in the sheet) come out as the bridge's events.
using Marrow.App.Runtime;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Port.PcControl;

internal sealed class TactusRuntimes : ITactusChats
{
    public event Action<string>? Interrupted
    {
        add => ThreadRuntime.TactusInterrupted += value;
        remove => ThreadRuntime.TactusInterrupted -= value;
    }

    public event Action<string>? UserWrote
    {
        add => ThreadRuntime.TactusUserWroteIn += value;
        remove => ThreadRuntime.TactusUserWroteIn -= value;
    }

    public event Action<string, string, PcAnswer>? QuestionAnswered
    {
        add => ThreadRuntime.TactusQuestionAnswered += value;
        remove => ThreadRuntime.TactusQuestionAnswered -= value;
    }

    public ITactusChat? Find(string thread) =>
        Guid.TryParse(thread, out var id) ? Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(id) : null;
}
