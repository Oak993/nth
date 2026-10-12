// PORT-ONLY: Tactus (ED-NEW-pcctl-40; DESIGN.md 2.10, 3.3). The bridge's engines over the app's composition: each chat's
// engine gets that chat's parts (its own audit log), "attended" is asked for Hydra heads only (the lead chat is never
// limited by it), and the bridge lists the chats that have an engine. Fake desktop, no provider.
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-40")]
public sealed class PcBridgePartsTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    [Fact]
    public async Task A_chats_engine_runs_on_that_chats_parts_and_writes_its_own_audit_log()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        var thread = "chat-" + Guid.NewGuid().ToString("N");
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var each = PcControlBridge.PartsFor.Override(assembly.PartsFor);

        var engine = PcControlBridge.Engine(new PcRegistration(thread, ProviderKind.Claude, "claude-test", true))!;
        var result = await PcEngineHarness.Call(engine, "pc_observe", Look);

        Assert.True(result.Ok, result.Render());
        Assert.Single(rig.Audits[thread].Entries);
        Assert.Contains(thread, PcControlBridge.Threads);
        PcControlBridge.Forget(thread);
        Assert.DoesNotContain(thread, PcControlBridge.Threads);
    }

    [Fact]
    public async Task Attended_is_asked_for_heads_only()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        var lead = "chat-" + Guid.NewGuid().ToString("N");
        var head = "chat-" + Guid.NewGuid().ToString("N");
        var asked = new List<string>();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var each = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var away = PcControlBridge.Attended.Override(thread =>
        {
            lock (asked) asked.Add(thread);
            return false;
        });

        var leadResult = await PcEngineHarness.Call(PcControlBridge.Engine(new PcRegistration(lead, ProviderKind.Codex, null, true))!, "pc_observe", Look);
        var headResult = await PcEngineHarness.Call(PcControlBridge.Engine(new PcRegistration(head, ProviderKind.Codex, null, true) { Head = "a Hydra head" })!, "pc_observe", Look);

        Assert.True(leadResult.Ok, leadResult.Render());
        Assert.Equal(PcErrorCode.Unattended, headResult.Error);
        Assert.DoesNotContain(lead, asked);
        Assert.Contains(head, asked);
        PcControlBridge.Forget(lead);
        PcControlBridge.Forget(head);
    }

    [Fact]
    public void No_engine_while_the_composition_has_no_parts_for_the_chat()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var each = PcControlBridge.PartsFor.Override(_ => null);

        Assert.Null(PcControlBridge.Engine(new PcRegistration("chat-" + Guid.NewGuid().ToString("N"), ProviderKind.Claude, null, true)));
    }
}
