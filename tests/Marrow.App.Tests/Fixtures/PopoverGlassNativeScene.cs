using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Marrow.App.Tests.Fixtures;

internal sealed record PopoverGlassNativeScene(Avalonia.Controls.Window Window, Popup Popup, Control Anchor, double Scale);
