// PORT-ONLY: DESIGN.md 2.2, 9.1, 9.3; ED-NEW-dextra-open-5.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class AppAssistantHomes(AppModel model, AssistantStore store) : IAssistantHomes
{
    private Marrow.Core.Models.Project? Project() =>
        (model.SelectedThreadID is { } selected && model.Thread(selected) is { } chat ? model.Project(chat.ProjectID) : null) ?? model.Projects.FirstOrDefault();

    public MissionProject? ProjectOf(AssistantProfile profile) =>
        Project() is { } project ? new MissionProject(project.Id, profile.Name, Path.Combine(store.Root, "assistants", profile.Id, "folder")) : null;

    public Guid? OpenHome(AssistantProfile profile)
    {
        if (profile.Retired || !Guid.TryParse(profile.Id, out var assistant)) return null;
        if (model.Threads.FirstOrDefault(t => t.AssistantID == assistant && t.MissionID is null && t.ParentThreadID is null) is { } found) return found.Id;
        if (Project() is not { } project) return null;
        var provider = profile.Model is { } want && Marrow.Core.Port.SwiftEnum<ProviderKind>.FromRawValue(want.ProviderId) is { } named
            ? named : model.AssistantHost.BrainProvider() ?? ProviderKind.Claude;
        var thread = new ChatThread(project.Id, provider, profile.Model?.Model ?? model.AssistantHost.BrainModel(), null, SealedProviderConfig.CappedMode)
        {
            Title = profile.Name, HasCustomTitle = true, AssistantID = assistant,
            ProviderAccountID = profile.Model?.Account ?? model.AssistantHost.Settings.Value.BrainAccount,
            WorktreePath = Path.Combine(store.Root, "assistants", profile.Id, "folder"),
        };
        model.InsertThread(thread);
        return thread.Id;
    }
}
