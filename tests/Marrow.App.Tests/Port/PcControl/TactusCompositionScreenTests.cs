// PORT-ONLY: Tactus (ED-NEW-pcctl-32; DESIGN.md 3.2, 4.1, 10.4, 10.7). The composition's new seams on fake backends and a
// fake hotkey thread: the first "on" makes the pill's screen once with the parts and the chords and disposing ends it; the
// surface's grant store reaches the engine's approver; a chat that closes loses its engine. No window, no hook.
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class TactusCompositionScreenTests
{
    private static readonly PcAppIdentity Outlook = new("c:\\office\\outlook.exe");

    [Fact]
    public void The_first_on_makes_the_screen_once_with_the_parts_and_the_chords_and_disposing_ends_it()
    {
        using var rig = new PcAssemblyRig();
        var made = new List<(PcAssembly Assembly, TactusHotkeys Keys)>();
        var screen = new PcDisposeCount();
        var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
            Screen = (assembly, keys) =>
            {
                made.Add((assembly, keys));
                return screen;
            },
        });

        composition.SharedParts();
        composition.PartsFor("chat-a");
        var (assembly, keys) = Assert.Single(made);
        Assert.Same(composition.Assembly, assembly);
        Assert.Same(composition.Hotkeys, keys);
        Assert.Equal(0, screen.Count);

        composition.Dispose();
        composition.Dispose();
        Assert.Equal(1, screen.Count);
    }

    [Fact]
    public void Nothing_makes_a_screen_while_tactus_is_off()
    {
        using var rig = new PcAssemblyRig(PcSettings.Default);
        var made = 0;
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
            Screen = (_, _) =>
            {
                made++;
                return null;
            },
        });

        Assert.Null(composition.SharedParts());
        Assert.Equal(0, made);
    }

    [Fact]
    public void The_surface_s_grant_store_answers_the_engine_s_approver()
    {
        using var rig = new PcAssemblyRig();
        var grants = new PcGrants(new InMemorySettingsStore());
        using var composition = new TactusComposition(new TactusHost
        {
            Settings = () => rig.Settings,
            Backends = () => rig.Backends,
            Hotkeys = _ => new TactusFakeHotkeyHost(),
            Surface = () => new PcSurface { Grants = grants },
        });
        var parts = composition.SharedParts()!;

        Assert.Null(parts.Approver.Standing(Outlook, PcCapability.Act));
        grants.Grant("chat-0", Outlook, "Outlook", use: true, PcGrantScope.Always);

        Assert.Equal(PcApproval.Always, parts.Approver.Standing(Outlook, PcCapability.Act));
        Assert.Same(grants, composition.Assembly!.Surface.Grants);
    }

    [Fact]
    public void A_chat_that_closes_loses_its_engine()
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
        var id = Guid.NewGuid();
        var registration = new PcRegistration(id.ToString("D"), ProviderKind.Claude, null, true);
        var first = PcControlBridge.Engine(registration);
        Assert.NotNull(first);
        Assert.Same(first, PcControlBridge.Engine(registration));

        TactusComposition.ChatClosed(id);
        var again = PcControlBridge.Engine(registration);

        Assert.NotNull(again);
        Assert.NotSame(first, again);
        PcControlBridge.Forget(id.ToString("D"));
    }
}
