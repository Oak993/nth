using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// Helpers for AppModelTests (App/AppModel.swift). Helper types live here, never inside a test class (R-TEST-3).
// Everything an AppModel touches is bound to a throwaway place first (R-TEST-9, R-TEST-10):
// - UserDefaults.Standard: an in-memory store (TestRuntime's, or a fresh one in headless mode);
// - Storage.Locations: a scratch root and home under MARROW_PORT_DATA_DIR, so library.json, threads/, attachments/
//   and the worktrees root never touch %LOCALAPPDATA%\Marrow Code or the user's profile;
// - SecretStore.Shared: an in-memory Credential Manager, so AppSettings' key read never reaches the real store;
// - Workspace.Open: records nothing and opens nothing.
// The only processes these helpers start are the system git (found as the app finds it, LoginEnvironment.Git) and,
// through bootstrap, git runs the app itself makes; never a real AI CLI (see AppModelHarness.Bootstrap).

/// <summary>An in-memory Credential Manager for the secret store's backend seam (ED-093): target names compare
/// without case, as in Credential Manager; 1168 (ERROR_NOT_FOUND) for a missing target.</summary>
public sealed class AppModelCredentialBackend : ICredentialBackend
{
    private const int ErrorSuccess = 0;
    private const int ErrorNotFound = 1168;
    private readonly Dictionary<string, GenericCredential> credentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    public int Write(GenericCredential credential)
    {
        lock (gate) credentials[credential.TargetName] = credential with { Blob = [.. credential.Blob] };
        return ErrorSuccess;
    }

    public int Read(string targetName, out GenericCredential? credential)
    {
        lock (gate)
        {
            if (credentials.TryGetValue(targetName, out var held))
            {
                credential = held with { Blob = [.. held.Blob] };
                return ErrorSuccess;
            }
        }
        credential = null;
        return ErrorNotFound;
    }

    public int Delete(string targetName)
    {
        lock (gate) return credentials.Remove(targetName) ? ErrorSuccess : ErrorNotFound;
    }
}

/// <summary>
/// The environment one AppModel test runs in. With <c>withRuntime</c> (the default) a <see cref="TestRuntime"/> binds
/// MainActor to a manual loop, SwiftClock to a manual clock (2026-09-22 12:00 UTC) and UserDefaults to an in-memory
/// store; without it (headless tests) only an in-memory UserDefaults is bound and MainActor is the caller's.
/// </summary>
public sealed class AppModelHarness : IDisposable
{
    /// <summary>A real process may be slow on a loaded machine (git, PowerShell): the longest any wait here takes.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(120);

    private readonly List<IDisposable> scopes = [];
    private readonly HashSet<AppModel> paletteModels = [];
    private readonly Lock paletteGate = new();
    private readonly string baseFolder;

    public AppModelHarness(bool withRuntime = true)
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        baseFolder = Path.Combine(dataDirectory, "AppModelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        Root = Path.Combine(baseFolder, "root");
        Home = Path.Combine(baseFolder, "home");
        Scratch = Path.Combine(baseFolder, "scratch");
        Directory.CreateDirectory(Scratch);
        Directory.CreateDirectory(Home);
        // R-TEST-10: Command Code must never consult the owner's HOME or UserProfile.
        var homeBoundary = typeof(LoginEnvironment).GetProperty("HomeForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(homeBoundary);
        scopes.Add(Assert.IsType<Ambient<string>>(homeBoundary.GetValue(null)).Override(Home));
        // R-TEST-9: a Command Code session this harness starts installs its mod and approval folders under the scratch
        // folder, never under the real %LOCALAPPDATA%\nth.
        scopes.Add(Marrow.Services.Providers.CommandCodeSession.LocalApplicationDataForTesting.Override(Path.Combine(baseFolder, "LocalAppData")));
        if (withRuntime)
        {
            Runtime = new TestRuntime();
            Defaults = Runtime.Defaults;
        }
        else
        {
            Defaults = new InMemorySettingsStore();
            scopes.Add(UserDefaults.Standard.Override(Defaults));
        }
        scopes.Add(Storage.Locations.Override(StorageLocations.Create(Root, Home)));
        // R-TEST-9: FileOps.WriteAtomic deliberately refuses to create a missing parent folder (ED-121), and this
        // harness never created Root itself, only Scratch/Home; a WriteLibrary call before any AppModel load (every
        // caller that seeds a thread/project before Bootstrap) silently failed to write library.json (Storage.Write
        // swallows the IOException and only logs it, Storage.cs:415-443), so AppModel loaded with zero threads
        // downstream. Storage.LibraryURL's own directory, not the raw Root field, is what Storage will actually write
        // to: Marrow.Testing.TestDataDirectory.Normalize's '/' -> '\\' replacement and Marrow.Core.Port.SwiftPath's
        // own separator handling can produce two different literal paths for the same logical location on a non-
        // Windows test host, so creating the directory through the exact path Storage targets keeps this correct
        // regardless of that (out-of-unit, unfixed here) mismatch.
        Directory.CreateDirectory(Path.GetDirectoryName(Storage.LibraryURL)!);
        Credentials = new AppModelCredentialBackend();
        scopes.Add(SecretStore.Shared.Override(new SecretStore(Credentials)));
        scopes.Add(Workspace.Open.Override(_ => true));
        scopes.Add(OpenPanel.Runner.Override(new RecordingOpenPanelRunner(null))); // R-TEST-10: a click on Add project must never show the real Windows folder dialog (it blocks the test host); a test that drives the panel overrides this again.
        scopes.Add(AppModel.PaletteLifetimeForTesting.Override(model => { lock (paletteGate) paletteModels.Add(model); }));
    }

    public TestRuntime? Runtime { get; }

    /// <summary>The runtime; fails the test when the harness was made without one.</summary>
    public TestRuntime Main => Runtime ?? throw new InvalidOperationException("This harness has no TestRuntime.");

    public InMemorySettingsStore Defaults { get; }
    public AppModelCredentialBackend Credentials { get; }

    /// <summary>The store root (Storage.Root), where library.json, threads/ and attachments/ live.</summary>
    public string Root { get; }

    /// <summary>The home directory the worktrees root is built from (Storage.swift:38).</summary>
    public string Home { get; }

    /// <summary>A folder for project checkouts and anything else a test needs on disk.</summary>
    public string Scratch { get; }

    /// <summary>A fresh folder under <see cref="Scratch"/>.</summary>
    public string NewFolder(string label)
    {
        var folder = Path.Combine(Scratch, label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Writes library.json as the app writes it (DiskWriter, Storage.swift:358-361), before a model is made.</summary>
    public void WriteLibrary(IEnumerable<Project> projects, IEnumerable<ChatThread> threads) =>
        DiskWriter.Shared.WriteSynchronously(
            new Library() with { Projects = new ValueList<Project>(projects), Threads = new ValueList<ChatThread>(threads) },
            Storage.LibraryURL);

    /// <summary>Writes a thread's document as the app writes it, at threads/&lt;UUID&gt;.json.</summary>
    public static void WriteDocument(Guid threadID) =>
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(threadID), Storage.ThreadURL(threadID));

    /// <summary>
    /// Waits for DocumentPrefetch.shared's background worker to drain. warm([]) queues nothing and hands back the
    /// worker still running, or nil once the queue is empty (Storage.swift:279-305).
    /// </summary>
    public static void WaitForPrefetch()
    {
        var worker = DocumentPrefetch.Shared.Warm([]);
        if (worker is not null && !worker.Completion.Wait(Bound)) throw new TimeoutException("The document prefetch did not drain.");
    }

    public static byte[] LibraryBytes() => File.ReadAllBytes(Storage.LibraryURL);

    /// <summary>The library on disk now, decoded by Storage.loadLibrary.</summary>
    public static Library LoadedLibrary() => Storage.LoadLibrary() switch
    {
        LibraryLoadResult.Loaded loaded => loaded.Library,
        var other => throw new InvalidOperationException("library.json did not load: " + other),
    };

    /// <summary>AppModel() on the main loop (AppModel.swift:119-146).</summary>
    public AppModel MakeModel() => Main.OnMain(() => new AppModel());

    /// <summary>
    /// Runs AppModel.bootstrap() (AppModel.swift:237-278) to its end on the main loop, made safe first (R-TEST-10):
    /// every CLI provider gets a custom binary path that does not exist, so ProviderRegistry.executable(for:)
    /// (ProviderRegistry.swift:219-226) answers nil without a fallback search and refresh(_:) starts no process
    /// (:358-373); and the API-key providers must hold no key, or refreshAll() would validate it over the network
    /// (:376-420). A key they could fall back to from the environment (AppSettings.swift:904-918) fails the test here,
    /// before bootstrap, rather than reaching the internet.
    /// </summary>
    public void Bootstrap(AppModel model)
    {
        PrepareBootstrap(model);
        Main.Loop.Run(() => model.Bootstrap(), Bound);
    }

    public void PrepareBootstrap(AppModel model)
    {
        if (!LoginEnvironment.Load().Wait(Bound)) throw new TimeoutException("LoginEnvironment.load() did not finish.");
        var environment = LoginEnvironment.Current;
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
        {
            if (!kind.IsAPIKeyBased || kind.ApiKeyEnvVar is not { } variable) continue;
            if (environment.TryGetValue(variable, out var value) && value.Length > 0)
                throw new InvalidOperationException(
                    "The environment holds " + variable + ": bootstrap would validate it over the network (R-TEST-10). Run the tests without it.");
        }
        var missing = Path.Combine(Scratch, "no-such-cli", "missing.exe");
        Main.OnMain(() =>
        {
            foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
                if (!kind.IsAPIKeyBased) model.Settings.SetBinaryPath(missing, kind);
        });
    }

    public void Dispose()
    {
        try
        {
            // Stop timers and their actor posts while this fixture's dispatcher,
            // credentials and directories still exist. Hidden views do not own an
            // AppModel's palette lifetime, and detaching them is insufficient.
            StopOwnedPalettes();
            FinishDiskWrites();
            try
            {
                FinishFileIndexBuilds();
                WaitForPrefetch();
            }
            finally { FinishDiskWrites(); }
        }
        finally
        {
            // FinishFileIndexBuilds may pump an already-queued actor continuation.
            // Collect any palette it creates before removing the ownership scope.
            StopOwnedPalettes();
            for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
            Runtime?.Dispose();
            try { Directory.Delete(baseFolder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void StopOwnedPalettes()
    {
        AppModel[] owned;
        lock (paletteGate) { owned = [.. paletteModels]; paletteModels.Clear(); }
        foreach (var model in owned) model.StopPaletteChecks();
    }

    // R-TEST-5: a queued write captures this fixture's manual clock. Keep that clock and
    // its directory alive through the actual shared writer fence, including any retry.
    // The fence runs off-thread; clock advancement and all actor cleanup remain here.
    private void FinishDiskWrites()
    {
        var barrier = Task.Factory.StartNew(DiskWriter.Shared.WaitForQueuedWrites, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var poll = TimeSpan.FromMilliseconds(5);
        while (!barrier.IsCompleted)
        {
            var remaining = Bound - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("The harness disk writer did not drain before its clock and directory were retired.");
            if (barrier.Wait(remaining < poll ? remaining : poll)) break;
            // Do not call TestRuntime.Advance: it also pumps actor callbacks, which can
            // themselves block behind this fence. Only the owning clock is advanced.
            if (Runtime is { } runtime && runtime.Time.ActiveTimers > 0)
                runtime.Time.Advance(FileOps.RetryDelays[0]);
        }
        barrier.GetAwaiter().GetResult();
    }

    private static readonly FieldInfo FileIndexBuilding =
        typeof(FileIndex).GetField("Building", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(nameof(FileIndex), "Building");

    /// <summary>
    /// R-TEST-5. FileIndex's building map is a static shared by every composer (ComposerView.swift:1125-1126), and a
    /// build leaves it only when its task, spawned on the main actor, runs to its end there (:1146-1152). A composer
    /// mounted in this harness whose build of one of its folders is still under way when the test ends would never
    /// finish it: this manual loop is never pumped again, and the headless session makes a new dispatcher for every
    /// test (AvaloniaTestIsolationLevel.PerTest). Every later composer in that folder would join the dead build
    /// (:1140-1141), and with two left, a composer in any new folder waits on one of them (:1142-1144). So the builds
    /// of this harness's folders are run to their end here, on the loop (and, on the headless UI thread, the dispatcher)
    /// they were spawned on, before the folders are deleted.
    /// </summary>
    private void FinishFileIndexBuilds()
    {
        var building = (IDictionary)FileIndexBuilding.GetValue(null)!;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseFolder)) + Path.DirectorySeparatorChar;
        var pending = new List<Task>();
        // The map is main-actor state; a harness of a test class running in parallel reads it here while a headless
        // test's composer may be writing it, so a read that meets a write is taken again.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                pending.Clear();
                foreach (DictionaryEntry entry in building)
                {
                    if (entry.Key is string directory && entry.Value is TaskHandle { Completion.IsCompleted: false } build
                        && Path.GetFullPath(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        pending.Add(build.Completion);
                }
                break;
            }
            catch (InvalidOperationException) when (attempt < 100) { }
        }
        if (pending.Count == 0) return;
        var finished = Task.WhenAll(pending);
        var onUiThread = HeadlessSession.IsInBody;
        var loop = Runtime?.Loop ?? MainActor.Executor.Current as ManualMainLoop;
        if (loop is not null)
        {
            loop.RunUntil(() =>
            {
                if (onUiThread) Dispatcher.UIThread.RunJobs();
                return finished.IsCompleted;
            }, Bound);
        }
        else if (onUiThread)
        {
            HeadlessSession.RunDispatcherUntil(finished, Bound);
        }
        else if (!finished.Wait(Bound))
        {
            throw new TimeoutException("A FileIndex build of this harness's folders did not finish.");
        }
    }
}

/// <summary>Sample library values, trimmed to what AppModel reads (Core/Models/Library.swift:57-70, :121-226).</summary>
public static class AppModelSamples
{
    public static Project Project(string name, string path) => new(name, path);

    /// <summary>A thread as ChatThread.init makes it (Library.swift:204-226), on codex, supervised.</summary>
    public static ChatThread Thread(Project project) =>
        new(project.Id, ProviderKind.Codex, model: null, effort: null, runtimeMode: RuntimeMode.Supervised);

    /// <summary>A Hydra head's record (Core/Models/Hydra.swift:320-389), droppy-run, delegated.</summary>
    public static HydraHeadInfo Head(HydraHeadInfo.Status status, string? baseTree = null) =>
        new HydraHeadInfo(1, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
        {
            HydraHeadInfoStatus = status,
            BaseTree = baseTree,
        };
}

/// <summary>
/// A throwaway git checkout for the worktree and checkpoint tests: a local identity, no signing, no line-ending
/// conversion, and .git left unhidden (Git for Windows' core.hideDotFiles default). Git runs WITHOUT the app's prefix, so
/// what it reads back is git's own answer, independent of the code under test.
/// </summary>
public sealed class AppModelGitRepo
{
    private AppModelGitRepo(string root) => Root = root;

    public string Root { get; }

    /// <summary>An existing checkout (or linked worktree) at <paramref name="root"/>, for reading git's answers there.</summary>
    public static AppModelGitRepo At(string root) => new(root);

    /// <summary>An initialised checkout on branch main in <paramref name="folder"/>, with no commit.</summary>
    public static AppModelGitRepo Empty(string folder)
    {
        var repository = new AppModelGitRepo(folder);
        repository.Git("init", "-q", "-b", "main");
        repository.Git("config", "user.name", "Port Test");
        repository.Git("config", "user.email", "port-test@localhost");
        repository.Git("config", "commit.gpgsign", "false");
        repository.Git("config", "core.autocrlf", "false");
        repository.Git("config", "core.hideDotFiles", "false");
        return repository;
    }

    /// <summary>A checkout on main holding a.txt in one commit.</summary>
    public static AppModelGitRepo WithCommit(string folder)
    {
        var repository = Empty(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.txt"), Encoding.UTF8.GetBytes("a\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "first");
        return repository;
    }

    /// <summary>The checkpoint ref prefix of a thread, as Git.swift:651 builds it.</summary>
    public static string CheckpointPrefix(Guid thread) =>
        "refs/droppy-code/checkpoints/" + SwiftString.Lowercased(SwiftUUID.UuidString(thread)) + "/";

    /// <summary>Makes one checkpoint ref of <paramref name="thread"/> pointing at HEAD (Git.swift:409-411 shape).</summary>
    public void AddCheckpoint(Guid thread) => Git("update-ref", CheckpointPrefix(thread) + "1-before", "HEAD");

    /// <summary>The checkpoint refs of <paramref name="thread"/> that exist now.</summary>
    public IReadOnlyList<string> Checkpoints(Guid thread) =>
        SwiftString.Split(Git("for-each-ref", "--format=%(refname)", CheckpointPrefix(thread)), "\n").ToList();

    /// <summary>Adds a linked worktree at <paramref name="path"/> on a new branch.</summary>
    public void AddWorktree(string path, string branch) => Git("worktree", "add", "-q", "-b", branch, path);

    public string Git(params string[] arguments)
    {
        var result = Raw(arguments);
        if (!result.Succeeded)
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + result.FailureMessage);
        return result.Output;
    }

    public ShellResult Raw(params string[] arguments)
    {
        var run = Shell.Run(LoginEnvironment.Git, arguments, @in: Root, environment: LoginEnvironment.Current);
        if (!run.Wait(AppModelHarness.Bound)) throw new TimeoutException("git " + string.Join(' ', arguments) + " did not finish.");
        return run.Result;
    }
}
