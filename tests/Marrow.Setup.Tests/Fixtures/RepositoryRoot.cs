namespace Marrow.Setup.Tests.Fixtures;

/// <summary>The checkout the tests run in (the folder that holds Marrow.sln), for tests that read source or script files (unit U10).</summary>
public static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }
}
