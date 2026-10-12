// PORT-ONLY: Tactus, the pill's real windows and calls (OD-0145, ED-NEW-pcctl-32; DESIGN.md 10.3, 10.4, 10.6, 10.7). No Swift
// twin. Makes TactusScreen on the UI thread for one set of shared parts: the input and shadow bands (PillBandWindows over
// Win32, the hit test in force), the monitors, the pill host, the card's bridge to the broker, the chat mirror
// (TactusChatBridge.Current, over the app's runtimes), the card's keyboard (CardFocus over Win32: the band window's
// activation, the reply field's focus, the hand-back), the pointers, the user's pointer and buttons (the hooks', else
// GetCursorPos and GetAsyncKeyState), the auto-dodge (window events, caption buttons and the top taskbar, read off the UI
// thread) and the full-screen watch as the pill's shade (another monitor, or the dot), and the app's own calls (Settings at
// Tactus, a chat opened at its asks). Never run by automated tests; the composition's tests bind fakes through TactusHost.Screen.
using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl;

internal static class TactusScreenDesktop
{
    public static TactusScreen Make(PcAssembly assembly, TactusHotkeys hotkeys)
    {
        var clock = TimeProvider.System;
        double Now() => clock.GetTimestamp() / (double)clock.TimestampFrequency;
        var bands = new PillBandWindows(new Win32OverlayNative());
        var surfaces = new PillBandWindowSurfaces(bands);
        var overlays = new PillOverlays(bands, new Win32OverlayBandNative(), OverlayHitTestSwitch.Current(), Now);
        var map = new MonitorMap(new Win32OverlayMonitors());
        map.Refresh();
        var screens = new PillScreens();
        // the full-screen shade of each monitor (the bands are never the full-screen window)
        var fullscreen = new FullscreenWatch(new Win32FullscreenSource(), Now);
        overlays.HandlesChanged += () =>
        {
            fullscreen.Ignore.Clear();
            fullscreen.Ignore.UnionWith(overlays.Handles);
            fullscreen.Invalidate();
        };
        var host = new TactusPillHost(overlays, surfaces, map, screens, fullscreen.State, clock: clock);
        var cards = new PillAskBridge(assembly.Asks);
        var feed = new TactusPillFeed(assembly, cards, Now, () => hotkeys.PauseProblem is not null);
        var chatFeed = new PcChatFeed(assembly.Pause, assembly.Stop, assembly.Asks, assembly.Clock);
        var chat = new TactusChatBridge(chatFeed, assembly.Asks, cards, new TactusRuntimes(), () => assembly.Settings.ResumeContinuesChat,
            post: action => Dispatcher.UIThread.Post(action));
        TactusChatBridge.Current = chat;

        // the band window of each monitor: the card's keyboard comes from the one the pill shows on
        var windows = new Dictionary<string, PillOverlayWindow>(StringComparer.OrdinalIgnoreCase);
        nint Shown() => host.MonitorId is { } id && windows.TryGetValue(id, out var window) ? window.Handle : 0;
        // a borrow that waits for the user counts for every window (the lease does not say which)
        var focus = new CardFocus(new CardFocusWin32(), Shown, _ => assembly.Lease.Waits.Count > 0, clock,
            (seconds, run) => DispatcherTimer.RunOnce(run, TimeSpan.FromSeconds(seconds)));
        bands.Opened += (monitor, input, _) =>
        {
            windows[monitor.Id] = input;
            input.Activating += focus.Activating;
            input.Activated += (_, _) => focus.Activated();
            input.Deactivated += (_, _) => focus.Deactivated();
            input.DisplayChanged += () => host.Displays(map.Refresh());
            input.Closed += (_, _) =>
            {
                if (windows.TryGetValue(monitor.Id, out var open) && open == input) windows.Remove(monitor.Id);
            };
        };
        var pointers = new PointerOverlays(new PointerWindows(), new Win32PointerScreens(assembly.Shared.Catalog, Now), Now, () => host.ReduceMotion);
        var pointer = new TactusPointerState(assembly.Input, new Win32PointerProbe(), Now);
        var dodgeEvents = new Win32DodgeEvents();
        var dodge = new TactusDodge(dodgeEvents, new CaptionButtons(new Win32CaptionButtonSource(assembly.Shared.Reader)), host, overlays, Now,
            action => Dispatcher.UIThread.Post(action), (seconds, run) => DispatcherTimer.RunOnce(run, TimeSpan.FromSeconds(seconds)),
            invalidate: fullscreen.Invalidate);

        return new TactusScreen(new TactusScreenParts
        {
            Assembly = assembly,
            Host = host,
            Screens = screens,
            Map = map,
            Cards = cards,
            Feed = feed,
            Hotkeys = hotkeys,
            Focus = focus,
            Pointers = pointers,
            Pointer = pointer,
            Overlays = overlays,
            Save = settings => PcSettingsStore.Write(UserDefaults.Standard.Current, settings),
            Post = action => Dispatcher.UIThread.Post(action),
            After = (seconds, run) => DispatcherTimer.RunOnce(run, TimeSpan.FromSeconds(seconds)),
            Now = Now,
            OpenSettings = () => MainActor.Post(() => Marrow.App.Window.WindowManager.Shared.ShowSettings(Marrow.App.Views.Settings.SettingsPage.PcControl)),
            OpenChat = OpenChat,
            KeyboardToCard = () =>
            {
                if (host.MonitorId is not { } id || !windows.TryGetValue(id, out var window)) return;
                focus.Activating();
                window.Activate();
                host.Pill(id)?.Card.Current?.Composer?.Field.Focus();
            },
            Owned = [new Releasing(() =>
            {
                if (ReferenceEquals(TactusChatBridge.Current, chat)) TactusChatBridge.Current = null;
            }), chat, chatFeed, dodge, dodgeEvents, overlays, pointer],
        });
    }

    // A chat opened at its asks; the card's draft is not put in the chat's composer yet (ED-NEW-pcctl-32 item 6).
    private static void OpenChat(string thread, string? draft)
    {
        if (!Guid.TryParse(thread, out var id)) return;
        MainActor.Post(() =>
        {
            var windows = Marrow.App.Window.WindowManager.Shared;
            windows.ShowMain();
            windows.Model.SelectedThreadID = id;
        });
    }

    private sealed class Releasing(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
