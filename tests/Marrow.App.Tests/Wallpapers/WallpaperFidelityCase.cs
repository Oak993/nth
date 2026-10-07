using System.Text.Json;

namespace Marrow.App.Tests.Wallpapers;

// Each entry retains user inputs and the source host's resolved uniforms; the app gets user inputs only.
public sealed class WallpaperFidelityCase(JsonElement inputs)
{
    public JsonElement Inputs { get; } = inputs;
    public string Collection => Inputs.GetProperty("collection").GetString()!;
    public string Wallpaper => Inputs.GetProperty("wallpaper").GetString()!;
    public string Variant => Inputs.GetProperty("variant").GetString()!;
    public string File => Inputs.GetProperty("file").GetString()!;
    public bool Premium => Variant is not ("default" or "quiet" or "rich");
    public string Label => FormattableString.Invariant($"{Collection}/{Wallpaper}/{Variant}/{Inputs.GetProperty("packId").GetString()}/{(Inputs.GetProperty("dark").GetBoolean() ? "dark" : "light")}/t={Inputs.GetProperty("time").GetDouble():G}/clock={Inputs.GetProperty("clockHours").GetDouble():G}/accent={Inputs.GetProperty("accent").GetString()}/focus={Inputs.GetProperty("focus").GetDouble():G}/tilt=[{Inputs.GetProperty("tilt")[0].GetDouble():G},{Inputs.GetProperty("tilt")[1].GetDouble():G}]");
}
