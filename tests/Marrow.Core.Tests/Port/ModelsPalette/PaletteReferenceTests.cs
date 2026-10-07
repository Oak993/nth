using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Tests.Port.ModelsPalette;

// PORT-ONLY: complete IR-3 reference cells and real published lineup; ED-NEW-models-palette-6.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteReferenceTests
{
    [Fact]
    public void Every_stop_title_and_motion_dial_matches_the_executable_IR3_reference()
    {
        using var fixture=Read();
        foreach(var cell in fixture.RootElement.GetProperty("cells").EnumerateArray())
        {
            var palette=PaletteSourcePack.Default.Palettes[cell.GetProperty("key").GetString()!];
            var word=cell.GetProperty("effort").GetString(); var s=cell.GetProperty("s").GetDouble();
            Assert.Equal(cell.GetProperty("e").GetDouble(),PaletteIntensity.Effort(word));
            var stops=PaletteIntensity.Stops(palette,s,PaletteIntensity.Effort(word));
            Assert.Equal(cell.GetProperty("stops").EnumerateArray().Select(x=>x.GetString()),stops);
            Assert.Equal(cell.GetProperty("title").GetString(),PaletteIntensity.Title(palette,stops));
            var motion=PaletteIntensity.Motion(palette.Key,s,PaletteIntensity.Effort(word));
            foreach(var dial in cell.GetProperty("motion").EnumerateObject()) Assert.Equal(dial.Value.GetDouble(),motion[dial.Name]);
        }
    }

    [Fact]
    public void Every_perceptual_level_matches_reference_and_meets_the_point_zero_two_floor()
    {
        using var fixture=Read();
        foreach(var entry in fixture.RootElement.GetProperty("levels").EnumerateObject())
        {
            var palette=PaletteSourcePack.Default.Palettes[entry.Name]; var scale=PaletteIntensity.Levels(palette);
            Assert.Equal(entry.Value[0].GetInt32(),scale.Count);
            foreach(var n in entry.Value[1].EnumerateObject())
            {
                var actual=scale.Positions[int.Parse(n.Name,System.Globalization.CultureInfo.InvariantCulture)];
                Assert.Equal(n.Value.EnumerateArray().Select(x=>x.GetDouble()),actual);
                for(var i=1;i<actual.Count;i++) Assert.True(PaletteMath.Distance(PaletteIntensity.Stops(palette,actual[i-1],1),PaletteIntensity.Stops(palette,actual[i],1))>=.02);
            }
        }
    }

    [Fact]
    public void All_global_lineups_match_the_reference_in_ten_shuffled_instances()
    {
        using var fixture=Read();
        for(var instance=0;instance<10;instance++)
        {
            var random=new Random(instance);
            var rows=PaletteSourcePack.Default.Baseline.OrderBy(_=>random.Next()).ToArray();
            var result=LineupRanker.Build(rows,maker=>PaletteSourcePack.Default.ForMaker(maker));
            foreach(var maker in fixture.RootElement.GetProperty("lineups").EnumerateObject())
                foreach(var model in maker.Value.GetProperty("positions").EnumerateObject())
                {
                    var actual=result[maker.Name].Lookup(model.Name);
                    Assert.Equal(model.Value[0].GetDouble(),actual.Strength);
                    Assert.Equal(model.Value[1].GetInt32(),actual.Level);
                    Assert.Equal(model.Value[2].GetInt32(),actual.Levels);
                }
        }
    }

    [Fact]
    public void Harness_word_order_effort_synonyms_and_new_generation_preserve_global_identity()
    {
        var pack=PaletteSourcePack.Default;
        var lineups=LineupRanker.Build(pack.Baseline,pack.ForMaker);
        var claude=lineups["anthropic"];
        Assert.Equal(claude.Lookup("claude-opus-5.5"),claude.Lookup("claude-5.5-opus-high-thinking"));
        Assert.Equal(claude.Lookup("claude-opus-5.5"),claude.Lookup("opus-5.5"));
        Assert.Equal(PaletteIntensity.Effort("xhigh"),PaletteIntensity.Effort("extra high"));
        Assert.Equal(1,PaletteIntensity.Effort("ultra"));
        var openai=LineupRanker.Build(pack.Baseline.Concat([new ModelPaletteFact("openai/gpt-7","openai",0,2000000000,1000000,true)]),pack.ForMaker)["openai"];
        Assert.Equal(1,openai.Lookup("gpt-7").Level);
        Assert.True(openai.Lookup("gpt-7-mini").Level>1);
    }

    private static JsonDocument Read()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Marrow.sln")))root=root.Parent;
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,"tests/fixtures/models-palette/ir3.json")));
    }
}
