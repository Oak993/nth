using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, OD-0145, ED-NEW-pcctl-25; DESIGN.md 4.1, 4.7, 10.4). The grants store: "Always"
// grants in nth's settings under pcControl.grants, "This chat" grants in memory per chat; Use implies See; Settings lists,
// changes and removes them; a malformed entry is skipped.
[Trait("Decision", "ED-NEW-pcctl-25")]
public sealed class PcGrantsTests
{
    private static readonly PcAppIdentity Outlook = new("C:\\Office\\OUTLOOK.EXE");
    private static readonly PcAppIdentity Excel = new("c:\\office\\excel.exe");

    [Fact]
    public void Always_is_saved_in_settings_and_Use_implies_See()
    {
        var store = new InMemorySettingsStore();
        var grants = new PcGrants(store);
        var changes = 0;
        grants.Changed += () => changes++;

        grants.Grant("chat-1", Outlook, "Outlook", use: true, PcGrantScope.Always);

        Assert.Equal(1, changes);
        Assert.Equal(PcApproval.Always, grants.Standing(Outlook, PcCapability.See));
        Assert.Equal(PcApproval.Always, grants.Standing(Outlook, PcCapability.Act));
        Assert.Null(grants.Standing(Excel, PcCapability.See));
        var saved = Assert.IsAssignableFrom<IReadOnlyList<object>>(store.Object(PcGrants.Key));
        var entry = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(Assert.Single(saved));
        Assert.Equal(Outlook.Key, entry["app"]);
        Assert.Equal("Outlook", entry["name"]);
        Assert.Equal("C:\\Office\\OUTLOOK.EXE", entry["path"]);
        Assert.Equal(true, entry["use"]);

        // a new store over the same settings reads it back
        var again = new PcGrants(store);
        Assert.Equal(new[] { new PcGrant(Outlook.Key, "Outlook", "C:\\Office\\OUTLOOK.EXE", true, PcGrantScope.Always) }, again.Always.ToArray());
    }

    [Fact]
    public void See_only_covers_See_and_not_Use()
    {
        var grants = new PcGrants(new InMemorySettingsStore());

        grants.Grant("chat-1", Excel, "Excel", use: false, PcGrantScope.Always);

        Assert.Equal(PcApproval.Always, grants.Standing(Excel, PcCapability.See));
        Assert.Null(grants.Standing(Excel, PcCapability.Act));
        // a later grant with Use widens it; one without Use never narrows it
        grants.Grant("chat-1", Excel, "Excel", use: true, PcGrantScope.Always);
        Assert.Equal(PcApproval.Always, grants.Standing(Excel, PcCapability.Act));
        grants.Grant("chat-2", Excel, "Excel", use: false, PcGrantScope.Always);
        Assert.Equal(PcApproval.Always, grants.Standing(Excel, PcCapability.Act));
        Assert.Single(grants.Always);
    }

    [Fact]
    public void A_chat_grant_is_in_memory_for_that_chat_only()
    {
        var store = new InMemorySettingsStore();
        var grants = new PcGrants(store);

        grants.Grant("chat-1", Outlook, "Outlook", use: false, PcGrantScope.Chat);

        Assert.Null(store.Object(PcGrants.Key));
        Assert.Equal(PcApproval.Chat, grants.ForChat("chat-1", Outlook, PcCapability.See));
        Assert.Null(grants.ForChat("chat-1", Outlook, PcCapability.Act));
        Assert.Null(grants.ForChat("chat-2", Outlook, PcCapability.See));
        Assert.Null(grants.Standing(Outlook, PcCapability.See));
        Assert.Equal(PcGrantScope.Chat, Assert.Single(grants.All).Scope);

        // Always replaces the chat's own grant, so the list shows the app once
        grants.Grant("chat-1", Outlook, "Outlook", use: true, PcGrantScope.Always);
        var only = Assert.Single(grants.All);
        Assert.Equal(PcGrantScope.Always, only.Scope);
        Assert.Null(grants.ForChat("chat-1", Outlook, PcCapability.See));

        grants.Grant("chat-2", Excel, "Excel", use: true, PcGrantScope.Chat);
        grants.EndChat("chat-2");
        Assert.Null(grants.ForChat("chat-2", Excel, PcCapability.Act));
    }

    [Fact]
    public void Settings_changes_Use_and_removes_a_grant()
    {
        var store = new InMemorySettingsStore();
        var grants = new PcGrants(store);
        grants.Grant("chat-1", Outlook, "Outlook", use: true, PcGrantScope.Always);
        grants.Grant("chat-1", Excel, "Excel", use: true, PcGrantScope.Always);
        grants.Grant("chat-3", Excel, "Excel", use: false, PcGrantScope.Chat);

        Assert.True(grants.SetUse(Outlook.Key, false));
        Assert.False(grants.SetUse(Outlook.Key, false));
        Assert.Null(grants.Standing(Outlook, PcCapability.Act));
        Assert.Equal(PcApproval.Always, grants.Standing(Outlook, PcCapability.See));

        Assert.True(grants.Remove(Excel.Key));
        Assert.Null(grants.Standing(Excel, PcCapability.See));
        Assert.Null(grants.ForChat("chat-3", Excel, PcCapability.See));
        Assert.False(grants.Remove(Excel.Key));
        Assert.True(grants.Remove(Outlook.Key));
        // the last one gone removes the key
        Assert.Null(store.Object(PcGrants.Key));
    }

    [Fact]
    public void A_malformed_entry_is_skipped()
    {
        var store = new InMemorySettingsStore();
        store.Set(PcGrants.Key, new List<object>
        {
            "not a grant",
            new Dictionary<string, object> { ["name"] = "No key" },
            new Dictionary<string, object> { ["app"] = Outlook.Key, ["use"] = true },
            new Dictionary<string, object> { ["app"] = Outlook.Key, ["use"] = false },
        });
        var grants = new PcGrants(store);

        var grant = Assert.Single(grants.Always);
        Assert.Equal(Outlook.Key, grant.Key);
        Assert.Equal(Outlook.Key, grant.Name);
        Assert.True(grant.Use);
    }
}
