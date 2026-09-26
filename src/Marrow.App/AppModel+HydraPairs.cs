using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App;

// Source: DroppyCode/App/AppModel+HydraPairs.swift (B2 1fbf81e), :1-137.
public sealed partial class AppModel
{
    // :15-17
    public ValueList<HydraPair> HydraPickerPairs =>
        new(Settings.HydraPairs.Where(pair => Providers.CountsAsInstalled(pair.Provider) && Settings.IsEnabled(pair.Provider)));

    // :21-23
    public bool LeadsWithHydraPair(HydraPair pair, ChatThread thread) =>
        Settings.HydraEnabled && thread.HydraEnabled && !thread.IsHelper && thread.HydraPairID == pair.Id && thread.Provider == pair.Provider;

    // :29-52
    public void EnterHydraPair(HydraPair pair, Guid threadID)
    {
        if (Thread(threadID) is not { } thread) return;

        var switchesProvider = pair.Provider != thread.Provider;
        var lead = HydraLead(pair, thread);
        UpdateThread(threadID, current => current with
        {
            Provider = pair.Provider,
            ProviderSessionID = switchesProvider ? null : current.ProviderSessionID,
            Model = lead.Model,
            Effort = lead.Effort,
            FastMode = lead.FastMode,
            HydraPairID = pair.Id,
            HydraEnabled = true,
        });

        if (switchesProvider) ExistingRuntime(threadID)?.StopSession(); // :46 existingRuntime(for:)?, never builds one

        Settings.Remember(lead.Model, lead.Effort, pair.Provider);
        Settings.DefaultProvider = pair.Provider;
        Settings.LastHydraPairID = pair.Id;
        Settings.HydraEnabled = true;
        Settings.HydraDefaultEnabled = true;
    }

    // :55-81
    private (string? Model, string? Effort, bool FastMode) HydraLead(HydraPair pair, ChatThread thread)
    {
        var switchesProvider = pair.Provider != thread.Provider;
        var option = pair.OrchestratorModel is { } leadModel
            ? Providers.Model(leadModel, pair.Provider)
            : switchesProvider
                ? Providers.DefaultModel(pair.Provider)
                : Providers.Model(thread.Model, pair.Provider);
        var model = option?.Id ?? pair.OrchestratorModel ?? (switchesProvider ? null : thread.Model);
        var preference = Settings.Preference(pair.Provider, model);

        var effort = pair.OrchestratorEffort is { } pairEffort
            && (option?.Efforts.Contains(pairEffort) ?? true)
                ? pairEffort
                : preference.Effort is { } preferredEffort && option?.Efforts.Contains(preferredEffort) == true
                    ? preferredEffort
                    : null;
        var fastMode = (option?.SupportsFast ?? false) && preference.FastMode;
        return (model, effort, fastMode);
    }

    // :88-106
    public void UpdateHydraPair(Guid id, Func<HydraPair, HydraPair> change)
    {
        var previous = Settings.HydraPair(id);
        if (previous is null) return;

        Settings.UpdateHydraPair(id, change);
        var pair = Settings.HydraPair(id);
        if (pair is null || pair == previous) return;

        foreach (var thread in Threads.Where(thread => thread.HydraPairID == id && Settings.HydraEnabled && thread.HydraEnabled && !thread.IsHelper).ToArray())
        {
            if (thread.Provider != pair.Provider)
            {
                UpdateThread(thread.Id, current => current with { HydraPairID = null });
                continue;
            }

            var lead = HydraLead(pair, thread);
            if (thread.Model != lead.Model || thread.Effort != lead.Effort || thread.FastMode != lead.FastMode)
                UpdateThread(thread.Id, current => current with
                {
                    Model = lead.Model,
                    Effort = lead.Effort,
                    FastMode = lead.FastMode,
                });

            Settings.Remember(lead.Model, lead.Effort, pair.Provider);
        }
    }

    // :110-121
    public void RemoveHydraPair(Guid id)
    {
        Settings.RemoveHydraPair(id);
        if (Settings.LastHydraPairID == id) Settings.LastHydraPairID = null;

        foreach (var thread in Threads.Where(thread => thread.HydraPairID == id).ToArray())
            UpdateThread(thread.Id, current => current with { HydraPairID = null });

        foreach (var project in Projects.Where(project => project.HydraPairID == id).ToArray())
            UpdateProject(project.Id, current => current with { HydraPairID = null });
    }

    // :125-127
    public void SetHydraPair(Guid? pairID, Guid projectID) =>
        UpdateProject(projectID, project => project with { HydraPairID = pairID });

    // :133-136
    public void LeaveHydraPair(Guid threadID)
    {
        UpdateThread(threadID, thread => thread with { HydraPairID = null });
        Settings.LastHydraPairID = null;
    }
}