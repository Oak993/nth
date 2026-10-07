// PORT-ONLY: AC-3's IR-3 lineup algorithm, mirrored intensity_r3.py :447-910; ED-NEW-models-palette-6.
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
namespace Marrow.Core.Port.ModelsPalette;

public readonly record struct ModelPalettePosition(double Strength,int Level,int Levels);
public sealed class MakerLineup
{
    private readonly LineupRanker.Member[] members;
    private readonly PaletteAnchor palette;
    public FrozenDictionary<string,ModelPalettePosition> Positions { get; }
    internal MakerLineup(LineupRanker.Member[] members,PaletteAnchor palette)
    { this.members=members;this.palette=palette;Positions=LineupRanker.Position(members,palette).ToFrozenDictionary(StringComparer.Ordinal); }
    public ModelPalettePosition Lookup(string id)
    {
        var (c,_,date)=LineupRanker.CanonicalFacts(id,false);
        c=c switch {"deepseek-flash"=>"deepseek-v4.1-flash","hy3-paid"=>"hy3",_=>c};
        if(date is not null&&Positions.TryGetValue(c+"-"+date,out var dated))return dated;
        if(Positions.TryGetValue(c,out var direct))return direct;
        if(Positions.TryGetValue(LineupRanker.Canonical(c),out var speed))return speed;
        var h=Regex.Replace(c,@"^claude-(\d+(?:\.\d+)*)-(opus|sonnet|haiku|fable)(?=-|$)","claude-$2-$1",RegexOptions.CultureInvariant);
        h=Regex.Replace(h,@"^(opus|sonnet|haiku|fable)-(?=\d)","claude-$1-",RegexOptions.CultureInvariant);
        var x=h;
        for(var i=0;i<3;i++)
        {
            if(Positions.TryGetValue(x,out var match)||Positions.TryGetValue(LineupRanker.Canonical(x),out match))return match;
            var next=Regex.Replace(x,@"-(none|minimal|low|medium|high|xhigh|extra-high|max|ultra|thinking|reasoning)$","",RegexOptions.CultureInvariant);
            if(next==x)break;x=next;
        }
        for(var i=0;i<3;i++)h=Regex.Replace(h,@"-(none|minimal|low|medium|high|xhigh|extra-high|thinking|reasoning)$","",RegexOptions.CultureInvariant);
        var maker=members.FirstOrDefault()?.Maker??"";
        var orphan=LineupRanker.Canonical(h)!=h&&!members.Any(m=>m.Canon==LineupRanker.Canonical(h));
        var fact=new ModelPaletteFact(h,maker,0,0,0,true);
        var m=LineupRanker.Make(maker,h,[new(fact,true,null)],null,orphan);
        var peer=members.Where(p=>p.Tier==m.Tier&&LineupRanker.CompareVersion(p.Version,m.Version)==0)
            .OrderBy(p=>p.Created==0?long.MaxValue:p.Created).ThenBy(p=>p.Price).ThenBy(p=>p.Canon,StringComparer.Ordinal).FirstOrDefault();
        if(peer is not null)m=LineupRanker.Make(maker,h,[new(fact with {Price=peer.Price,Created=peer.Created},true,null)],null,orphan);
        return LineupRanker.Position([..members,m],palette)[h];
    }
    public string? ResolveAlias(string alias,double? price=null)
    {
        var words=Regex.Split(alias.ToLowerInvariant(),@"[-_.]+").Where(w=>w.Length>0&&w is not("latest" or "claude" or "gpt" or "gemini")).ToArray();
        bool Plain(LineupRanker.Member m)=>!LineupRanker.Tokens(m.Canon).Any(t=>LineupRanker.Modifiers.Contains(t,StringComparer.Ordinal)&&!words.Contains(t,StringComparer.Ordinal));
        var candidates=members.Where(m=>words.All(w=>LineupRanker.Tokens(m.Canon).Contains(w,StringComparer.Ordinal))&&Plain(m)).ToArray();
        if(price is {} p&&candidates.Any(m=>Math.Abs(m.Price-p)<1e-9))candidates=candidates.Where(m=>Math.Abs(m.Price-p)<1e-9).ToArray();
        if(candidates.Length==0)candidates=members.Where(Plain).ToArray();if(candidates.Length==0)candidates=members;
        return LineupRanker.Order(candidates).FirstOrDefault()?.Canon;
    }
}
public static class LineupRanker
{
    private static readonly FrozenDictionary<string,double> Down=Table("nano:0 tiny:0 micro:0 lite:1 xs:1 oss:1 gemma:1 glimmer:1 edge:1 ministral:1 nemo:1 mini:2 small:2 haiku:2");
    private static readonly FrozenDictionary<string,double> Up=Table("flash:3 flashx:3 fast:3 highspeed:3 ultraspeed:3 air:3 turbo:3 lightning:3 scout:4 sonnet:4 medium:4 plus:5 super:5 maverick:5 large:6 pro:6 opus:6 premier:6 max:7 ultra:7 prime:8 fable:9 astra:9");
    private static readonly FrozenDictionary<string,double> Family=Table("luna:4.5 terra:5.5 sol:6.5 astra:9");
    internal static readonly string[] Modifiers=["pro","max","plus","ultra","prime"];
    private static readonly string[] Dates=[@"-\d{4}-\d{2}-\d{2}$",@"-\d{8}$",@"-\d{2}-\d{4}$",@"-\d{2}-\d{2}$",@"-(0\d{3}|1[0-2]\d{2}|2[0-9]{3})$"];
    private static FrozenDictionary<string,double> Table(string s)=>s.Split(' ').Select(x=>x.Split(':')).ToFrozenDictionary(x=>x[0],x=>double.Parse(x[1],CultureInfo.InvariantCulture),StringComparer.Ordinal);
    internal static string[] Tokens(string c)=>Regex.Split(c,@"[-_.\s/]+",RegexOptions.CultureInvariant);
    public static string Canonical(string id,bool keepSpeed=false)=>CanonicalFacts(id,!keepSpeed).Canon;
    internal static (string Canon,bool Stripped,string? Date) CanonicalFacts(string id,bool stripSpeed)
    {
        var x=id.ToLowerInvariant().TrimStart('~').Split('/')[^1];var raw=x;
        x=Regex.Replace(x,@"\[.*?\]","",RegexOptions.CultureInvariant).Split(':')[0];string? date=null;
        for(var i=0;i<4;i++)
        {
            foreach(var rx in Dates){var m=Regex.Match(x,rx,RegexOptions.CultureInvariant);if(m.Success){date??=Regex.Replace(m.Value,@"\D","",RegexOptions.CultureInvariant);x=x[..m.Index];}}
            x=Regex.Replace(x,@"-(free|paid|preview|exp|contributor|thinking|customtools|latest|instruct|it|sante)$","",RegexOptions.CultureInvariant);
            if(stripSpeed)x=Regex.Replace(x,@"-(fast|highspeed|ultraspeed)$","",RegexOptions.CultureInvariant);
        }
        var stripped=x!=raw;x=Regex.Replace(x,@"(?<=[a-z-]\d)-(?=\d{1,2}(?:-|$))",".",RegexOptions.CultureInvariant);
        return (x,stripped,date);
    }
    private static double Tier(string c,string maker)
    {
        var tokens=Tokens(c);var fam=maker=="openai"?tokens.Where(Family.ContainsKey).Select(t=>Family[t]).ToArray():[];
        var downs=tokens.Where(Down.ContainsKey).Select(t=>Down[t]).ToArray();
        if(fam.Length>0){var b=fam.Max();if(tokens.Any(t=>Modifiers.Contains(t,StringComparer.Ordinal)))return b+.4;if(downs.Length>0)return b-new[]{.4,.35,.3}[(int)downs.Min()];return b;}
        if(downs.Length>0)return downs.Min();
        double? Scoped(string t)=> (maker,t) switch {("openai","max")=>4.5,("x-ai","build")=>1,("deepseek","flash")=>4,("meta","muse" or "spark")=>6,("meta","glimmer")=>1,_=>Up.TryGetValue(t,out var n)?n:null};
        var ups=tokens.Select(Scoped).Where(t=>t.HasValue).Select(t=>t!.Value).ToArray();return ups.Length==0?4:ups.Max();
    }
    private static int[] Version(string c,string maker)
    {
        foreach(var token in Regex.Split(c,@"[-_\s]+",RegexOptions.CultureInvariant))
        {
            if(Regex.IsMatch(token,@"^(\d+x)?\d+(\.\d+)?[bmt]$|^a\d+(\.\d+)?b$|^r\d+b$",RegexOptions.CultureInvariant))continue;
            var m=Regex.Match(token,@"(\d+(?:\.\d+)*)",RegexOptions.CultureInvariant);if(!m.Success)continue;
            var parts=m.Value.Split('.').Select(p=>int.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:int.MaxValue).ToArray();
            if(maker=="x-ai"&&parts.Length>=2&&parts[1]>=10&&parts[1]%10==0)parts=[parts[0],parts[1]/10,0,..parts.Skip(2)];
            return parts;
        }
        return [];
    }
    internal sealed record Row(ModelPaletteFact Fact,bool Base,string? Date);
    internal sealed class Member
    {
        internal required string Canon,Maker,Stem;
        internal string? FamilyWord;
        internal double Tier,MajorTier,Price;
        internal long Created,Context;
        internal int[] Version=[];
        internal bool Down,Next,Legacy;
        internal (int Current,int Next,double Tier) Class=>(Legacy?0:1,Next?1:0,Tier);
        internal (int Current,int Next,double Tier) Major=>(Legacy?0:1,Next?1:0,MajorTier);
    }
    internal static Member Make(string maker,string canon,IReadOnlyList<Row> rows,string? basis,bool orphan)
    {
        var rep=rows.OrderBy(r=>!r.Base).ThenBy(r=>r.Fact.Created==0?long.MaxValue:r.Fact.Created).ThenByDescending(r=>r.Fact.Price).ThenByDescending(r=>r.Fact.Context).ThenBy(r=>r.Fact.Id,StringComparer.Ordinal).First().Fact;
        var tc=basis??canon;var tier=orphan?Tier(Canonical(tc),maker)-.05:Tier(tc,maker);
        var tokens=Tokens(tc);var fam=maker=="openai"?tokens.Where(Family.ContainsKey).OrderByDescending(t=>Family[t]).ThenByDescending(t=>t,StringComparer.Ordinal).FirstOrDefault():null;
        return new(){Canon=canon,Maker=maker,Stem=tokens[0],Tier=tier,MajorTier=fam is not null?Family[fam]:orphan?tier+.05:tier,FamilyWord=fam,Down=tokens.Any(Down.ContainsKey),Version=Version(tc,maker),Created=rep.Created,Price=rep.Price,Context=rep.Context};
    }
    internal static int CompareVersion(int[] a,int[] b)
    { for(var i=0;i<Math.Min(a.Length,b.Length);i++){var cmp=a[i].CompareTo(b[i]);if(cmp!=0)return cmp;}return a.Length.CompareTo(b.Length); }
    private static int CompareGroup(Member a,Member b)
    {var c=a.Class.CompareTo(b.Class);if(c!=0)return c;c=CompareVersion(a.Version,b.Version);if(c!=0)return c;c=(a.Created/86400).CompareTo(b.Created/86400);return c!=0?c:a.Price.CompareTo(b.Price);}
    internal static Member[] Order(IEnumerable<Member> members)
    {
        var ms=members.ToArray();var family=ms.Where(m=>m.FamilyWord is not null).ToArray();
        var max=family.Where(m=>m.Version.Length>0).Select(m=>m.Version[0]).DefaultIfEmpty(-1).Max();
        foreach(var m in ms)m.Next=max>=0&&m.FamilyWord is null&&!m.Down&&family.Any(f=>f.Stem==m.Stem)&&m.Version.Length>0&&m.Version[0]>max;
        foreach(var m in ms)m.Legacy=m.Created!=0&&ms.Any(n=>n.Created-m.Created>548L*86400&&(n.Next?1:0,n.Tier).CompareTo((m.Next?1:0,m.Tier))>=0);
        return ms.OrderDescending(Comparer<Member>.Create((a,b)=>{var cmp=CompareGroup(a,b);if(cmp!=0)return cmp;cmp=a.Context.CompareTo(b.Context);return cmp!=0?cmp:StringComparer.Ordinal.Compare(a.Canon,b.Canon);})).ToArray();
    }
    internal static Dictionary<string,ModelPalettePosition> Position(Member[] members,PaletteAnchor palette)
    {
        var ordered=Order(members);var groups=new List<List<Member>>();
        foreach(var m in ordered){if(groups.Count>0&&CompareGroup(groups[^1][0],m)==0)groups[^1].Add(m);else groups.Add([m]);}
        var result=new Dictionary<string,ModelPalettePosition>(StringComparer.Ordinal);if(groups.Count==0)return result;
        var scale=PaletteIntensity.Levels(palette);var g=groups.Count;var n=Math.Min(g,scale.Count);var levels=new int[g];
        if(g<=scale.Count)for(var i=0;i<g;i++)levels[i]=i;
        else
        {
            var heads=groups.Select(x=>x[0]).ToArray();var priority=new List<int>();
            void Add(int i){if(!priority.Contains(i))priority.Add(i);}
            if(heads[0].Class==heads[1].Class)Add(0);
            for(var i=0;i<g-1;i++)if(heads[i].FamilyWord is not null&&heads[i+1].FamilyWord!=heads[i].FamilyWord)Add(i);
            for(var i=0;i<g-1;i++)if(heads[i].Major!=heads[i+1].Major)Add(i);
            for(var i=0;i<g-1;i++)if(heads[i].Class!=heads[i+1].Class)Add(i);
            var runs=new List<int[]>();var start=0;
            for(var i=1;i<=g;i++)if(i==g||heads[i].Class!=heads[i-1].Class){runs.Add(Enumerable.Range(start,i-start-1).ToArray());start=i;}
            for(var r=0;r<runs.Select(x=>x.Length).DefaultIfEmpty(0).Max();r++)foreach(var run in runs)if(r<run.Length)Add(run[r]);
            var steps=priority.Take(n-1).ToHashSet();var lv=0;for(var i=0;i<g;i++){levels[i]=lv;if(steps.Contains(i))lv++;}
        }
        var positions=scale.Positions[n];for(var i=0;i<g;i++)foreach(var m in groups[i])result.Add(m.Canon,new(positions[levels[i]],levels[i]+1,n));
        return result;
    }
    public static FrozenDictionary<string,MakerLineup> Build(IEnumerable<ModelPaletteFact> facts,Func<string,PaletteAnchor> palette)
    {
        var result=new Dictionary<string,MakerLineup>(StringComparer.Ordinal);
        foreach(var maker in facts.Where(f=>f.Eligible).GroupBy(f=>f.Maker,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            var slots=new Dictionary<string,List<Row>>(StringComparer.Ordinal);
            foreach(var f in maker.Where(f=>!f.Builtin))
            {var(c,stripped,date)=CanonicalFacts(f.Id,false);if(!slots.TryGetValue(c,out var rows))slots[c]=rows=[];rows.Add(new(f,!stripped,date));}
            foreach(var f in maker.Where(f=>f.Builtin))
            {var c=Canonical(f.Id,true);if(!slots.ContainsKey(c)&&!slots.ContainsKey(Canonical(c)))slots[c]=[new(f,true,null)];}
            var split=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var c in slots.Keys.Order(StringComparer.Ordinal).ToArray())
            {
                var rs=slots[c];var undated=rs.Where(r=>r.Date is null&&r.Fact.Created!=0).ToArray();var dated=rs.Where(r=>r.Date is not null&&r.Fact.Created!=0).ToArray();
                var anchor=(undated.Length>0?undated:dated).OrderBy(r=>r.Fact.Created).ThenBy(r=>r.Fact.Id,StringComparer.Ordinal).FirstOrDefault();if(anchor is null)continue;
                foreach(var r in dated)if(r.Fact.Created-anchor.Fact.Created>30L*86400)
                {var dc=c+"-"+r.Date;rs.Remove(r);if(!slots.TryGetValue(dc,out var target))slots[dc]=target=[];target.Add(r with {Base=true});split[dc]=c;}
            }
            foreach(var c in slots.Keys.Order(StringComparer.Ordinal).ToArray())
            {if(split.ContainsKey(c))continue;var b=Canonical(c);if(b!=c&&slots.TryGetValue(b,out var baseRows)&&slots.Remove(c,out var rs))baseRows.AddRange(rs.Select(r=>r with {Base=false}));}
            var ms=slots.Where(s=>s.Value.Count>0).OrderBy(s=>s.Key,StringComparer.Ordinal).Select(s=>Make(maker.Key,s.Key,s.Value,split.GetValueOrDefault(s.Key),!split.ContainsKey(s.Key)&&Canonical(s.Key)!=s.Key)).ToArray();
            result[maker.Key]=new(ms,palette(maker.Key));
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
