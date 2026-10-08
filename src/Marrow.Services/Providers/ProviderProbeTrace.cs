using System.Diagnostics;
using Marrow.Core.Models;

namespace Marrow.Services.Providers;

// T44 opt-in diagnostics. Never records command arguments, credentials, account names, or probe output.
public static class ProviderProbeTrace
{
    public static Action<string, long, long, int, int>? Record;
    public static ProviderProbeWorkScope Begin(ProviderKind provider, string phase) => Volatile.Read(ref Record) is { } record
        ? new(record, $"Provider: {provider.Id} {phase}", Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId) : default;
}
