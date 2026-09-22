using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY. The SwiftUI view mechanics that are not Avalonia types (ED-071): the animation transaction,
// body tracking, .onChange, .task(id:) and Binding. They need no Avalonia reference, but they are view concepts,
// so they live in the UI layer (Core and Services have no SwiftUI construct: survey section 2).
// Source: draft C (proto-C Port/Observation.cs), with task bodies on the ED-066 task model.

/// <summary>
/// A SwiftUI animation value. Abstract on purpose: the concrete curves (easeOut, spring with response and
/// damping, ED-011) are defined by the UI units that port Chrome.swift, not by the foundation.
/// </summary>
public abstract record Animation;

/// <summary>
/// The SwiftUI transaction: <c>withAnimation(anim) { mutate }</c> marks only the state changes made inside the
/// closure. Tracked and OnChange capture it when a change is scheduled, so an apply animates exactly the
/// changes SwiftUI would. It has no global effect.
/// </summary>
public static class Transaction
{
    [ThreadStatic] private static Animation? current;

    public static Animation? Current => current;

    /// <summary><c>withAnimation(animation) { body }</c>.</summary>
    public static void WithAnimation(Animation? animation, Action body)
    {
        var previous = current;
        current = animation;
        try { body(); }
        finally { current = previous; }
    }
}

/// <summary>
/// A SwiftUI body sub-expression: re-evaluated once per main-actor turn after anything it read changed, with the
/// result handed to <c>apply</c>. A burst of writes is one apply (a SwiftUI render pass), and the apply runs
/// inside the transaction of the change that scheduled it. The apply runs outside any Swift task.
/// </summary>
public sealed class Tracked<T> : IDisposable
{
    private readonly Func<T> value;
    private readonly Action<T> apply;
    private ObservationTracker? tracker;
    private bool scheduled;
    private bool disposed;
    private Animation? pendingAnimation;

    public Tracked(Func<T> value, Action<T> apply)
    {
        this.value = value;
        this.apply = apply;
        apply(Evaluate());
    }

    private T Evaluate()
    {
        var (result, next) = ObservationTracking.Track(value, Schedule);
        tracker = next;
        return result;
    }

    private void Schedule()
    {
        if (disposed) return;
        pendingAnimation ??= Transaction.Current;
        if (scheduled) return;
        scheduled = true;
        MainActor.Post(Update);
    }

    private void Update()
    {
        scheduled = false;
        if (disposed) return;
        var animation = pendingAnimation;
        pendingAnimation = null;
        Transaction.WithAnimation(animation, () => apply(Evaluate()));
    }

    public void Dispose()
    {
        disposed = true;
        tracker?.Cancel();
    }
}

/// <summary>
/// SwiftUI <c>.onChange(of:initial:)</c>: after the change, once per turn, only when the value differs (Equatable),
/// with (oldValue, newValue); <c>initial: true</c> also fires once at attach with (value, value).
/// </summary>
public sealed class OnChange<T> : IDisposable
{
    private readonly Tracked<T> tracked;
    private T last = default!;
    private bool primed;

    public OnChange(Func<T> value, Action<T, T> action, bool initial = false)
    {
        tracked = new Tracked<T>(value, current =>
        {
            if (!primed)
            {
                primed = true;
                last = current;
                if (initial) action(current, current);
                return;
            }
            if (EqualityComparer<T>.Default.Equals(last, current)) return;
            var old = last;
            last = current;
            action(old, current);
        });
    }

    public void Dispose() => tracked.Dispose();
}

/// <summary>
/// SwiftUI <c>.task(id:)</c>: starts after appear (on a later main turn), is cancelled on disappear, and is cancelled
/// and restarted when the id changes (compared with ==). The body is a main-actor task; it reads its own
/// cancellation through SwiftTask.IsCancelled. <c>.task</c> without an id is a ViewTask with a constant id.
/// </summary>
public sealed class ViewTask<TId>
{
    private readonly Func<TId, Task> body;
    private TId id;
    private bool appeared;

    public ViewTask(TId id, Func<TId, Task> body)
    {
        this.id = id;
        this.body = body;
    }

    public TaskHandle? Running { get; private set; }

    public void Appear()
    {
        if (appeared) return;
        appeared = true;
        Start();
    }

    public void Disappear()
    {
        appeared = false;
        Running?.Cancel();
        Running = null;
    }

    public void UpdateId(TId newId)
    {
        if (EqualityComparer<TId>.Default.Equals(id, newId)) return;
        id = newId;
        if (!appeared) return;
        Running?.Cancel();
        Start();
    }

    private void Start()
    {
        var current = id;
        Running = MainActor.Spawn(() => body(current));
    }
}

/// <summary>
/// SwiftUI <c>Binding&lt;Value&gt;</c> (<c>@Binding var x</c>, <c>$state</c>, <c>$model.property</c> through <c>@Bindable</c>,
/// and the one first-class <c>ReferenceWritableKeyPath</c> at ChatView.swift:611). Reads go through the source
/// property, so a view reading <c>binding.Value</c> is tracked on the SOURCE, and a write runs the source's setter
/// and its didSet, exactly as SwiftUI forwards both.
/// </summary>
public sealed class Binding<T>(Func<T> get, Action<T> set)
{
    public T Value { get => get(); set => set(value); }

    /// <summary><c>.constant(value)</c>: writes are ignored.</summary>
    public static Binding<T> Constant(T value) => new(() => value, _ => { });
}
