namespace Marrow.Core.Web;

// PORT-ONLY: approved DESIGN 5.7-5.8, 3.11; ED-NEW-web-w9-1. CSS-pixel device sizes and per-tab drawer state.
public enum DevicePreset { Phone, Tablet, Laptop, Fill, Responsive }

public sealed record DeviceMetrics(int Width, int Height, double DeviceScaleFactor, bool Mobile, bool Touch, double Scale);

public sealed record PreviewState
{
    public DevicePreset Preset { get; init; } = DevicePreset.Phone;
    public int Width { get; init; } = 390;
    public int Height { get; init; } = 844;
    public bool Rotated { get; init; }
    public bool ReloadOnSave { get; init; } = true;
    public bool ConsoleOpen { get; init; } = true;
    public bool ConsoleCollapsed { get; init; }
    public double ConsoleHeight { get; init; } = 190;

    public PreviewState Normalized() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : DevicePreset.Phone,
        Width = Math.Clamp(Width, 240, 4000), Height = Math.Clamp(Height, 240, 4000),
        ConsoleHeight = double.IsFinite(ConsoleHeight) ? Math.Clamp(ConsoleHeight, 90, 4000) : 190,
    };

    public DeviceMetrics Metrics(double viewportWidth, double viewportHeight)
    {
        var state = Normalized();
        var (width, height, dpr, mobile, touch) = state.Preset switch
        {
            DevicePreset.Phone => (390, 844, 3d, true, true),
            DevicePreset.Tablet => (820, 1180, 2d, true, true),
            DevicePreset.Laptop => (1280, 800, 1d, false, false),
            DevicePreset.Fill => (Math.Max(1, (int)viewportWidth), Math.Max(1, (int)viewportHeight), 1d, false, false),
            DevicePreset.Responsive => (state.Width, state.Height, 1d, false, false),
            _ => throw new ArgumentOutOfRangeException(nameof(Preset)),
        };
        if (state.Rotated && state.Preset != DevicePreset.Fill) (width, height) = (height, width);
        var fit = state.Preset == DevicePreset.Fill ? 1 : Math.Clamp(Math.Min((viewportWidth - 28) / width, (viewportHeight - 28) / height), .001, 1);
        return new DeviceMetrics(width, height, dpr, mobile, touch, fit);
    }
}

public sealed class PreviewDrawerState
{
    private double beforeDrag;
    public bool Open { get; set; } = true;
    public bool Collapsed { get; private set; }
    public double Height { get; private set; } = 190;
    public double ShownHeight => !Open ? 0 : Collapsed ? 24 : Height;
    public void Load(PreviewState state) { Open = state.ConsoleOpen; Collapsed = state.ConsoleCollapsed; Height = state.Normalized().ConsoleHeight; }
    public void Begin() => beforeDrag = Height;
    public void DragTo(double value, double paneHeight)
    {
        if (value < 34) { Height = beforeDrag > 90 ? beforeDrag : 190; Collapsed = true; return; }
        Collapsed = false; Height = Math.Clamp(value, 90, Math.Max(90, paneHeight * .6));
    }
    public void End() => beforeDrag = Height;
    public void Collapse() { Collapsed = true; }
    public void Restore() { Open = true; Collapsed = false; }
    public void Reset() { Open = true; Collapsed = false; Height = 190; }
}
