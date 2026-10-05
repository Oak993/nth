// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): one frame of the draining line.
namespace Marrow.App.Port.Voice.Countdown;

/// <summary>Whether the line shows, how much of its length is left (1 to 0), its colour this frame (the arrival brightening included) and that brightening's amount (0 to 1).</summary>
public readonly record struct CapCountdownFrame(bool Visible, double Fraction, CountdownColour Colour, double Brightening);
