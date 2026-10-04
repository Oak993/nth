using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6 real executable names, including npm/npx batch entrypoints.
public sealed class ShimFilesTests
{
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void Native_executable_and_sidecar_are_written_under_the_provided_name()
    {
        using var fixture = new ShimFixtures();
        var source = Path.Combine(AppContext.BaseDirectory, "shim-launcher", "Marrow.Shim" + ShimFixtures.Extension);
        var target = ShimFiles.Write(fixture.Bin, "python.exe", source, new ShimDefinition { Target = fixture.Target });
        Assert.Equal(Path.Combine(fixture.Bin, "python.exe"), target);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        Assert.Equal(fixture.Target, ShimDefinition.Read(Path.Combine(fixture.Bin, "python.shim")).Target);
    }
    [WindowsShimFact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task Npm_and_npx_cmd_wrappers_dispatch_to_Node_with_prefix_arguments_and_preserve_exit()
    {
        using var fixture = new ShimFixtures();
        foreach (var name in new[] { "npm", "npx" })
        {
            var source = Path.Combine(AppContext.BaseDirectory, "shim-launcher", "Marrow.Shim.exe");
            var entry = ShimFiles.Write(fixture.Bin, name + ".cmd", source,
                new ShimDefinition { Target = fixture.Target, PrefixArguments = ["args", name + "-cli.js"], NodePath = "package-set" });
            // JIT test launcher companions only; a published NativeAOT launcher needs none.
            foreach (var companion in Directory.GetFiles(Path.GetDirectoryName(source)!))
                File.Copy(companion, Path.Combine(fixture.Bin, ".launchers", Path.GetFileName(companion)), true);
            using var child = fixture.Start("cmd.exe", "/d", "/s", "/c", "\"\"" + entry + "\" \"two words\" \"a\\\"quote\" \"مرحبا 日本語\"\"");
            child.StandardInput.Close();
            var output = await child.StandardOutput.ReadToEndAsync();
            var error = await child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("", error);
            Assert.Equal(0, child.ExitCode);
            Assert.Equal(new[] { name + "-cli.js", "two words", "a\"quote", "مرحبا 日本語" }, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => ShimFixtures.Decode(l.TrimEnd('\r'))));
            // The fixture's echo mode returns 37; wrapper must retain that code as well.
            ShimDefinition.Write(Path.Combine(fixture.Bin, ".launchers", name + ".shim"), new ShimDefinition { Target = fixture.Target });
            using var echo = fixture.Start("cmd.exe", "/d", "/s", "/c", "\"\"" + entry + "\" echo\"");
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            var outTask = echo.StandardOutput.BaseStream.CopyToAsync(stdout);
            var errTask = echo.StandardError.BaseStream.CopyToAsync(stderr);
            byte[] input = [0, 255, 128, 13, 10];
            await echo.StandardInput.BaseStream.WriteAsync(input);
            echo.StandardInput.Close();
            await echo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAll(outTask, errTask);
            Assert.Equal(input, stdout.ToArray());
            Assert.Equal(new byte[] { 0, 255, 10, 13, 128 }, stderr.ToArray());
            Assert.Equal(37, echo.ExitCode);
            ShimDefinition.Write(Path.Combine(fixture.Bin, ".launchers", name + ".shim"),
                new ShimDefinition { Target = fixture.Target, PrefixArguments = ["env", "NODE_PATH"], NodePath = "package-set" });
            using var env = fixture.Start("cmd.exe", "/d", "/s", "/c", "\"\"" + entry + "\"\"");
            env.StandardInput.Close();
            var nodePath = (await env.StandardOutput.ReadToEndAsync()).TrimEnd('\r', '\n');
            await env.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("package-set", ShimFixtures.Decode(nodePath));
            Assert.Equal(0, env.ExitCode);

        }
    }
}
