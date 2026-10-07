// PORT-ONLY: OD-0116, ED-439. Remembers, per Pi executable build, that history injection through the load extension could not be proven, so the host knows before it asks anything.
using System.Collections.Concurrent;

namespace Marrow.Services.Providers;

internal static class PiHistoryInjection
{
    private static readonly ConcurrentDictionary<string, bool> Unavailable = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(Marrow.Core.Port.SwiftURL executable)
    {
        var info = new FileInfo(executable.Path);
        return info.Exists ? executable.Path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : executable.Path + "|missing";
    }

    public static bool IsUnavailable(Marrow.Core.Port.SwiftURL executable) => Unavailable.ContainsKey(Key(executable));

    public static void MarkUnavailable(Marrow.Core.Port.SwiftURL executable) => Unavailable[Key(executable)] = true;
}
