using System.Reflection;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

public static class StrikeOracleLiquid
{
    public static JewelLiquid From(string maker, string theme = "dark")
    {
        using var doc = StrikeVectors.Read("liquid-palettes.json"); var p = doc.RootElement.GetProperty(maker).GetProperty(theme).GetProperty("high");
        var stops = new[] { p.GetProperty("deep").GetString()!, p.GetProperty("mid").GetString()!, p.GetProperty("light").GetString()!, p.GetProperty("highlight").GetString()! };
        return (JewelLiquid)Activator.CreateInstance(typeof(JewelLiquid), BindingFlags.Instance | BindingFlags.NonPublic, null, [maker, stops], System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
