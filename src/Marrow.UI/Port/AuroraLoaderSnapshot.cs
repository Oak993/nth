using Avalonia;

namespace Marrow.UI.Port;

// PORT-ONLY: immutable UI-to-compositor state, sent only when layout, theme or loading state changes.
internal sealed record AuroraLoaderSnapshot(AuroraLoaderKind Kind, Size Size, bool Dark, bool ReduceMotion,
    TimeProvider Clock, long Started, long? Finished, AuroraLoaderTelemetry? Telemetry);
