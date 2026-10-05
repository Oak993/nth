using Avalonia.Controls;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A window holding a bottom-pinned stack, its departures overlay, their WindowGlide and the manual clocks (WindowGlideTests).</summary>
public sealed record WindowGlideRig(Window Window, StackPanel Stack, Canvas Departures, WindowGlide Glide, ManualFrameSource Frames, ManualTimeProvider Time);
