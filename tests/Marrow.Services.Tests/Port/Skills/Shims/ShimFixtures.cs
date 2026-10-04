using System.Diagnostics;
using System.Text;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: SK-6 disposable distribution and child processes.
internal sealed class ShimFixtures : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "marrow-sk6-" + Guid.NewGuid().ToString("N"));
    public string Bin => Path.Combine(Root, "addons", "bin");
    public string Work => Path.Combine(Root, "addons", "work");
    public string Target => Path.Combine(Root, "target", "Marrow.Shim.FakeTarget" + Extension);
    public static string Extension => OperatingSystem.IsWindows() ? ".exe" : "";
    public ShimFixtures()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "shim-fixture")))
            File.Copy(file, Path.Combine(Path.GetDirectoryName(Target)!, Path.GetFileName(file)));
    }
    public string Install(string name, ShimDefinition definition)
    {
        Directory.CreateDirectory(Bin);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "shim-launcher")))
            File.Copy(file, Path.Combine(Bin, Path.GetFileName(file)), true);
        var shim = Path.Combine(Bin, name + Extension);
        File.Copy(Path.Combine(Bin, "Marrow.Shim" + Extension), shim, true);
        ShimDefinition.Write(Path.ChangeExtension(shim, ".shim"), definition);
        return shim;
    }
    public Process Start(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["MARROW_PORT_DATA_DIR"] = Root;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    public async Task<string[]> Lines(string shim, params string[] args)
    {
        using var child = Start(shim, args);
        child.StandardInput.Close();
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("", await error);
        Assert.Equal(0, child.ExitCode);
        return (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
    }
    public static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    public void Dispose() => Directory.Delete(Root, true);
}
