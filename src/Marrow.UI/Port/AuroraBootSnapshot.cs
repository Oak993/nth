using Avalonia;

namespace Marrow.UI.Port;

internal sealed record AuroraBootSnapshot(Size Size, Rect Window, AuroraBootFrame Frame, double Seconds, bool Dark, bool ReduceMotion, AuroraLoaderTelemetry? Telemetry);
