namespace Marrow.Testing;

// PORT-ONLY, no Swift twin (R-TEST-10; ED-211). The folders a test may launch a provider CLI from: this repository
// (the stand-ins under tests/fixtures and their build outputs), the process temp folder and MARROW_PORT_DATA_DIR
// (the scratch folders tests compile or copy stand-ins into). Each test assembly that can reach ProviderRegistry hands
// this list to ProviderRegistry.ExecutableRootsForTesting from a module initializer, so a provider executable found
// anywhere else (a real installed claude, codex, copilot, ... under the user's profile) is treated as not installed.

/// <summary>The allowed provider-CLI roots for a test process.</summary>
public static class ProviderCliRoots
{
    /// <summary>The repository root (the folder holding Marrow.sln), the temp folder and MARROW_PORT_DATA_DIR.</summary>
    public static IReadOnlyList<string> Compute()
    {
        var roots = new List<string> { Path.GetTempPath() };
        var dataDirectory = TestDataDirectory.Read();
        if (!string.IsNullOrEmpty(dataDirectory)) roots.Add(dataDirectory);
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Marrow.sln")))
            {
                roots.Add(folder.FullName);
                break;
            }
        }
        return roots;
    }
}
