using System.Net;
using System.Net.Http.Headers;
namespace Marrow.Services.Tests.Fixtures;
    public sealed class PaletteCaptureFixture(PalettePipelineFixture fake):HttpMessageHandler
    {
        private readonly HttpMessageInvoker inner=new(fake.Http,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if(request.RequestUri!.Host!="t0.gstatic.com")return inner.SendAsync(request,cancellationToken);
            var missing=request.RequestUri.Query.Contains(".invalid",StringComparison.Ordinal);
            var response=new HttpResponseMessage(missing?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=new ByteArrayContent([137,80,78,71,13,10,26,10,missing?(byte)233:(byte)232])};
            response.Content.Headers.ContentType=new MediaTypeHeaderValue("image/png");response.Headers.Date=new DateTimeOffset(2026,10,7,6,20,0,TimeSpan.Zero);return Task.FromResult(response);
        }
    }
