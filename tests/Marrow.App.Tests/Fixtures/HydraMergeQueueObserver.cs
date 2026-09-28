using System.Reflection;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: observe the EXISTING actor executor, never replace a product result. TaskHandle's envelope
// carries its Func<Task> body (ED-066); recognizing AutoMergeHydraWork's one spawned body distinguishes
// the resume sweep from disk saves and notifications, and measures its state at enqueue and execution.
public sealed class HydraMergeQueueObserver(IMainExecutor inner, Action<Func<Task>, bool> observe) : IMainExecutor
{
    public bool CheckAccess() => inner.CheckAccess();
    public void Enqueue(Action work)
    {
        var body = work.Target?.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.GetValue(work.Target)).OfType<Func<Task>>().FirstOrDefault();
        if (body is null || !body.Method.Name.Contains("<AutoMergeHydraWork>", StringComparison.Ordinal)) { inner.Enqueue(work); return; }
        observe(body, false);
        inner.Enqueue(() => { observe(body, true); work(); });
    }
}
