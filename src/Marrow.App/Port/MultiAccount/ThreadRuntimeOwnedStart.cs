using Marrow.Services.Providers;
using Marrow.Core.Models;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private static bool SameResumeIdentity(ProviderKind provider, string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal)
        || Guid.TryParse(expected, out var first) && Guid.TryParse(actual, out var second) && first == second
        || provider == ProviderKind.Pi && Path.IsPathFullyQualified(expected) && Path.IsPathFullyQualified(actual)
            && string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task<string> StartOwnedSessionAsync(ProviderSession candidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = candidate.Start();
        try { return await start.WaitAsync(ct); }
        catch
        {
            if (!start.IsCompleted) _ = StopLateOwnedStartAsync(start, candidate);
            throw;
        }
    }

    private static async Task StopLateOwnedStartAsync(Task<string> start, ProviderSession candidate)
    {
        try { await start; }
        catch { /* A failed late start still releases its partially connected provider. */ }
        finally { candidate.OnEvent = null; candidate.Stop(); }
    }
}
