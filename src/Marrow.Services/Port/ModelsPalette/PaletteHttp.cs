// PORT-ONLY: captured, cache-less and keyless AC-3 transport, fixed host inventory and <=3 redirects;
// ED-NEW-models-palette-6. Handler injection cannot be lost on an OS event thread.
using System.Globalization;
using System.Net;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed record PaletteHttpReply(int Status,string Type,byte[] Body,Uri Url,DateTimeOffset? Date,int Age,string? RetryAfter);
internal sealed class PaletteHttp : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeProvider time;
    public PaletteHttp(HttpMessageHandler handler,TimeProvider time)
    {client=new(handler,disposeHandler:false){Timeout=Timeout.InfiniteTimeSpan};this.time=time;}
    public async Task<PaletteHttpReply?> Get(Uri original,int cap,int timeout,bool refresh,CancellationToken ct)
    {
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(timeout),time);
            using var cancelled=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);
            var url=original;
            for(var redirects=0;redirects<=3;redirects++)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,url);
                request.Headers.UserAgent.ParseAdd("MarrowColourEngine/AC-3");
                request.Headers.Accept.ParseAdd(original.Host=="openrouter.ai"||original.AbsolutePath.EndsWith("/overview",StringComparison.Ordinal)?"application/json":"image/png,image/*;q=0.5");
                if(refresh)request.Headers.CacheControl=new(){NoCache=true};
                using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancelled.Token);
                var status=(int)response.StatusCode;
                if(status is 301 or 302 or 303 or 307 or 308)
                {
                    if(redirects==3||response.Headers.Location is not {} location)return null;
                    var next=new Uri(url,location);
                    if(next.Scheme!="https"||!AllowedFinal(original,next))return null;url=next;continue;
                }
                if(response.RequestMessage?.RequestUri is {} final&&!AllowedFinal(original,final))return null;
                if(response.Content.Headers.ContentLength>cap)return null;
                using var stream=await response.Content.ReadAsStreamAsync(cancelled.Token);using var bytes=new MemoryStream();
                var buffer=new byte[8192];int read;
                while((read=await stream.ReadAsync(buffer,cancelled.Token))!=0)
                {if(bytes.Length+read>cap)return null;bytes.Write(buffer,0,read);}
                return new(status,response.Content.Headers.ContentType?.MediaType??"",bytes.ToArray(),url,response.Headers.Date,
                    response.Headers.Age is {} age?(int)Math.Clamp(age.TotalSeconds,0,int.MaxValue):0,response.Headers.RetryAfter?.ToString());
            }
            return null;
        }
        catch(HttpRequestException){return null;}catch(OperationCanceledException){return null;}catch(IOException){return null;}
    }
    private static bool AllowedFinal(Uri original,Uri final)=>string.Equals(original.IdnHost,final.IdnHost,StringComparison.OrdinalIgnoreCase)||
        original.IdnHost=="huggingface.co"&&!original.AbsolutePath.EndsWith("/overview",StringComparison.Ordinal)&&final.IdnHost=="cdn-avatars.huggingface.co";
    public void Dispose()=>client.Dispose();
    public static bool ImageMagic(byte[] b,string type)=> type.ToLowerInvariant() switch
    {
        "image/png"=>b.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
        "image/jpeg"=>b.AsSpan().StartsWith(new byte[]{255,216,255}),
        "image/x-icon" or "image/vnd.microsoft.icon"=>b.AsSpan().StartsWith(new byte[]{0,0,1,0}),
        "image/webp"=>b.Length>=12&&b.AsSpan(0,4).SequenceEqual("RIFF"u8)&&b.AsSpan(8,4).SequenceEqual("WEBP"u8),
        "image/gif"=>b.AsSpan().StartsWith("GIF87a"u8)||b.AsSpan().StartsWith("GIF89a"u8),_=>false
    };
}
