using System.Text.Json;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.3, ED-NEW-web-w8c-1. Runs only in isolated frame worlds; never evaluates a query as code.
public sealed class WebFindFallback(IWebEngineView page)
{
    private readonly List<(int Context, int Count)> frames = [];
    private readonly SemaphoreSlim serial = new(1);
    private int count;
    private int active = -1;

    public async Task<WebFindResult> StartAsync(string query, bool matchCase, CancellationToken cancellationToken)
    {
        await serial.WaitAsync(cancellationToken);
        try
        {
            await ClearAsync(cancellationToken);
            if (string.IsNullOrEmpty(query)) return new(0, -1);
            using var tree = JsonDocument.Parse(await page.CallCdpAsync("Page.getFrameTree", "{}", null, cancellationToken));
            foreach (var id in FrameIds(tree.RootElement.GetProperty("frameTree")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var world = JsonDocument.Parse(await page.CallCdpAsync("Page.createIsolatedWorld",
                    JsonSerializer.Serialize(new { frameId = id, worldName = "nth-visus-find" }), null, CancellationToken.None));
                var context = world.RootElement.GetProperty("executionContextId").GetInt32();
                frames.Add((context, 0)); // Even a partial or cancelled evaluation may have created highlights.
                var result = await EvaluateAsync(context, SearchScript.Replace("__OPTIONS__", JsonSerializer.Serialize(new { query, matchCase }), StringComparison.Ordinal), cancellationToken);
                var matches = result.GetProperty("count").GetInt32();
                if (matches < 0) throw new InvalidOperationException("Invalid find result.");
                frames[^1] = (context, matches);
                count = checked(count + matches);
            }
            active = count == 0 ? -1 : 0;
            await SelectAsync(cancellationToken);
            return new(count, active);
        }
        catch
        {
            // Await cleanup before returning an error; retain failed worlds for Stop's retry.
            try { await ClearAsync(CancellationToken.None); } catch (Exception) { }
            throw;
        }
        finally { serial.Release(); }
    }

    public async Task<WebFindResult> MoveAsync(bool previous, CancellationToken cancellationToken)
    {
        await serial.WaitAsync(cancellationToken);
        try
        {
            if (count > 0) active = (active + (previous ? count - 1 : 1)) % count;
            await SelectAsync(cancellationToken);
            return new(count, active);
        }
        finally { serial.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await serial.WaitAsync(cancellationToken);
        try { await ClearAsync(cancellationToken); }
        finally { serial.Release(); }
    }

    private async Task ClearAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        List<Exception> errors = [];
        foreach (var frame in frames.ToArray())
        {
            try
            {
                await EvaluateAsync(frame.Context, "(()=>{CSS.highlights.delete('nth-visus-find');CSS.highlights.delete('nth-visus-active');globalThis.__nthFind?.style.remove();delete globalThis.__nthFind;return {count:0,index:-1};})()", CancellationToken.None);
                frames.Remove(frame);
            }
            catch (Exception e) when (ContextGone(e)) { frames.Remove(frame); }
            catch (Exception e) { errors.Add(e); }
        }
        count = 0; active = -1;
        if (errors.Count > 0) throw new InvalidOperationException("Could not clear page find.", new AggregateException(errors));
    }

    private static bool ContextGone(Exception e) => e.Message.Contains("Cannot find context with specified id", StringComparison.Ordinal)
        || e.Message.Contains("Execution context was destroyed", StringComparison.Ordinal);

    private async Task SelectAsync(CancellationToken token)
    {
        var offset = 0;
        foreach (var frame in frames)
        {
            var index = active >= offset && active < offset + frame.Count ? active - offset : -1;
            await EvaluateAsync(frame.Context, "(()=>{const s=globalThis.__nthFind;CSS.highlights.delete('nth-visus-active');const i=" + index + ";if(s&&i>=0){const r=s.ranges[i];CSS.highlights.set('nth-visus-active',new Highlight(r));r.startContainer.parentElement?.scrollIntoView({block:'center',inline:'nearest'});}return {count:s?.ranges.length??0,index:i};})()", token);
            offset += frame.Count;
        }
    }

    private async Task<JsonElement> EvaluateAsync(int context, string expression, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var result = JsonDocument.Parse(await page.CallCdpAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { contextId = context, expression, returnByValue = true, awaitPromise = true }), null, CancellationToken.None));
        token.ThrowIfCancellationRequested();
        if (result.RootElement.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Page find failed.");
        return result.RootElement.GetProperty("result").GetProperty("value").Clone();
    }

    private static IEnumerable<string> FrameIds(JsonElement tree)
    {
        yield return tree.GetProperty("frame").GetProperty("id").GetString()!;
        if (tree.TryGetProperty("childFrames", out var children))
            foreach (var child in children.EnumerateArray())
                foreach (var id in FrameIds(child)) yield return id;
    }

    private const string SearchScript = """
        (async()=>{
          const o=__OPTIONS__, pieces=[], ranges=[];
          if(!globalThis.CSS?.highlights||!globalThis.Highlight)throw Error('Find highlighting unavailable');
          const walker=document.createTreeWalker(document.body??document.documentElement,NodeFilter.SHOW_TEXT);
          let text='',n,visited=0,previous=null,previousBlock=null;
          function append(value,node,offset,end=offset+value.length,collapsed=false){pieces.push({start:text.length,length:value.length,node,offset,endNode:node,endOffset:end,space:collapsed});text+=value;}
          function breakBlock(){if(pieces.at(-1)?.space){pieces.pop();text=text.slice(0,-1);}append('\n',null,0);}
          function rendered(node,mode){
            if(mode!=='normal'&&mode!=='nowrap'&&mode!=='pre-line'){append(node.data,node,0);return;}
            const parts=mode==='pre-line'?/[^\t\n\f\r ]+|[\t\f ]+|\r\n|[\r\n]/g:/[^\t\n\f\r ]+|[\t\n\f\r ]+/g;let part;
            while((part=parts.exec(node.data))!==null){
              const value=part[0],at=part.index,end=at+value.length;
              if(mode==='pre-line'&&/^[\r\n]/.test(value)){if(pieces.at(-1)?.space){pieces.pop();text=text.slice(0,-1);}append('\n',node,at,end);continue;}
              if(!/^[\t\n\f\r ]/.test(value)){append(value,node,at);continue;}
              if(!text.length||text.endsWith('\n'))continue;
              if(pieces.at(-1)?.space){pieces.at(-1).endNode=node;pieces.at(-1).endOffset=end;}
              else append(' ',node,at,end,true);
            }
          }
          function block(p){while(p){const d=getComputedStyle(p).display;if(d!=='inline'&&d!=='contents')return p;p=p.parentElement;}return null;}
          function hasBreak(a,b){const between=document.createRange();between.setStartAfter(a);between.setEndBefore(b);return !!between.cloneContents().querySelector('br');}
          while(n=walker.nextNode()){
            const p=n.parentElement;
            if(p&&!p.closest('script,style,noscript,template,head,[hidden]')){
              const style=getComputedStyle(p), r=document.createRange();r.selectNodeContents(n);
              if(style.visibility!=='hidden'&&style.visibility!=='collapse'&&r.getClientRects().length&&n.data.length){
                const boundary=block(p);
                if(previous&&(boundary!==previousBlock||hasBreak(previous,n)))breakBlock();
                rendered(n,style.whiteSpace);previous=n;previousBlock=boundary;
              }
            }
            if(++visited%256===0)await new Promise(resolve=>setTimeout(resolve,0));
          }
          // Regex case folding preserves UTF-16 offsets (lowercasing the whole document can change its length).
          const escaped=o.query.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
          const pattern=new RegExp(escaped,o.matchCase?'gu':'giu');
          function locate(at,end){let lo=0,hi=pieces.length;const point=end?at-1:at;while(lo<hi){const mid=(lo+hi)>>>1;if(pieces[mid].start<=point)lo=mid+1;else hi=mid;}const p=pieces[lo-1];if(!p?.node)return null;return end&&at===p.start+p.length?[p.endNode,p.endOffset]:[p.node,p.offset+at-p.start];}
          let m,found=0;
          while((m=pattern.exec(text))!==null){
            if(!m[0].length)break;
            const a=locate(m.index,false),b=locate(m.index+m[0].length,true),r=document.createRange();
            if(!a||!b)continue;
            r.setStart(...a);r.setEnd(...b);ranges.push(r);
            if(++found%256===0)await new Promise(resolve=>setTimeout(resolve,0));
          }
          const style=document.createElement('style');
          style.textContent='::highlight(nth-visus-find){background:#f5d76e;color:#161616}::highlight(nth-visus-active){background:#eb9228;color:#161616}';
          (document.head??document.documentElement).append(style);
          globalThis.__nthFind={ranges,style};CSS.highlights.set('nth-visus-find',new Highlight(...ranges));
          return {count:ranges.length,index:ranges.length?0:-1};
        })()
        """;
}
