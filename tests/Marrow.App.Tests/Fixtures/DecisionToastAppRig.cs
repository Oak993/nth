using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

// An app with Dextra started over a temp store (AS-9): the decision toasts go to a fake channel, the chat toasts to a recording center, the Windows
// calls of a click to a recorder, and "the app is in front" is a flag. Time is the harness's manual clock. Made inside HeadlessSession.OnUiThread.
internal sealed class DecisionToastAppRig : IDisposable
{
    private readonly ThreadRuntimeTurnRig rig;
    private readonly AssistantTempRoot temp = new();
    private readonly AssistantStore store;
    private readonly List<IDisposable> scopes = [];

    public DecisionToastAppRig(Func<ChatThread, ChatThread>? configure = null, bool active = false, bool fakeChannel = true)
    {
        rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure);
        store = AssistantStore.Open(temp.Roots);
        Active = active;
        Center = new RecordingAppToastCenter();
        Windows = new RecordingToastWindows();
        if (fakeChannel) rig.App.DecisionChannelOverride = Channel;
        scopes.Add(NSApplication.IsActive.Override(() => Active));
        scopes.Add(ToastNotifications.Center.Override(Center));
        scopes.Add(ToastActivationWindows.Current.Override(Windows));
        Controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => [new AssistantProfile("dextra", "Dextra"), new AssistantProfile("store", "Store")]));
        Delegate = new AppDelegate { Model = rig.App };
    }

    public AppModel App => rig.App;

    public AppModelHarness H => rig.H;

    public ChatThread Thread => rig.InitialThread;

    public AssistantController Controller { get; }

    public FakeNotificationChannel Channel { get; } = new();

    public RecordingAppToastCenter Center { get; }

    public RecordingToastWindows Windows { get; }

    public AppDelegate Delegate { get; }

    public bool Active { get; set; }

    public List<(DecisionItem Card, DecisionAnswer Answer)> Answers { get; } = [];

    public DecisionItem Raise(DecisionItem card) => H.Main.OnMain(() => Controller.Queue.Raise(card, (item, answer) => Answers.Add((item, answer))));

    public void Pass(TimeSpan by) => H.Main.Advance(by);

    /// <summary>A press on a toast, as Windows hands it to the running app (the protocol launch, forwarded).</summary>
    public void Press(IReadOnlyDictionary<string, object> info) =>
        H.Main.Loop.Run(() => Delegate.UserNotificationCenter(new UNNotificationResponse(new UNNotification(info))), AppModelHarness.Bound);

    public static Dictionary<string, object> Info(Guid decision, NoticeAction action, string nonce) => new(StringComparer.Ordinal)
    {
        ["assistantDecision"] = decision.ToString("D"),
        ["action"] = DecisionNoticeWords.Key(action),
        ["nonce"] = nonce,
    };

    public void Dispose()
    {
        H.Main.OnMain(App.UnbindDextra);
        Controller.Dispose();
        for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
        store.Dispose();
        rig.Dispose();
        temp.Dispose();
    }
}
