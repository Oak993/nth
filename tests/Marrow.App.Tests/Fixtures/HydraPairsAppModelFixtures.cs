using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A throwaway AppModel environment scoped to the HydraPairs tests.</summary>
public sealed class HydraPairsAppModelHarness : IDisposable
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(120);

    private readonly List<IDisposable> scopes = [];
    private readonly string baseFolder;

    public HydraPairsAppModelHarness()
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests.");

        baseFolder = Path.Combine(dataDirectory, "AppModelHydraPairsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        Root = Path.Combine(baseFolder, "root");
        Home = Path.Combine(baseFolder, "home");
        Scratch = Path.Combine(baseFolder, "scratch");
        Directory.CreateDirectory(Scratch);

        Main = new TestRuntime();
        Defaults = Main.Defaults;
        scopes.Add(Storage.Locations.Override(StorageLocations.Create(Root, Home)));
        scopes.Add(SecretStore.Shared.Override(new SecretStore(
            new AppSettingsCredentialBackend(() => Main.Loop.CheckAccess()))));
        scopes.Add(Workspace.Open.Override(_ => true));
    }

    public TestRuntime Main { get; }

    public InMemorySettingsStore Defaults { get; }

    public string Root { get; }

    public string Home { get; }

    public string Scratch { get; }

    public string NewFolder(string label)
    {
        var folder = Path.Combine(Scratch, label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void WriteLibrary(IEnumerable<Project> projects, IEnumerable<ChatThread> threads) =>
        DiskWriter.Shared.WriteSynchronously(
            new Library() with
            {
                Projects = new ValueList<Project>(projects),
                Threads = new ValueList<ChatThread>(threads),
            },
            Storage.LibraryURL);

    public AppModel MakeModel() => Main.OnMain(() => new AppModel());

    public void Dispose()
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
            scopes[index].Dispose();
        Main.Dispose();
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The minimal project and thread values used by the HydraPairs tests.</summary>
public static class HydraPairsAppModelSamples
{
    public static Project Project(string name, string path) => new(name, path);

    public static ChatThread Thread(Project project) =>
        new(project.Id, ProviderKind.Codex, model: null, effort: null, runtimeMode: RuntimeMode.Supervised);
}

/// <summary>A provider-session fake that records whether the old session was stopped.</summary>
internal sealed class HydraPairsStopTrackingProviderSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; }
    public bool IsRunning => true;
    public int StopCount { get; private set; }
    public Task<string> Start() => Task.FromResult("test-session");
    public Task Send(TurnInput input) => Task.CompletedTask;
    public Task Interrupt() => Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) { }
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }
    public Task Compact() => Task.CompletedTask;
    public void Stop() => StopCount++;
}
