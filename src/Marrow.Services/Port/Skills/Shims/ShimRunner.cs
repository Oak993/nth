using System.Diagnostics;

namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6 and DL-6. Inherit handles directly: no decoding, re-encoding or pipe pump.
public static class ShimRunner
{
    public static async Task<int> RunAsync(ShimDefinition definition, IReadOnlyList<string> arguments, IShimLifetime lifetime)
    {
        lifetime.Arm();
        PythonShimRun? python = null;
        string? profile = null;
        try
        {
            var start = new ProcessStartInfo(definition.Target) { UseShellExecute = false };
            foreach (var item in definition.Environment) start.Environment[item.Key] = item.Value;
            if (definition.PythonPathFile != null)
            {
                python = PythonShimRun.Prepare(definition.Target, definition.PythonPathFile, definition.PackagePaths,
                    definition.WorkDirectory ?? throw new InvalidDataException("WorkDirectory"));
                start.FileName = python.Target;
                start.Environment["PYTHONUTF8"] = "1";
            }
            if (definition.NodePath != null) start.Environment["NODE_PATH"] = definition.NodePath;
            foreach (var arg in definition.PrefixArguments) start.ArgumentList.Add(arg);
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            if (definition.LibreOffice)
            {
                profile = Path.Combine(definition.WorkDirectory ?? throw new InvalidDataException("WorkDirectory"), "lo-profile-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(profile);
                start.ArgumentList.Add("-env:UserInstallation=" + new Uri(Path.GetFullPath(profile)).AbsoluteUri);
            }
            using var child = Process.Start(start) ?? throw new InvalidOperationException(definition.Target);
            await child.WaitForExitAsync().ConfigureAwait(false);
            return child.ExitCode;
        }
        finally
        {
            python?.Dispose();
            if (profile != null) Directory.Delete(profile, true);
        }
    }
}
