// PORT-ONLY: Tactus (ED-NEW-pcctl-32). The pill on screen as one test uses it: the shared parts on fakes (PcAssemblyRig), the
// pill host on fake bands and monitors (PcPillHostRig), the card's bridge and the feed, the card's keyboard over fake Win32
// calls, the chords on a fake hotkey thread, and what the screen asks the app to do (settings saved, a chat opened,
// Settings opened, the keyboard put in the card). Everything runs inline on the test's UI thread.
using Marrow.App.Port.PcControl;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcScreenRig : IDisposable
{
    public const nint Overlay = 0x50;
    public const nint Word = 0x10;

    public PcScreenRig(PcSettings? settings = null)
    {
        Parts = new PcAssemblyRig(settings);
        Assembly = Parts.Build();
        Pill = PcPillHostRig.Create();
        Cards = new PillAskBridge(Assembly.Asks);
        Hotkeys = new TactusHotkeys(KeysHost, () => Parts.Settings, Assembly.Stop, Assembly.Pause, Assembly.Lanes, _ => false, () => 0);
        Feed = new TactusPillFeed(Assembly, Cards, () => Pill.Now, () => Hotkeys.PauseProblem is not null);
        Native.Front = Word;
        Native.Windows[Word] = (40, "OpusApp");
        Native.Windows[Overlay] = (7, "Tactus");
        Focus = new CardFocus(Native, () => Overlay, _ => false, Pill.Clock, (seconds, run) => FocusLater.Add((seconds, run)));
        var map = new MonitorMap(new PcFakeOverlayMonitors { Now = [PcTestMonitors.Main, PcTestMonitors.Right] });
        map.Refresh();
        Screen = new TactusScreen(new TactusScreenParts
        {
            Assembly = Assembly,
            Host = Pill.Host,
            Screens = Pill.Screens,
            Map = map,
            Cards = Cards,
            Feed = Feed,
            Hotkeys = Hotkeys,
            Focus = Focus,
            Save = next =>
            {
                Saved.Add(next);
                Parts.Settings = next;
            },
            After = (seconds, run) => Later.Add((seconds, run)),
            Now = () => Pill.Now,
            OpenSettings = () => SettingsOpened++,
            OpenChat = (thread, draft) => Chats.Add((thread, draft)),
            KeyboardToCard = () =>
            {
                KeyboardCalls++;
                // the band window is activated: MA_ACTIVATE saves the window the keyboard came from, then Windows activates it
                Focus.Activating();
                Native.Front = Overlay;
                Focus.Activated();
            },
        });
    }

    public PcAssemblyRig Parts { get; }
    public PcAssembly Assembly { get; }
    public PcPillHostRig Pill { get; }
    public PillAskBridge Cards { get; }
    public TactusPillFeed Feed { get; }
    public TactusFakeHotkeyHost KeysHost { get; } = new();
    public TactusHotkeys Hotkeys { get; }
    public PcFakeCardFocusNative Native { get; } = new();
    public CardFocus Focus { get; }
    public TactusScreen Screen { get; }
    public List<PcSettings> Saved { get; } = [];
    public List<(double Seconds, Action Run)> Later { get; } = [];
    public List<(double Seconds, Action Run)> FocusLater { get; } = [];
    public List<(string Thread, string? Draft)> Chats { get; } = [];
    public int SettingsOpened { get; private set; }
    public int KeyboardCalls { get; private set; }

    public void Dispose()
    {
        Screen.Dispose();
        Hotkeys.Dispose();
        Parts.Dispose();
    }
}
