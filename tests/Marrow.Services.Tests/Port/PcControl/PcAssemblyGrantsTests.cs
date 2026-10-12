// PORT-ONLY: Tactus (ED-NEW-pcctl-25, ED-NEW-pcctl-32; DESIGN.md 4.1, 10.4). The grant store bound through the surface: the
// assembly's approver reads the "Always" grants from it and writes the card's answers to it, a chat grant covers that chat's
// later steps without a card, and without the store every app is asked (the surface's Standing alone answers). Fakes only.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class PcAssemblyGrantsTests
{
    private static readonly PcAppIdentity Outlook = new("c:\\office\\outlook.exe");

    [Fact]
    public async Task With_the_grant_store_the_approver_reads_and_writes_it()
    {
        using var rig = new PcAssemblyRig();
        var grants = new PcGrants(new InMemorySettingsStore());
        grants.Grant("chat-0", Outlook, "Outlook", use: true, PcGrantScope.Always);
        var assembly = rig.Build(new PcSurface { Grants = grants });

        Assert.Equal(PcApproval.Always, assembly.Approver.Standing(Outlook, PcCapability.Act));

        var asking = assembly.Approver.Ask(new PcAskSource("chat-1", "Outlook"), new PcAppIdentity("c:\\office\\excel.exe"), "Excel", PcCapability.Act, false, true, CancellationToken.None);
        var ask = Assert.Single(assembly.Asks.Pending);
        Assert.Equal(PcResolve.Done, assembly.Asks.Resolve(ask.Id, 0, new PcApprovalReply(PcApproval.Chat, Use: true), PcAnswerSource.Pill));
        Assert.Equal(PcApproval.Chat, await asking.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(PcApproval.Chat, grants.ForChat("chat-1", new PcAppIdentity("c:\\office\\excel.exe"), PcCapability.Act));
        Assert.Equal(PcApproval.Chat, await assembly.Approver.Ask(new PcAskSource("chat-1", "Outlook"), new PcAppIdentity("c:\\office\\excel.exe"), "Excel", PcCapability.Act, false, true, CancellationToken.None));
        Assert.Empty(assembly.Asks.Pending);
    }

    [Fact]
    public void Without_the_grant_store_the_surface_s_standing_alone_answers()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build(new PcSurface { Standing = (app, capability) => app == Outlook && capability == PcCapability.See ? PcApproval.Always : null });

        Assert.Equal(PcApproval.Always, assembly.Approver.Standing(Outlook, PcCapability.See));
        Assert.Null(assembly.Approver.Standing(Outlook, PcCapability.Act));
        Assert.Null(assembly.Surface.Grants);
    }
}
