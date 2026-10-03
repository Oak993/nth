namespace Marrow.UI.Tests.Fixtures;

// An observable flag a test toggles to show or hide something (PopoverCardTests' deferred popover content).
public sealed class ShownFlag : Marrow.Core.Port.ObservableModel
{
    private bool value;

    public bool Value { get => Read(value); set => Set(ref this.value, value); }
}
