using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// PORT-ONLY: OD-0065/0066 prerequisite; ten isolated caches, fake API, no palette proof claimed.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-modelspalette-2")]
public sealed class PublicModelCatalogueStoreTests
{
    private const string First = """{"data":[{"id":"maker/one","created":1,"pricing":{"prompt":"0.01"}}]}""";
    private const string Current = """{"data":[{"id":"maker/two","created":2},{"id":"maker/one","created":1,"pricing":{"prompt":"0.01"}}]}""";

    [Fact]
    public async Task Ten_independent_roots_with_different_histories_and_a_failed_source_converge_and_restart_identically()
    {
        using var runtime = new TestRuntime();
        using var roots = new ModelsPaletteRoots();
        using var handler = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(handler);
        var expected = PublicModelCatalogue.Parse(Current)!;
        for (var i = 0; i < 10; i++)
        {
            var root = roots.New();
            using (var store = new PublicModelCatalogueStore(root))
            {
                Assert.Null(store.Current);
                if (i % 2 == 0)
                {
                    handler.Answer = _ => InProcessHttpHandler.Reply(200, First);
                    Assert.True(await store.Refresh());
                }
                var previous = store.Current;
                handler.Answer = _ => InProcessHttpHandler.Reply(503, "{}");
                Assert.False(await store.Refresh());
                Assert.Same(previous, store.Current);
                handler.Answer = _ => InProcessHttpHandler.Reply(200, Current);
                Assert.True(await store.Refresh());
                Assert.Equal(expected, store.Current);
            }
            using var restarted = new PublicModelCatalogueStore(root);
            Assert.Equal(expected, restarted.Current);
        }
    }

    [Fact]
    public async Task Failed_or_malformed_refreshes_leave_the_last_good_cache_byte_exact()
    {
        using var runtime = new TestRuntime();
        using var roots = new ModelsPaletteRoots();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, First) };
        using var transport = URLSession.Transport.Override(handler);
        using var store = new PublicModelCatalogueStore(roots.New());
        Assert.True(await store.Refresh());
        var bytes = File.ReadAllBytes(store.CachePath);
        var snapshot = store.Current;
        handler.Answer = _ => InProcessHttpHandler.Reply(200, "{\"data\":[]}");
        Assert.False(await store.Refresh());
        Assert.Same(snapshot, store.Current);
        Assert.Equal(bytes, File.ReadAllBytes(store.CachePath));
        handler.Answer = _ => throw new HttpRequestException("fixture source unavailable");
        await Assert.ThrowsAnyAsync<Exception>(() => store.Refresh());
        Assert.Same(snapshot, store.Current);
        Assert.Equal(bytes, File.ReadAllBytes(store.CachePath));
    }

    [Fact]
    public async Task A_corrupted_cache_is_researched_again_without_overwriting_it_during_loading()
    {
        using var runtime = new TestRuntime();
        using var roots = new ModelsPaletteRoots();
        var root = roots.New();
        string path;
        using (var store = new PublicModelCatalogueStore(root)) path = store.CachePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string corrupt = "{\"version\":999,\"data\":[{\"id\":\"maker/one\"}]}";
        File.WriteAllText(path, corrupt);
        using var restarted = new PublicModelCatalogueStore(root);
        Assert.Null(restarted.Current);
        Assert.Equal(corrupt, File.ReadAllText(path));
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, Current) };
        using var transport = URLSession.Transport.Override(handler);
        Assert.True(await restarted.Refresh());
        Assert.Equal(PublicModelCatalogue.Parse(Current), restarted.Current);
    }

}
