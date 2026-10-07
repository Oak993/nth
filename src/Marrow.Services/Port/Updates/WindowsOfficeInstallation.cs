// PORT-ONLY: UE-3; read only the Click-to-Run version and update-channel values used by trial H.
using Microsoft.Win32;

namespace Marrow.Services.Port.Updates;

public static class WindowsOfficeInstallation
{
    public static string ClientPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Microsoft Shared", "ClickToRun", "OfficeC2RClient.exe");

    public static Task<OfficeInstallation?> Read(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var configuration = machine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration", writable: false);
            if (configuration?.GetValue("VersionToReport") is not string version || string.IsNullOrWhiteSpace(version)) continue;
            var channel = configuration.GetValue("UpdateChannel") as string ?? configuration.GetValue("CDNBaseUrl") as string;
            return Task.FromResult<OfficeInstallation?>(new(version, channel, ClientPath, File.Exists(ClientPath)));
        }
        return Task.FromResult<OfficeInstallation?>(null);
    }
}
