using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/ObservedValue.swift (B2 1fbf81e), :1-14.

/// <summary>
/// One value under its own observation. A view that reads a thread or project through
/// its cell depends on that item alone, so a change to any other item never re-renders it.
/// </summary> // :4-5
public sealed class ObservedValue<TValue> : MainActorModel // :6-8 @MainActor @Observable final class ObservedValue<Value>, R-NAME-1
{
    private TValue value; // :9 var value: Value

    public TValue Value // :9, R-OBS-3: Value is unconstrained, so SetAlways
    {
        get => Read(value);
        set => SetAlways(ref this.value, value);
    }

    public ObservedValue(TValue value) // :11-13 init(_ value: Value) { self.value = value }
    {
        this.value = value;
    }
}
