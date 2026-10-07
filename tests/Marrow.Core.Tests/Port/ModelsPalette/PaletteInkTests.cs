using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Core.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-18")]
public sealed class PaletteInkTests
{
    [Fact]
    public void The_fluid_matches_the_independently_executed_locked_v22_at_every_captured_step()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Marrow.sln")))root=root.Parent;
        using var vectors=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!.FullName,"porting/evidence/models-palette/round2/ink/v22-vectors.json")));
        foreach(var group in vectors.RootElement.GetProperty("rows").EnumerateArray().GroupBy(x=>x.GetProperty("nx").GetInt32()))
        {
            var sim=new PaletteInk(group.Key,group.First().GetProperty("ny").GetInt32());var step=0;
            foreach(var row in group)
            {
                while(step<row.GetProperty("step").GetInt32()){sim.Step(1/60d);step++;}
                Assert.InRange(Math.Abs(sim.Density.Sum(x=>(double)x)-row.GetProperty("mass").GetDouble()),0,1e-5);
                foreach(var cell in row.GetProperty("cells").EnumerateArray())
                {
                    var k=sim.Index(cell.GetProperty("i").GetInt32(),cell.GetProperty("j").GetInt32());
                    Assert.InRange(Math.Abs(sim.Density[k]-cell.GetProperty("d").GetDouble()),0,1e-6);
                    Assert.InRange(Math.Abs(sim.U[k]-cell.GetProperty("u").GetDouble()),0,1e-5);
                    Assert.InRange(Math.Abs(sim.V[k]-cell.GetProperty("v").GetDouble()),0,1e-5);
                }
            }
        }
    }
}
