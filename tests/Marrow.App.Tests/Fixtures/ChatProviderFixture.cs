using Marrow.Core.Support;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: compile the repository's generic provider stand-in; never resolve an installed AI executable.
internal static class ChatProviderFixture
{
    private static readonly Lazy<string> compiled = new(Compile);

    public static string Create(string directory, string output, int exit = 0)
    {
        var path = Path.Combine(directory, "chat-provider-stand-in.exe");
        File.Copy(compiled.Value, path);
        File.WriteAllText(Path.Combine(directory, "stdout.txt"), output);
        File.WriteAllText(Path.Combine(directory, "exit.txt"), exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(directory, "sleep-ms.txt"), "100");
        return path;
    }

    private static string Compile()
    {
        string? source = null;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "provider-probe-cli", "ProviderProbeStandIn.cs");
            if (File.Exists(candidate)) { source = candidate; break; }
        }
        Assert.NotNull(source);
        var root = Marrow.Testing.TestDataDirectory.Read()!;
        var output = Path.Combine(root, "chat-provider-stand-in.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var result = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: root).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.FailureMessage);
        return output;
    }
}
