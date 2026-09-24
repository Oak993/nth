using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Testing;

// PORT-ONLY test fixture, no Swift twin: the environment MCPStoreTests run MCPStore in (R-TEST-9, R-TEST-10).
// - A scratch Storage.Root (StorageScratchFolder) so mcp.json/custom-mcp.json/the provider configs never touch
//   %LOCALAPPDATA%\Marrow Code.
// - An in-memory Credential Manager backend bound to SecretStore.Shared, so no secret reaches the real store.
// - A TestRuntime binding MainActor to a manual loop, so StartProbe's spawned task never runs until the test
//   drains the loop (RunUntilIdle/RunUntil).
// - The default-browser seam records the address instead of opening a browser (MCPOAuth's own flow).
// MCPProxy.Shared is a real process-wide singleton (MCPProxySingletonFixture's own note): starting it is safe
// (idempotent, loopback only) and every construction here binds a fresh scratch Storage.Root first.
namespace Marrow.Services.Tests.Fixtures;

/// A MainActor executor that runs everything inline, with no queue and no custom SynchronizationContext: for
/// the few tests that need a real stdio probe's process I/O to actually complete (ManualMainLoop's own
/// SynchronizationContext stalls that I/O; this sidesteps it and lets it run exactly as it would with no
/// TestRuntime at all, matching MCPProbeTests.cs's own successful pattern).
public sealed class DirectMainExecutor : IMainExecutor
{
    public bool CheckAccess() => true;
    public void Enqueue(Action work) => work();
}

public sealed class MCPStoreHarness : IDisposable
{
    private readonly StorageScratchFolder folder;
    private readonly IDisposable storeScope;
    private readonly IDisposable browserScope;

    public MCPStoreHarness()
    {
        folder = new StorageScratchFolder();
        Memory = new InMemoryCredentialBackend();
        Backend = new RefusingCredentialBackend(Memory);
        Store = new SecretStore(Backend);
        Runtime = new TestRuntime();
        storeScope = SecretStore.Shared.Override(Store);
        browserScope = Workspace.Open.Override(_ => true);
    }

    public InMemoryCredentialBackend Memory { get; }
    public RefusingCredentialBackend Backend { get; }
    public SecretStore Store { get; }
    public TestRuntime Runtime { get; }
    public string Root => folder.Root;

    public void Dispose()
    {
        browserScope.Dispose();
        storeScope.Dispose();
        folder.Dispose();
    }
}

/// Same scratch Storage.Root and in-memory secret store as <see cref="MCPStoreHarness"/>, but MainActor is bound
/// to <see cref="DirectMainExecutor"/> instead of a TestRuntime, for the handful of tests that need a real
/// stdio probe (ProbeProcessScripts) to actually finish.
public sealed class MCPStoreDirectHarness : IDisposable
{
    private readonly StorageScratchFolder folder;
    private readonly IDisposable storeScope;
    private readonly IDisposable browserScope;
    private readonly IDisposable executorScope;

    public MCPStoreDirectHarness()
    {
        folder = new StorageScratchFolder();
        Memory = new InMemoryCredentialBackend();
        Store = new SecretStore(new RefusingCredentialBackend(Memory));
        storeScope = SecretStore.Shared.Override(Store);
        browserScope = Workspace.Open.Override(_ => true);
        executorScope = MainActor.Executor.Override(new DirectMainExecutor());
    }

    public InMemoryCredentialBackend Memory { get; }
    public SecretStore Store { get; }

    public void Dispose()
    {
        executorScope.Dispose();
        browserScope.Dispose();
        storeScope.Dispose();
        folder.Dispose();
    }
}
