using System.Text.Json;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Approved static option metadata, retained independently of the animated host.</summary>
public sealed class StaticWallpaperData
{
    public JsonElement Data { get; }
    public double[][] Roles { get; }
    public double[] DarkHues { get; }
    public double[] LightHues { get; }
    public double[] Light { get; }
    public bool OwnLight => Data.GetProperty("light").GetProperty("own").GetBoolean();
    public bool OwnPerspective => Data.GetProperty("persp").GetProperty("own").GetBoolean();
    public bool OwnFocus => Data.GetProperty("focus").GetProperty("own").GetBoolean();
    public double Depth => Data.GetProperty("persp").GetProperty("depth").GetDouble();
    public double PerspectiveDefault => Data.GetProperty("persp").GetProperty("def").GetDouble();
    public double FocusDefault => Data.GetProperty("focus").GetProperty("def").GetDouble();

    public StaticWallpaperData(JsonElement data)
    {
        Data = data.Clone();
        Roles = Data.GetProperty("roles").EnumerateArray().Select(Numbers).ToArray();
        DarkHues = Numbers(Data.GetProperty("hues").GetProperty("d"));
        LightHues = Numbers(Data.GetProperty("hues").GetProperty("l"));
        Light = Numbers(Data.GetProperty("light").GetProperty("def"));
    }

    internal static double[] Numbers(JsonElement array) => array.EnumerateArray().Select(v => v.GetDouble()).ToArray();
}
