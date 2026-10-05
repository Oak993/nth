// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): a line colour as the model carries it.
namespace Marrow.App.Port.Voice.Countdown;

/// <summary>An sRGB colour, each channel 0 to 1 (not gamma-decoded), and its opacity 0 to 1.</summary>
public readonly record struct CountdownColour(double R, double G, double B, double A = 1.0);
