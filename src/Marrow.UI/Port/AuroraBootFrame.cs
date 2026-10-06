using Avalonia;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113. One rectangle/progress drives the tile and the eventual main-window clip.
public readonly record struct AuroraBootFrame(Rect Tile, double CornerRadius, Rect ContentClip, Rect TitleBand,
    double Progress, double Launch, double Idle, double TileOpacity, double ContentOpacity, double GlowOpacity,
    double ScrimOpacity, bool MainWindowShown, bool StageClosed);
