using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// Audit fix (svc): DroppyCode/Core/Models/HydraDelegationStream.swift:132 `private(set) var delegations: [HydraDelegation]` is a value array,
// so a snapshot read before Ingest never observes later appends (SOURCE FACT: Swift Array copy-on-write).
public sealed class HydraDelegationStreamSnapshotTests
{
    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:132
    [Fact]
    public void A_delegations_snapshot_taken_before_ingest_stays_empty()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        var saved = parser.Delegations;

        parser.Ingest("```hydra\n[{\"prompt\":\"go\"}]\n```");

        Assert.Single(parser.Delegations);
        Assert.Empty(saved);
    }
}
