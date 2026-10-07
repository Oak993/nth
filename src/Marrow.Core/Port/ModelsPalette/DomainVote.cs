// PORT-ONLY: AC-3 domain votes over a pinned ICANN PSL; no guessed maker domains; ED-617.
using System.Collections.Frozen;
using System.Globalization;
namespace Marrow.Core.Port.ModelsPalette;
public readonly record struct MakerDomainVote(string Url,bool Trusted);
public static class DomainVote
{
    private static readonly FrozenSet<string> suffixes=Load();
    private static readonly FrozenSet<string> denied=new[]{"huggingface.co","github.com","x.com","twitter.com","linkedin.com","discord.com","youtube.com"}.ToFrozenSet(StringComparer.Ordinal);
    private static FrozenSet<string> Load()
    {
        using var stream=typeof(DomainVote).Assembly.GetManifestResourceStream("Marrow.Core.ModelsPalette.PslIcann.txt")!;
        using var text=new StreamReader(stream);return text.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>
        {var prefix=s.StartsWith('!')?"!":s.StartsWith("*.",StringComparison.Ordinal)?"*.":"";return prefix+new IdnMapping().GetAscii(s[prefix.Length..]).ToLowerInvariant();}).ToFrozenSet(StringComparer.Ordinal);
    }
    public static string? Registrable(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.HostNameType!=UriHostNameType.Dns)return null;
        var labels=uri.IdnHost.ToLowerInvariant().TrimEnd('.').Split('.');int count=1;
        for(var i=0;i<labels.Length;i++)
        {
            var tail=string.Join('.',labels.Skip(i));
            if(suffixes.Contains("!"+tail)){count=labels.Length-i-1;break;}
            if(suffixes.Contains(tail))count=Math.Max(count,labels.Length-i);
            if(i>0&&suffixes.Contains("*."+tail))count=Math.Max(count,labels.Length-i+1);
        }
        return labels.Length<=count?null:string.Join('.',labels.Skip(labels.Length-count-1));
    }
    public static string? Choose(IEnumerable<MakerDomainVote> votes)
    {
        return votes.Select(v=>(Domain:Registrable(v.Url),v.Trusted)).Where(v=>v.Domain is not null&&!denied.Contains(v.Domain))
            .GroupBy(v=>v.Domain!,StringComparer.Ordinal).Where(g=>g.Count()>=2||g.Any(v=>v.Trusted)).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key,StringComparer.Ordinal).Select(g=>g.Key).FirstOrDefault();
    }
}
