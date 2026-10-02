using System.Collections;
using System.Reflection;
using Avalonia.Media;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>Puts an icon straight into the favicon cache's memory (no fetch, no network), and takes it out again.
/// The cache is process-wide, so a test seeds a host no other test uses and removes it in a finally block.</summary>
public static class FaviconCacheSeed
{
    private static IDictionary Memory() =>
        (IDictionary)typeof(FaviconCache).GetField("memory", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    public static void Seed(string host, IImage icon) => Memory()[host] = icon;

    public static void Remove(string host) => Memory().Remove(host);
}
