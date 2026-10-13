using System.Text.Json;
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// DESIGN.md 4.3: a model choice is kept globally, per project and per chat.
[Trait("Decision", "ED-NEW-dextra-open-1")]
public sealed class DelegateScopedChoiceTests
{
    [Fact]
    public void Old_choices_preserve_unknown_fields_and_newer_settings_are_not_rewritten()
    {
        var store = new InMemorySettingsStore();
        store.Set(DextraSettings.SettingsKey, "{\"on\":true,\"futureField\":{\"keep\":7}}");
        var settings = new DextraSettings(store);
        settings.SetDelegateModel(null, Guid.NewGuid(), new DelegateWho(ProviderKind.Claude, "chosen"));
        using var saved = JsonDocument.Parse((string)store.Object(DextraSettings.SettingsKey)!);
        Assert.Equal(7, saved.RootElement.GetProperty("futureField").GetProperty("keep").GetInt32());
        Assert.Equal(1, saved.RootElement.GetProperty("version").GetInt32());
        const string newer = "{\"version\":2,\"on\":true,\"firstRunDone\":true,\"futureField\":9}";
        store.Set(DextraSettings.SettingsKey, newer);
        Assert.False(settings.Value.On);
        settings.SetDelegateModel(null, null, new DelegateWho(ProviderKind.Claude));
        Assert.Equal(newer, store.Object(DextraSettings.SettingsKey));
    }

    [Fact]
    public void Chat_beats_project_beats_global_and_explicit_Dextra_does_not_inherit()
    {
        var settings = new DextraSettings(new InMemorySettingsStore());
        var project = Guid.NewGuid();
        var chat = Guid.NewGuid();
        settings.SetDelegateModel(null, null, new DelegateWho(ProviderKind.Claude, "global"));
        settings.SetDelegateModel(project, null, new DelegateWho(ProviderKind.Codex, "project"));
        Assert.Equal("project", DelegateReach.Model(settings.Value, chat, project)?.Model);
        Assert.Equal("global", DelegateReach.Model(settings.Value, chat, Guid.NewGuid())?.Model);
        settings.SetDelegateModel(null, chat, null);
        Assert.Null(DelegateReach.Model(settings.Value, chat, project));
        settings.SetDelegateModel(null, chat, null, inherit: true);
        Assert.Equal("project", DelegateReach.Model(settings.Value, chat, project)?.Model);
    }

    [Fact]
    public void The_controller_uses_the_lead_chats_scope_when_a_head_asks()
    {
        using var rig = new DelegateRig();
        rig.Everywhere();
        var lead = rig.Owner();
        var head = rig.Owner(parent: lead.Id);
        var settings = new DextraSettings(new InMemorySettingsStore());
        settings.Value = rig.Preferences;
        settings.SetDelegateModel(lead.ProjectID, null, new DelegateWho(ProviderKind.Claude, "project"));
        settings.SetDelegateModel(null, lead.Id, new DelegateWho(ProviderKind.Claude, "chat"));
        rig.Preferences = settings.Value;
        Assert.True(rig.Ask(head.Id, "Which database does the repo use?", ["Postgres", "SQLite"]));
        Assert.Equal("chat", rig.Model.LastWho?.Model);
    }

    [Fact]
    public void Project_and_chat_model_choices_survive_settings_round_trip()
    {
        const string json = """
            {"delegate":"everywhere","delegateWith":{"provider":"Claude","model":"global-model"},
             "delegateProjectModels":[{"id":"aaaaaaaa-0000-0000-0000-000000000001","who":{"provider":"Codex","model":"project-model"}}],
             "delegateChatModels":[{"id":"bbbbbbbb-0000-0000-0000-000000000002","who":null}]}
            """;
        using var stored = JsonDocument.Parse(DextraSettings.ToJson(DextraSettings.FromJson(json)));
        Assert.True(stored.RootElement.TryGetProperty("delegateProjectModels", out var projects));
        Assert.Equal("project-model", projects[0].GetProperty("who").GetProperty("model").GetString());
        Assert.True(stored.RootElement.TryGetProperty("delegateChatModels", out var chats));
        Assert.Equal(JsonValueKind.Null, chats[0].GetProperty("who").ValueKind);
    }
}
