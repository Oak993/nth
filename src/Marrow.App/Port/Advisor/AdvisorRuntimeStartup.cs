// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2; owner 2026-10-11: "it should go inside Hydra,
// because Hydra runs the agents and delegates the work"). Program.Main's one Bind line: a Hydra head that nth runs as its
// own chat (a Droppy head) is advised by its lead's model, on whatever provider the lead runs; every other chat has no
// advisor. The advice runs as one headless turn of a provider session made the way a chat's is (the provider's first
// enabled account, its login or API key), on the main actor where sessions live. Each answer leaves a one-line note in
// the lead's transcript.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Services.Port.Advisor;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.App.Runtime;

namespace Marrow.App.Port.Advisor;

public static class AdvisorRuntimeStartup
{
    public static void Bind()
    {
        AdvisorSeams.Choice = (thread, _, _) => thread is { } id ? LeadOf(Marrow.App.Window.WindowManager.Shared.Model, id) : null;
        AdvisorHost.Runner.SetProcessDefault(() => new MainActorRunner(new SessionAdvisorRunner(MakeSession)));
        AdvisorHost.Noted.SetProcessDefault((thread, advice) => MainActor.Post(() => NoteInLead(Marrow.App.Window.WindowManager.Shared.Model, thread, advice)));
    }

    /// <summary>The advisor of a Droppy head: its lead's provider and model (the lead's chat model, else its pair's, else the provider's default); null for any other chat.</summary>
    public static AdvisorChoice? LeadOf(AppModel app, Guid head)
    {
        if (app.Thread(head) is not { Hydra.HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy, ParentThreadID: { } leadID }
            || app.Thread(leadID) is not { } lead) return null;
        var model = lead.Model is { Length: > 0 } chosen && chosen != "default" ? chosen
            : app.HydraPair(lead)?.OrchestratorModel ?? app.Providers.DefaultModel(lead.Provider)?.Id ?? "";
        return new AdvisorChoice(lead.Provider, model);
    }

    private static void NoteInLead(AppModel app, Guid? thread, AdvisorAdvice advice)
    {
        if (thread is not { } id || app.Thread(id) is not { Hydra: { } info, ParentThreadID: { } leadID }) return;
        var by = advice.By.Model.Length > 0 ? advice.By.Model : advice.By.Provider.DisplayName;
        app.Runtime(leadID).AppendHydraNote($"Hydra: {info.DisplayName} asked {by} for advice.");
    }

    private static ProviderSession MakeSession(AdvisorChoice choice)
    {
        var app = Marrow.App.Window.WindowManager.Shared.Model;
        var account = app.Settings.Accounts.Choose(choice.Provider)
            ?? throw new InvalidOperationException("No enabled " + choice.Provider.DisplayName + " account is available for the advisor.");
        var keyed = choice.Provider.IsAPIKeyBased || choice.Provider == ProviderKind.Local;
        var executable = keyed ? null : app.Providers.Executable(choice.Provider)
            ?? throw new ProviderError(new ProviderError.Case.NotInstalled(choice.Provider));
        var apiKey = choice.Provider.IsAPIKeyBased ? app.Providers.ApiKey(account.Id) : null;
        if (choice.Provider.IsAPIKeyBased && string.IsNullOrEmpty(apiKey)) throw new ProviderError(new ProviderError.Case.NotInstalled(choice.Provider));
        var directory = Path.Combine(Path.GetTempPath(), "nth-advisor");
        Directory.CreateDirectory(directory);
        var configuration = new SessionConfiguration
        {
            Provider = choice.Provider,
            Executable = executable,
            WorkingDirectory = ThreadRuntime.SessionDirectoryUrl(directory),
            AccountId = account.Id,
            Environment = app.Providers.Environment(account.Id),
            Model = choice.Model.Length == 0 ? null : choice.Model,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Plan,
            ApiKey = apiKey,
            AntiSlopEnabled = false,
            // No IgnisThreadID: the advisor's own session is no chat's, so it is never offered ask_advisor itself.
        };
        return AccountSessionFactory.Create(app.Providers, configuration, account.Id).Session;
    }

    private sealed class MainActorRunner(IAdvisorRunner inner) : IAdvisorRunner
    {
        public Task<AdvisorRun> Run(AdvisorChoice choice, string prompt, CancellationToken cancellationToken) =>
            MainActor.Invoke(() => inner.Run(choice, prompt, cancellationToken));
    }
}
