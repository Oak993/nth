// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): one reported row of the hardware card (V-10.1).
namespace Marrow.Services.Port.Voice.Probe;

/// <summary>A finished step: the row's name and the text shown for it.</summary>
public sealed record ProbeRow(string Name, string Text)
{
    public const string Processor = "Processor";
    public const string Memory = "Memory";
    public const string Graphics = "Graphics";
    public const string GpuBackend = "GPU backend";
}
