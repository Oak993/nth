// PORT-ONLY: web surfaces W11, web_device's sizes (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.7 device sizes, 5.10 "web_device
// {tab, preset | width,height}: device emulation (preview tabs)"; ED-NEW-web-w11-1). No Swift counterpart. A preset is a CSS
// size, a device scale and whether it is a touch phone; desktop clears the emulation (Emulation.clearDeviceMetricsOverride).
namespace Marrow.Core.Web;

/// <summary>One device size: CSS width and height, the device scale factor and whether it emulates a mobile device.</summary>
public sealed record WebDeviceSize(int Width, int Height, double Scale, bool Mobile);

public static class WebAgentDevices
{
    /// <summary>The presets web_device takes; desktop has no size (it clears the emulation).</summary>
    public static IReadOnlyDictionary<string, WebDeviceSize?> Presets { get; } = new Dictionary<string, WebDeviceSize?>(StringComparer.OrdinalIgnoreCase)
    {
        ["phone"] = new WebDeviceSize(390, 844, 3, true),
        ["tablet"] = new WebDeviceSize(820, 1180, 2, true),
        ["laptop"] = new WebDeviceSize(1280, 800, 1, false),
        ["desktop"] = null,
    };

    /// <summary>The smallest and largest emulated side (the device frame's own limits, DESIGN 4.1 table: 240 to 4,000).</summary>
    public const int MinSide = 240;

    public const int MaxSide = 4000;
}
