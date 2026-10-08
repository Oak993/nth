using Marrow.Core.Support;

namespace Marrow.App.Port;

// T44: diagnostics may discover installed providers, but never update them or resume copied repository work.
internal static class PerformanceProfileMaintenance
{
    internal static void Run(Action operation)
    {
        if (DevDataRoot.PerformanceRoot.Current.Length == 0) operation();
    }
    internal static Task Run(Func<Task> operation) =>
        DevDataRoot.PerformanceRoot.Current.Length == 0 ? operation() : Task.CompletedTask;
}
