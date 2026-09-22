using System.ComponentModel;
using System.Reflection;
using Xunit;

namespace Marrow.Testing;

/// <summary>
/// C# cannot check a switch over an abstract-record union for exhaustiveness: the discard arm the compiler demands
/// also hides a forgotten case (draft B, proto-B/Proto.Exhaustive). This makes the check mechanical: a test supplies
/// one sample per case, this proves the samples cover every nested sealed case, and the test then runs each
/// switch-bearing member over them, so a forgotten case reaches its UnreachableException in a test (ED-075).
/// </summary>
public static class UnionCoverage
{
    public static IReadOnlyList<Type> Cases<TUnion>() where TUnion : class =>
        typeof(TUnion).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsSealed && typeof(TUnion).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Fails unless <paramref name="samples"/> holds at least one instance of every case.</summary>
    public static IReadOnlyList<TUnion> AssertCovers<TUnion>(params TUnion[] samples) where TUnion : class
    {
        var missing = Cases<TUnion>().Where(c => !samples.Any(s => s.GetType() == c)).Select(c => c.Name).ToArray();
        Assert.True(missing.Length == 0, $"{typeof(TUnion).Name} samples miss: {string.Join(", ", missing)}");
        return samples;
    }
}

/// <summary>Records INotifyPropertyChanged traffic: what a binding would have been told.</summary>
public sealed class ChangeLog : IDisposable
{
    private readonly INotifyPropertyChanged source;

    public ChangeLog(INotifyPropertyChanged source)
    {
        this.source = source;
        source.PropertyChanged += OnChanged;
    }

    public List<string> Names { get; } = [];

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Names.Add(e.PropertyName ?? "");

    public void Dispose() => source.PropertyChanged -= OnChanged;
}
