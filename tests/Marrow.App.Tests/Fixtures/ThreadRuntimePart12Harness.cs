using Marrow.App;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Creates one thread runtime with every provider executable disabled and API environment keys checked.</summary>
public sealed class ThreadRuntimePart12Harness : IDisposable
{
    public ThreadRuntimePart12Harness(ProviderKind provider = ProviderKind.Codex)
    {
        Store = new AppModelHarness();
        Project = AppModelSamples.Project("Thread runtime", Store.NewFolder("thread-runtime"));
        Thread = AppModelSamples.Thread(Project) with { Provider = provider };
        // R-TEST-9: FileOps.WriteAtomic refuses to create a missing parent folder (ED-121), and AppModelHarness
        // only creates Scratch/Home, not Root, so the first WriteLibrary before any AppModel load needs this.
        Directory.CreateDirectory(Path.GetDirectoryName(Marrow.Services.Store.Storage.LibraryURL)!);
        Store.WriteLibrary([Project], [Thread]);
        AppModelHarness.WriteDocument(Thread.Id);
        App = Store.MakeModel();
        Store.PrepareBootstrap(App);
        Runtime = Store.Main.OnMain(() => new ThreadRuntime(Thread.Id, App));
    }

    public AppModelHarness Store { get; }
    public Project Project { get; }
    public ChatThread Thread { get; }
    public AppModel App { get; }
    public ThreadRuntime Runtime { get; }
    public TestRuntime Main => Store.Main;

    public void Dispose() => Store.Dispose();
}
