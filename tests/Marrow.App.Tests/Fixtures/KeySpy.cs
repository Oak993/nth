using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Records the keys that reach a control (bubbling, not already handled): a key the window took never does.</summary>
public sealed class KeySpy
{
    public KeySpy(Control control) => control.AddHandler(InputElement.KeyDownEvent, (_, e) => Keys.Add((e.Key, e.KeyModifiers)), RoutingStrategies.Bubble);

    public List<(Key Key, KeyModifiers Modifiers)> Keys { get; } = [];
}
