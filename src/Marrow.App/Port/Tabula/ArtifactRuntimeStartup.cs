// PORT-ONLY: web surfaces W12 and W13, Tabula (DESIGN.md 6.1-6.5; ED-NEW-web-w12-1, ED-NEW-web-w13-4; owner 2026-10-11
// "Visus and Tabula approved"). Program.Main's one Bind line: the process's artifact store lives under the threads folder
// (each thread's data folder holds its artifacts), and a tool call asks the chat's runtime, on the main actor, for the
// running turn and its prompt. From here every chat session gets the nth-ui server (CLI providers) or the native artifact
// tools (API providers), and every answer runs through the fenced fallback. W13 adds the artifacts runtime over the same
// store: the offline libraries beside the app, replies and events on the main actor, and nth.complete on the thread's
// own provider and model in a fresh headless session (one empty folder, plan mode, no tools). The pane's consent cards,
// file sheet and composer draft (IArtifactBridgeHost) and the tool client are bound by Tabula's pane (W14) and the MCP
// tools (W19); until then each refuses.
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Web;

namespace Marrow.App.Port.Tabula;

public static class ArtifactRuntimeStartup
{
    public static void Bind()
    {
        var store = new ArtifactStore(() => Storage.ThreadsDirectory);
        ArtifactHost.Shared.SetProcessDefault(new ArtifactHost(store,
            thread => MainActor.Run(() => Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(thread)?.ArtifactTurn())));
        var model = new SessionArtifactModelClient(Choice, Name, MakeSession);
        ArtifactRuntime.Bind(new ArtifactRuntime(store, new ArtifactLibraryFiles(ArtifactLibraryFiles.DefaultFolder),
            model: new MainActorModelClient(model), post: MainActor.Post));
    }

    /// <summary>The thread's provider and model (its chat model, else the provider's default); null for an unknown thread.</summary>
    private static AdvisorChoice? Choice(Guid thread)
    {
        var app = Marrow.App.Window.WindowManager.Shared.Model;
        if (app.Thread(thread) is not { } chat) return null;
        var model = chat.Model is { Length: > 0 } chosen && chosen != "default" ? chosen : app.Providers.DefaultModel(chat.Provider)?.Id ?? "";
        return new AdvisorChoice(chat.Provider, model);
    }

    private static string Name(AdvisorChoice choice) =>
        Marrow.App.Window.WindowManager.Shared.Model.Providers.Model(choice.Model, choice.Provider)?.Name is { Length: > 0 } name ? name
            : choice.Model.Length > 0 ? choice.Model : choice.Provider.DisplayName;

    private static ProviderSession MakeSession(AdvisorChoice choice)
    {
        var app = Marrow.App.Window.WindowManager.Shared.Model;
        var account = app.Settings.Accounts.Choose(choice.Provider)
            ?? throw new ArtifactBridgeException(ArtifactBridgeErrors.Unavailable, "No enabled " + choice.Provider.DisplayName + " account is available.");
        var keyed = choice.Provider.IsAPIKeyBased || choice.Provider == ProviderKind.Local;
        var executable = keyed ? null : app.Providers.Executable(choice.Provider)
            ?? throw new ProviderError(new ProviderError.Case.NotInstalled(choice.Provider));
        var apiKey = choice.Provider.IsAPIKeyBased ? app.Providers.ApiKey(account.Id) : null;
        if (choice.Provider.IsAPIKeyBased && string.IsNullOrEmpty(apiKey)) throw new ProviderError(new ProviderError.Case.NotInstalled(choice.Provider));
        // Its own empty folder: the call sees no project, no thread and no memory.
        var directory = Path.Combine(Path.GetTempPath(), "nth-artifact-model");
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
        };
        return AccountSessionFactory.Create(app.Providers, configuration, account.Id).Session;
    }

    /// <summary>Sessions live on the main actor.</summary>
    private sealed class MainActorModelClient(IArtifactModelClient inner) : IArtifactModelClient
    {
        public string? ModelName(Guid thread) => MainActor.IsIsolated ? inner.ModelName(thread) : MainActor.Run(() => inner.ModelName(thread)).GetAwaiter().GetResult();

        public Task<ArtifactCompletionResult> CompleteAsync(Guid thread, ArtifactCompletionRequest request, Action<string>? onDelta, CancellationToken cancellationToken) =>
            MainActor.Invoke(() => inner.CompleteAsync(thread, request, onDelta, cancellationToken));
    }
}
