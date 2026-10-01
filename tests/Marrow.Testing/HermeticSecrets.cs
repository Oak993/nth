using System.Collections;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY test guard (R-TEST-10). Removes every provider secret from the test process's environment before any test
/// runs, whatever started the process (one.sh clears 19 known keys, but a bare `dotnet test` does not: 2026-10-01 a
/// test run that way reached the real Z.ai API with the owner's ZAI_API_KEY). Every variable whose name ends in
/// _API_KEY, _AUTH_TOKEN, _ACCESS_TOKEN or _SECRET is removed. Each test assembly calls <see cref="Clear"/> from its
/// own module initializer, so it runs as the assembly loads.
/// </summary>
public static class HermeticSecrets
{
    private static readonly string[] Suffixes = ["_API_KEY", "_AUTH_TOKEN", "_ACCESS_TOKEN", "_SECRET"];

    /// <summary>The names removed by the last <see cref="Clear"/> (for the guard's own test).</summary>
    public static IReadOnlyList<string> Removed { get; private set; } = [];

    public static void Clear()
    {
        var removed = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!IsSecret(name)) continue;
            Environment.SetEnvironmentVariable(name, null);
            removed.Add(name);
        }
        Removed = removed;
    }

    public static bool IsSecret(string name) =>
        Suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
