using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6 and DL-6.
[Trait("Spec", "DL-6")]
public sealed class ShimPathTests
{
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void Insert_before_first_WindowsApps_preserves_every_user_entry_and_empty_entry()
    {
        const string original = "C:\\real-python;;C:\\Users\\u\\Microsoft\\WindowsApps;D:\\Microsoft\\WindowsApps;C:\\last";
        Assert.Equal("C:\\real-python;;C:\\addons\\bin;C:\\Users\\u\\Microsoft\\WindowsApps;D:\\Microsoft\\WindowsApps;C:\\last",
            ShimPath.Insert(original, "C:\\addons\\bin", ';'));
        Assert.Equal("C:\\one;C:\\addons\\bin", ShimPath.Insert("C:\\one", "C:\\addons\\bin", ';'));
        Assert.Equal("C:\\addons\\bin", ShimPath.Insert(null, "C:\\addons\\bin", ';'));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void WindowsApps_match_is_case_insensitive_handles_quotes_and_trailing_slash_and_not_similar_names()
    {
        Assert.Equal("C:\\Microsoft\\WindowsAppsExtra;bin;\"C:/MICROSOFT/WindowsApps/\";last",
            ShimPath.Insert("C:\\Microsoft\\WindowsAppsExtra;\"C:/MICROSOFT/WindowsApps/\";last", "bin", ';'));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void Empty_bin_exists_and_only_the_child_environment_changes()
    {
        using var fixture = new ShimFixtures();
        var parent = new Dictionary<string, string> { ["PATH"] = "real", ["keep"] = "value" };
        var child = ShimPath.ForChild(parent, fixture.Root);
        Assert.True(Directory.Exists(fixture.Bin));
        Assert.Empty(Directory.GetFiles(fixture.Bin));
        Assert.Equal("real" + Path.PathSeparator + fixture.Bin, child["PATH"]);
        Assert.Equal("real", parent["PATH"]);
        Assert.Equal("value", child["keep"]);
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void Real_python_wins_but_Marrow_shadows_the_Store_alias()
    {
        using var fixture = new ShimFixtures();
        var real = Path.Combine(fixture.Root, "real");
        var store = Path.Combine(fixture.Root, "Microsoft", "WindowsApps");
        foreach (var folder in new[] { real, store, fixture.Bin })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "python.exe"), "fake");
        }
        string Resolve(string path) => path.Split(Path.PathSeparator).Select(p => Path.Combine(p, "python.exe")).First(File.Exists);
        Assert.Equal(Path.Combine(real, "python.exe"), Resolve(ShimPath.Insert(real + Path.PathSeparator + store, fixture.Bin, Path.PathSeparator)));
        Assert.Equal(Path.Combine(fixture.Bin, "python.exe"), Resolve(ShimPath.Insert(store, fixture.Bin, Path.PathSeparator)));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Running_fake_provider_finds_a_shim_added_after_launch_on_its_next_process()
    {
        using var fixture = new ShimFixtures();
        var start = new System.Diagnostics.ProcessStartInfo(fixture.Target)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("provider");
        start.Environment["PATH"] = ShimPath.ForChild(new Dictionary<string, string> { ["PATH"] = "" }, fixture.Root)["PATH"];
        start.Environment["MARROW_PORT_DATA_DIR"] = fixture.Root;
        using var provider = System.Diagnostics.Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await provider.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            await provider.StandardInput.WriteLineAsync("python" + ShimFixtures.Extension);
            await provider.StandardInput.FlushAsync();
            Assert.Equal("missing", await provider.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            fixture.Install("python", new ShimDefinition { Target = fixture.Target });
            await provider.StandardInput.WriteLineAsync("python" + ShimFixtures.Extension);
            await provider.StandardInput.FlushAsync();
            Assert.Equal("found", ShimFixtures.Decode((await provider.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)))!));
        }
        finally
        {
            provider.StandardInput.Close();
            await provider.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
    }
}
