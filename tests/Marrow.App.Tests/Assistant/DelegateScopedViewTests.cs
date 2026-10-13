using Avalonia.Automation;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-open-1")]
public sealed class DelegateScopedViewTests(HeadlessSession headless)
{
    [Fact]
    public void The_scope_picker_writes_only_its_project_or_chat_and_the_model_menu_keeps_the_named_model()
    {
        headless.OnUiThread(() =>
        {
            var project = Guid.NewGuid();
            var chat = Guid.NewGuid();
            var settings = new DextraSettings(new InMemorySettingsStore());
            settings.Update(p => p with { Delegate = DelegateScope.Everywhere });
            var brain = new BrainChoices(() => [ProviderKind.Claude], _ => [], _ => [("chosen", "A long model name العربية")])
                { Project = () => project, Chat = () => chat };
            var rows = new AssistantSettingsPage(settings, new AssistantSettingsParts { Brain = brain }).Rows;
            rows.SetModelScope(DelegateModelScope.Project);
            rows.WithMenu.Select("Claude");
            rows.WithModelMenu.Select("chosen");
            Assert.Equal("chosen", DelegateReach.Model(settings.Value, chat, project)?.Model);
            Assert.Null(settings.Value.DelegateWith);
            rows.SetModelScope(DelegateModelScope.Chat);
            rows.WithMenu.Select("dextra");
            Assert.Null(DelegateReach.Model(settings.Value, chat, project));
            Assert.Equal("chosen", DelegateReach.Model(settings.Value, Guid.NewGuid(), project)?.Model);
            rows.WithMenu.Select("inherit");
            Assert.Empty(settings.Value.DelegateChatModels);
            Assert.Equal("chosen", DelegateReach.Model(settings.Value, chat, project)?.Model);
            Assert.Equal("Answers with scope", AutomationProperties.GetName(rows.WithScopePicker));
            Assert.Equal("Answer model", AutomationProperties.GetName(rows.WithModelMenu));
        });
    }

    [Fact]
    public void A_missing_chat_never_changes_the_global_choice()
    {
        headless.OnUiThread(() =>
        {
            var settings = new DextraSettings(new InMemorySettingsStore());
            var rows = new AssistantSettingsPage(settings).Rows;
            rows.SetModelScope(DelegateModelScope.Chat);
            Assert.False(rows.WithMenu.IsEnabled);
            rows.WithMenu.Select("Claude");
            Assert.Null(settings.Value.DelegateWith);
            Assert.Empty(settings.Value.DelegateChatModels);
        });
    }
}
