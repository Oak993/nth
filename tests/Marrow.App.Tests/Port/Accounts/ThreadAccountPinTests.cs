using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class ThreadAccountPinTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runtime_reads_the_persisted_account_pin_even_when_sidebar_pinning_differs(bool sidebarPin)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var current = rig.App.Thread(rig.InitialThread.Id)!;
            var json = JsonNode.Parse(JsonSerializer.Serialize(current with { IsPinned = sidebarPin }, Coding.StorageOptions))!;
            json["providerAccountPinID"] = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id.ToString("D");
            var pinned = JsonSerializer.Deserialize<ChatThread>(json.ToJsonString(), Coding.StorageOptions)!;
            rig.App.UpdateThread(current.Id, _ => pinned);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries);
            Assert.True(host.Pinned);
            json.AsObject().Remove("providerAccountPinID");
            var unpinned = JsonSerializer.Deserialize<ChatThread>(json.ToJsonString(), Coding.StorageOptions)!;
            rig.App.UpdateThread(current.Id, _ => unpinned);
            Assert.False(host.Pinned);
            Assert.Equal(sidebarPin, rig.App.Thread(current.Id)!.IsPinned);
        });
    }
}
