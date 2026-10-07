// PORT-ONLY: maker identity and router policy, AC-3 PLAN 4.1 (OD-0063/0072); ED-NEW-models-palette-6.
using System.Text.RegularExpressions;
namespace Marrow.Core.Port.ModelsPalette;
public readonly record struct MakerIdentity(string Maker,bool Router=false,bool ProviderOwn=false);
public static class Makers
{
    public static string Alias(string slug)=>slug.ToLowerInvariant().TrimStart('~') switch
    {"meta-llama"=>"meta","zai"=>"z-ai","xai"=>"x-ai","mistral"=>"mistralai","moonshot"=>"moonshotai","bytedance"=>"bytedance-seed",var s=>s};
    public static MakerIdentity Resolve(string provider,string id,IReadOnlyList<ModelPaletteFact> boundary)
    {
        var p=provider.ToLowerInvariant();var x=Regex.Replace(id.ToLowerInvariant().TrimStart('~'),@"\[.*?\]","",RegexOptions.CultureInvariant).Split(':')[0];
        if(x.StartsWith("opencode-go/",StringComparison.Ordinal))x=x[12..];
        if(p=="pi"&&x.StartsWith("openrouter/",StringComparison.Ordinal))x=x[11..];
        if(x.Contains('/',StringComparison.Ordinal))
        {var slug=x.Split('/')[0];return slug=="openrouter"?new("openrouter",true):new(Alias(slug));}
        var own=p switch {"claude"=>"anthropic","codex"=>"openai","antigravity"=>"google","deepseek"=>"deepseek","meta"=>"meta","zai"=>"z-ai","grok"=>"x-ai",_=>null};
        if(own is not null)return new(own);
        var c=LineupRanker.Canonical(x);var match=boundary.Where(f=>LineupRanker.Canonical(f.Id)==c).OrderBy(f=>f.Maker,StringComparer.Ordinal).FirstOrDefault();
        if(match is not null)return new(Alias(match.Maker));
        var family=Regex.Match(x,@"^(claude|gpt|o\d+|codex|gemini|gemma|grok|glm|kimi|muse|llama|deepseek|qwen|qwq|mistral|codestral|devstral|minimax|nemotron|granite|sonar|command|aya)(?=\b|\d)",RegexOptions.CultureInvariant).Value;
        var maker=family switch {"claude"=>"anthropic","gpt" or "codex"=>"openai","gemini" or "gemma"=>"google","grok"=>"x-ai","glm"=>"z-ai","kimi"=>"moonshotai","muse" or "llama"=>"meta","deepseek"=>"deepseek","qwen" or "qwq"=>"qwen","mistral" or "codestral" or "devstral"=>"mistralai","minimax"=>"minimax","nemotron"=>"nvidia","granite"=>"ibm-granite","sonar"=>"perplexity","command" or "aya"=>"cohere",_=>family.StartsWith('o')&&family.Length>1&&char.IsAsciiDigit(family[1])?"openai":p};
        return new(maker,ProviderOwn:maker==p);
    }
}
