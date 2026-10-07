using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision", "ED-NEW-modelspalette-7")]
public sealed class PaletteSingleFlightTests
{
    [Fact]
    public async Task A_refresh_during_the_automatic_fetch_joins_one_run_and_one_snapshot_result()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var delayed=new DelayedCatalogue(fake.Http);
        using var engine=new PaletteEngine(roots.New(),delayed,fake,fake.Time,fake.Pack);
        var automatic=engine.CheckNow();await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var refresh=engine.RefreshNow();var second=engine.RefreshNow();delayed.Release.TrySetResult();
        var result=await automatic;Assert.Same(result,await refresh);Assert.Same(result,await second);
        Assert.Equal(1,fake.Http.Received.Count(r=>r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal)));
        Assert.Equal(new[]{"acme"},result.NewMakers);
    }
    private sealed class DelayedCatalogue(HttpMessageHandler transport) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker=new(transport,disposeHandler:false);
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/models",StringComparison.Ordinal)){Entered.TrySetResult();await Release.Task.WaitAsync(ct);}
            return await invoker.SendAsync(request,ct);
        }
        protected override void Dispose(bool disposing){if(disposing)invoker.Dispose();base.Dispose(disposing);}
    }
}
