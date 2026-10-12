// PORT-ONLY: Tactus (ED-NEW-pcctl-40; DESIGN.md 3.2, 3.3, 4.4, 10.2). The app's one composition point, on fake backends and
// a fake hotkey thread: nothing is built while Tactus is off; the first "on" builds the parts once, with the Tactus chords
// as the stop key the engine arms; the switch going off stops everything once; a chat's turn end is watched once and ends
// its Tactus turn; heads are attended by idle time and the lead window; and the nth-pc entry is offered only while on. No
// real hook, hotkey, window or provider.
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-40")]
public sealed class TactusCompositionTests
{
    [Fact]
    public void Nothing_is_built_while_tactus_is_off()
    {
        using var rig = new PcAssemblyRig(PcSettings.Default);
        var backends = 0;
        var hosts = 0;
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => { backends++; return rig.Backends; },
            Hotkeys = _ => { hosts++; return new TactusFakeHotkeyHost(); },
        });

        Assert.Null(composition.SharedParts());
        Assert.Null(composition.PartsFor("chat-a"));
        Assert.Null(composition.Assembly);
        Assert.Null(composition.Hotkeys);
        Assert.Equal(0, backends);
        Assert.Equal(0, hosts);
    }

    [Fact]
    public void The_first_on_builds_the_parts_once_with_the_tactus_chords_as_the_stop_key()
    {
        using var rig = new PcAssemblyRig();
        var backends = 0;
        IPhysicalInput? given = null;
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => { backends++; return rig.Backends; },
            Hotkeys = input => { given = input; return new TactusFakeHotkeyHost(); },
        });

        var shared = composition.SharedParts();
        var again = composition.SharedParts();
        var chat = composition.PartsFor("chat-a");

        Assert.NotNull(shared);
        Assert.Same(shared, again);
        Assert.NotNull(chat);
        Assert.Same(shared.Control, chat.Control);
        Assert.Equal(1, backends);
        Assert.Same(rig.Input, given);
        Assert.Same(composition.Hotkeys, Assert.IsType<TactusHotkeys>(shared.StopKey));
        Assert.Equal("Ctrl+Alt+Esc", shared.StopKey.Chord);
        Assert.False(shared.Settings().RealInput);
    }

    [Fact]
    public void The_switch_going_off_stops_everything_once_and_offers_nothing()
    {
        using var rig = new PcAssemblyRig();
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
        });
        var parts = composition.SharedParts()!;
        var stops = new List<(PcScope, PcStopSource)>();
        parts.Control.Stopped += (scope, source) => stops.Add((scope, source));

        rig.Settings = rig.Settings with { Enabled = false };
        Assert.Null(composition.SharedParts());
        Assert.Null(composition.PartsFor("chat-a"));

        Assert.Equal((PcScope.All, PcStopSource.Settings), Assert.Single(stops));
        Assert.NotNull(composition.Assembly);
    }

    [Fact]
    public void A_chats_turn_end_is_watched_once_and_frees_the_windows_it_held()
    {
        using var rig = new PcAssemblyRig();
        var watches = new List<(string Thread, Action Ended)>();
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
            WatchTurnEnd = (thread, ended) => watches.Add((thread, ended)),
        });
        var thread = "chat-" + Guid.NewGuid().ToString("N");
        using var shared = PcControlBridge.Parts.Override(composition.SharedParts);
        using var each = PcControlBridge.PartsFor.Override(composition.PartsFor);
        Assert.NotNull(PcControlBridge.Engine(new PcRegistration(thread, ProviderKind.Claude, null, true)));
        composition.PartsFor(thread);
        var ownership = composition.Assembly!.Ownership;
        Assert.Null(ownership.Claim(0x1a, thread, "the lead chat"));

        var watched = Assert.Single(watches);
        Assert.Equal(thread, watched.Thread);
        watched.Ended();

        Assert.Null(ownership.Holder(0x1a, "someone else"));
        composition.PartsFor(thread);
        Assert.Equal(2, watches.Count);
        PcControlBridge.Forget(thread);
    }

    [Fact]
    public void Heads_are_attended_by_idle_time_and_the_lead_window()
    {
        using var rig = new PcAssemblyRig();
        long idle = 0;
        var minimized = false;
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
            IdleMs = () => idle,
            LeadMinimized = () => minimized,
        });

        Assert.True(composition.Attended("head"));
        idle = PcAttendance.AwayAfterMs + 1;
        Assert.False(composition.Attended("head"));
        idle = 0;
        minimized = true;
        Assert.False(composition.Attended("head"));
    }

    [Fact]
    public void The_nth_pc_entry_is_offered_only_while_tactus_is_on()
    {
        using var rig = new PcAssemblyRig();
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
        });
        using var shared = PcControlBridge.Parts.Override(composition.SharedParts);
        using var each = PcControlBridge.PartsFor.Override(composition.PartsFor);
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/work")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
            IgnisThreadID = Guid.NewGuid(),
        };

        Assert.NotNull(PcControlBridge.Server(configuration));
        rig.Settings = rig.Settings with { Enabled = false };
        Assert.Null(PcControlBridge.Server(configuration));
    }

    [Fact]
    public void Disposing_the_composition_disposes_the_parts_and_the_hotkey_thread()
    {
        var rig = new PcAssemblyRig();
        var host = new TactusCountingHotkeyHost();
        var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => host,
        });
        composition.SharedParts();

        composition.Dispose();
        composition.Dispose();

        Assert.Equal(1, rig.Owned.Count);
        Assert.Equal(1, host.Disposals);
        Assert.Null(composition.SharedParts());
    }
}
