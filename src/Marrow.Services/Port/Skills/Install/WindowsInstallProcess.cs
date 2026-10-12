// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>Opt-in executable allow-list by absolute path, length and hash. No implicit binding, shell, or PATH search.</summary>
public sealed class WindowsInstallProcess(IReadOnlyList<LocalExecutable> executables) : IInstallProcess
{
    public async Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installer executable execution is WINDOWS-ONLY.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(command.Executable) || !Path.IsPathFullyQualified(command.WorkingDirectory)) throw new InvalidDataException("Absolute process paths are required.");
        var pin = executables.SingleOrDefault(p => p.Path.Equals(command.Executable, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Installer executable is not allowed.");
        InstallHash.Verify(pin.Path, pin.Size, pin.Sha256);
        InstallPaths.NoReparseAncestors(command.WorkingDirectory);
        var start = new ProcessStartInfo(command.Executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = command.WorkingDirectory };
        if (command.QuotePropertyValues) start.Arguments = string.Join(' ', command.Arguments.Select(QuoteForWindowsInstaller));
        else foreach (var arg in command.Arguments) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw new IOException("Installer process did not start.");
        try { await child.WaitForExitAsync(cancellationToken); }
        catch { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); } throw; }
        return child.ExitCode;
    }

    // ED-323 (1): NAME=value becomes NAME="value"; every other argument keeps ArgumentList's quoting.
    internal static string QuoteForWindowsInstaller(string argument)
    {
        var property = Regex.Match(argument, @"\A([A-Z_][A-Z0-9_.]*)=(.*)\z", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!property.Success) return Quote(argument);
        var value = property.Groups[2].Value;
        if (value.Contains('"', StringComparison.Ordinal) || value.EndsWith('\\')) throw new InvalidDataException("A Windows Installer property value cannot be quoted safely.");
        return property.Groups[1].Value + "=\"" + value + "\"";
    }

    // The MSVC / CommandLineToArgvW rules that ArgumentList applies.
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return argument;
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { backslashes++; continue; }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
