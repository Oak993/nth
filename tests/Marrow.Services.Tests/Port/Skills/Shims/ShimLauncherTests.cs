using System.Diagnostics;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6, DL-6; real launcher, built fake targets only.
[Trait("Spec", "DL-6")]
public sealed class ShimLauncherTests
{
    internal static readonly string[] Arguments = ["", "two words", "a\"quote", "trailing\\", "\\\"", "مرحبا 日本語 🦴", "&|<>^%$`!;()"];

    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Arguments_with_spaces_quotes_empty_strings_and_Unicode_are_unchanged()
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("qpdf", new ShimDefinition { Target = fixture.Target });
        var lines = await fixture.Lines(shim, ["args", .. Arguments]);
        // Empty argv is base64 empty: encode with a nonempty sentinel in a separate run below.
        Assert.Equal(Arguments.Skip(1), lines.Select(ShimFixtures.Decode));
        using var child = fixture.Start(shim, "args", "");
        child.StandardInput.Close();
        Assert.Equal(Environment.NewLine, await child.StandardOutput.ReadToEndAsync());
        await child.WaitForExitAsync();
        Assert.Equal(0, child.ExitCode);
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Binary_stdin_stdout_stderr_and_nonzero_exit_are_unchanged()
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("pandoc", new ShimDefinition { Target = fixture.Target });
        using var child = fixture.Start(shim, "echo");
        var bytes = Enumerable.Range(0, 262144).Select(i => (byte)(i % 256)).ToArray();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var outTask = child.StandardOutput.BaseStream.CopyToAsync(output);
        var errTask = child.StandardError.BaseStream.CopyToAsync(error);
        await child.StandardInput.BaseStream.WriteAsync(bytes);
        child.StandardInput.Close();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await Task.WhenAll(outTask, errTask);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(new byte[] { 0, 255, 10, 13, 128 }, error.ToArray());
        Assert.Equal(37, child.ExitCode);
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Python_UTF8_and_package_paths_reach_only_the_child_and_locked_target_is_unchanged()
    {
        using var fixture = new ShimFixtures();
        var targetDirectory = Path.GetDirectoryName(fixture.Target)!;
        var pth = Path.Combine(targetDirectory, "python314._pth");
        const string original = "python314.zip\n.\nLib/site-packages\nimport site\n";
        File.WriteAllText(pth, original);
        File.WriteAllText(Path.Combine(targetDirectory, "pip.ini"), "[global]\ntarget=agent-packages\n");
        var package = Path.Combine(fixture.Root, "package set 日本語");
        var agent = Path.Combine(fixture.Work, "python-agent", "3.14");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(agent);
        foreach (var file in Directory.GetFiles(targetDirectory)) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        var before = Directory.GetFiles(targetDirectory).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
        var parentUtf8 = Environment.GetEnvironmentVariable("PYTHONUTF8");
        var shim = fixture.Install("python", new ShimDefinition
        {
            Target = fixture.Target, PythonPathFile = pth, PackagePaths = [package, agent], WorkDirectory = fixture.Work
        });
        Assert.Equal("1", ShimFixtures.Decode(Assert.Single(await fixture.Lines(shim, "env", "PYTHONUTF8"))));
        Assert.Equal(parentUtf8, Environment.GetEnvironmentVariable("PYTHONUTF8"));
        var childPaths = await fixture.Lines(shim, "python-paths");
        Assert.Equal("1", childPaths[0]);
        var pthEntries = ShimFixtures.Decode(childPaths[1]).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains(package, pthEntries);
        Assert.Contains(agent, pthEntries);
        // Per-call ._pth preparation is also inspected while the fake child is alive.
        using (var run = PythonShimRun.Prepare(fixture.Target, pth, [package, agent], fixture.Work))
        {
            var entries = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(run.Target)!, "python314._pth"));
            Assert.Contains(Path.Combine(targetDirectory, "python314.zip"), entries);
            Assert.Contains(targetDirectory, entries);
            Assert.Contains(package, entries);
            Assert.Contains(agent, entries);
            Assert.Equal("import site", entries[^1]);
            Assert.NotEqual(fixture.Target, run.Target);
            Assert.All(Directory.GetFiles(Path.GetDirectoryName(run.Target)!), file => Assert.False(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)));
            Assert.Equal("[global]\ntarget=agent-packages\n", File.ReadAllText(Path.Combine(Path.GetDirectoryName(run.Target)!, "pip.ini")));
        }
        Assert.Empty(Directory.GetDirectories(fixture.Work, "python-run-*"));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(targetDirectory).Select(Path.GetFileName).Order());
        foreach (var item in before)
        {
            Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(targetDirectory, item.Key!)));
            Assert.True(File.GetAttributes(Path.Combine(targetDirectory, item.Key!)).HasFlag(FileAttributes.ReadOnly));
        }
        // The original target is the user's tool in this fixture: no shim override.
        Assert.Equal(parentUtf8 ?? "<absent>", ShimFixtures.Decode(Assert.Single(await fixture.Lines(fixture.Target, "env", "PYTHONUTF8"))));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Node_PATH_is_set_only_for_the_child()
    {
        using var fixture = new ShimFixtures();
        var parent = Environment.GetEnvironmentVariable("NODE_PATH");
        var path = Path.Combine(fixture.Root, "node modules 日本語");
        var shim = fixture.Install("node", new ShimDefinition { Target = fixture.Target, NodePath = path });
        Assert.Equal(path, ShimFixtures.Decode(Assert.Single(await fixture.Lines(shim, "env", "NODE_PATH"))));
        Assert.Equal(parent, Environment.GetEnvironmentVariable("NODE_PATH"));
        Assert.Equal(parent ?? "<absent>", ShimFixtures.Decode(Assert.Single(await fixture.Lines(fixture.Target, "env", "NODE_PATH"))));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Concurrent_soffice_calls_get_distinct_profiles_and_delete_them_after_exit()
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("soffice", new ShimDefinition { Target = fixture.Target, LibreOffice = true, WorkDirectory = fixture.Work });
        var calls = await Task.WhenAll(fixture.Lines(shim, "profile"), fixture.Lines(shim, "profile"));
        var a = Assert.Single(calls[0]);
        var b = Assert.Single(calls[1]);
        Assert.NotEqual(a, b);
        Assert.StartsWith(Path.Combine(fixture.Work, "lo-profile-"), a, StringComparison.Ordinal);
        Assert.False(Directory.Exists(a));
        Assert.False(Directory.Exists(b));
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Soffice_profile_is_deleted_even_when_the_target_exits_with_failure()
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("soffice", new ShimDefinition { Target = fixture.Target, LibreOffice = true, WorkDirectory = fixture.Work });
        using var child = fixture.Start(shim, "profile", "fail");
        child.StandardInput.Close();
        var profile = (await child.StandardOutput.ReadToEndAsync()).TrimEnd('\r', '\n');
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(42, child.ExitCode);
        Assert.False(Directory.Exists(profile));
        Assert.Equal("", await child.StandardError.ReadToEndAsync());
    }
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Child_tree_lifetime_is_armed_before_start_through_the_seam()
    {
        using var fixture = new ShimFixtures();
        var marker = Path.Combine(fixture.Root, "armed");
        var lifetime = new RecordingShimLifetime(() => File.WriteAllText(marker, "armed"));
        Assert.Equal(0, await ShimRunner.RunAsync(new ShimDefinition { Target = fixture.Target }, ["armed", marker], lifetime));
        Assert.True(lifetime.Armed);
    }
}
