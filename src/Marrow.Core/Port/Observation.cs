using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Swift <c>@Observable</c> for a C# model that Avalonia binds to (INotifyPropertyChanged) and that
/// code and tests can track Swift-style: per property, per access, across objects (ED-070).
/// <code>
///   Swift                                   C#
///   var title: String                       public string Title { get => Read(title); set => Set(ref title, value); }
///   var x: T { didSet { ... } }             set => Set(ref x, value, didSet: oldValue => ...)
///   var v: Value (Value not Equatable)      set => SetAlways(ref v, value)
///   var derived: T { a &amp;&amp; b.value }         public T Derived => Computed(() => A &amp;&amp; B.Value);
///   @ObservationIgnored var cache           a plain field or auto-property (never Read/Set)
/// </code>
/// Constructors assign BACKING FIELDS: Swift runs no didSet and notifies nobody for assignments in init.
/// Source: draft C (proto-C Port/Observation.cs) for tracking; draft B for SetAlways and the isolation check.
/// </summary>
public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private Dictionary<string, List<ObservationTracker>>? trackers;
    private Dictionary<string, ObservationTracker>? computed;

    /// <summary>Records the access for whoever is tracking (a view, a computed property), then returns it.</summary>
    protected T Read<T>(T value, [CallerMemberName] string property = "")
    {
        ObservationTracking.RecordAccess(this, property);
        return value;
    }

    /// <summary>
    /// A tracked assignment for a property whose Swift type is Equatable or a class: stores, runs didSet
    /// (always, even for an equal value, as Swift does), then tells trackers and bindings only when the value
    /// changed under EqualityComparer.Default (Swift 6.2's shouldNotifyObservers, UNVERIFIED O-05; an equal
    /// value renders identically, so the only visible difference would be a render count).
    /// </summary>
    protected void Set<T>(ref T field, T value, Action<T>? didSet = null, [CallerMemberName] string property = "")
    {
        CheckIsolation();
        var oldValue = field;
        field = value;
        var changed = !EqualityComparer<T>.Default.Equals(oldValue, value);
        didSet?.Invoke(oldValue);
        if (changed) Notify(property);
    }

    /// <summary>A tracked assignment for a Swift type that is neither Equatable nor a class (an unconstrained
    /// generic such as <c>ObservedValue&lt;Value&gt;</c>): every assignment notifies.</summary>
    protected void SetAlways<T>(ref T field, T value, Action<T>? didSet = null, [CallerMemberName] string property = "")
    {
        CheckIsolation();
        var oldValue = field;
        field = value;
        didSet?.Invoke(oldValue);
        Notify(property);
    }

    /// <summary>
    /// A Swift computed property: recomputed on every read (as Swift does), and bindings to it are told when
    /// anything it read changes, including properties of OTHER observable objects.
    /// </summary>
    protected T Computed<T>(Func<T> compute, [CallerMemberName] string property = "")
    {
        computed ??= new(StringComparer.Ordinal);
        if (computed.Remove(property, out var previous)) previous.Cancel();
        var (value, tracker) = ObservationTracking.Track(compute, () =>
        {
            computed?.Remove(property);
            Notify(property);
        });
        computed[property] = tracker;
        return value;
    }

    protected void Notify(string property)
    {
        if (trackers is not null && trackers.Remove(property, out var list))
            foreach (var tracker in list.ToArray()) tracker.Fire();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    /// <summary>Overridden by <see cref="MainActorModel"/> to trap on an off-main mutation.</summary>
    protected virtual void CheckIsolation() { }

    /// <summary>Diagnostic: how many live trackers observe a property (tests prove trackers are released).</summary>
    public int TrackerCount(string property) =>
        trackers is not null && trackers.TryGetValue(property, out var list) ? list.Count : 0;

    internal void Register(string property, ObservationTracker tracker)
    {
        trackers ??= new(StringComparer.Ordinal);
        if (!trackers.TryGetValue(property, out var list)) trackers[property] = list = [];
        list.Add(tracker);
    }

    internal void Unregister(string property, ObservationTracker tracker)
    {
        if (trackers is not null && trackers.TryGetValue(property, out var list)) list.Remove(tracker);
    }
}

/// <summary>
/// A <c>@MainActor @Observable</c> type: every tracked assignment asserts the main actor, turning the off-main
/// mutation Swift rejects at compile time into a trap in tests.
/// </summary>
public abstract class MainActorModel : ObservableModel
{
    protected override void CheckIsolation() => MainActor.AssumeIsolated();
}

/// <summary>Swift <c>withObservationTracking(_:onChange:)</c> and the engine under Computed and view tracking.</summary>
public static class ObservationTracking
{
    internal readonly record struct Key(ObservableModel Owner, string Property)
    {
        public bool Equals(Key other) => ReferenceEquals(Owner, other.Owner) && string.Equals(Property, other.Property, StringComparison.Ordinal);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Owner), StringComparer.Ordinal.GetHashCode(Property));
    }

    private sealed class Scope(Scope? parent)
    {
        public readonly Scope? Parent = parent;
        public readonly HashSet<Key> Accesses = [];
    }

    // Tracking happens where views evaluate: on the main thread (R-ISO-1), so a thread-static scope suffices.
    [ThreadStatic] private static Scope? current;

    internal static void RecordAccess(ObservableModel owner, string property) => current?.Accesses.Add(new(owner, property));

    /// <summary>
    /// <c>withObservationTracking(_:onChange:)</c>: onChange runs once, synchronously, after the first write to
    /// anything <paramref name="apply"/> read. (Swift calls it at willSet; the source has zero direct uses.)
    /// </summary>
    public static T WithTracking<T>(Func<T> apply, Action onChange) => Track(apply, onChange).Value;

    public static (T Value, ObservationTracker Tracker) Track<T>(Func<T> apply, Action onChange)
    {
        var scope = new Scope(current);
        current = scope;
        T value;
        try { value = apply(); }
        finally { current = scope.Parent; }
        // A nested read (a computed property inside a tracked expression) is a read of the outer
        // expression too, exactly as SwiftUI sees it.
        current?.Accesses.UnionWith(scope.Accesses);
        return (value, new ObservationTracker(onChange, scope.Accesses));
    }
}

/// <summary>One armed observation: fires once, then releases every registration.</summary>
public sealed class ObservationTracker
{
    private Action? onChange;
    private readonly ObservationTracking.Key[] keys;

    internal ObservationTracker(Action onChange, IEnumerable<ObservationTracking.Key> keys)
    {
        this.onChange = onChange;
        this.keys = keys.ToArray();
        foreach (var key in this.keys) key.Owner.Register(key.Property, this);
    }

    internal void Fire()
    {
        var action = onChange;
        if (action is null) return;
        Cancel();
        action();
    }

    public void Cancel()
    {
        if (onChange is null) return;
        onChange = null;
        foreach (var key in keys) key.Owner.Unregister(key.Property, this);
    }
}
