using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>An optional-string twin of <see cref="ObservableIntBox"/>: the effort card's chosen effort, observable.</summary>
public sealed class ObservableStringBox : MainActorModel
{
    private string? held;

    public string? Value { get => Read(held); set => Set(ref held, value); }

    public Binding<string?> AsBinding() => new(() => Value, v => Value = v);
}
