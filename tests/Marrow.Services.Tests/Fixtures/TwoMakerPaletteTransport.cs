using System.Net;
using System.Text.Json.Nodes;
namespace Marrow.Services.Tests.Fixtures;
public sealed class TwoMakerPaletteTransport(PalettePipelineFixture fake) : HttpMessageHandler
{
    private readonly HttpMessageInvoker inner=new(fake.Http,false);
    private int active,peak;
    public TaskCompletionSource BothStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Peak=>peak;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.RequestUri!.Host=="huggingface.co"&&request.RequestUri.AbsolutePath.StartsWith("/api/",StringComparison.Ordinal))
        {
            var n=Interlocked.Increment(ref active);peak=Math.Max(peak,n);if(n==2)BothStarted.TrySetResult();
            try{await Release.Task.WaitAsync(ct);}finally{Interlocked.Decrement(ref active);}
        }
        var reply=await inner.SendAsync(request,ct);
        if(request.RequestUri.AbsolutePath.EndsWith("/models",StringComparison.Ordinal))
        {
            var data=JsonNode.Parse(await reply.Content.ReadAsStringAsync(ct))!;var beta=data["data"]![0]!.DeepClone();beta["id"]="beta/nova-1";beta["name"]="Beta: Nova 1";beta["hugging_face_id"]="beta/nova-1";data["data"]!.AsArray().Add(beta);
            reply.Content=new StringContent(data.ToJsonString());reply.Content.Headers.ContentType=new("application/json");
        }
        return reply;
    }
    protected override void Dispose(bool disposing){if(disposing){Release.TrySetResult();inner.Dispose();}base.Dispose(disposing);}
}
