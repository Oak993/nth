using System.Diagnostics;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A temporary folder that stands in for an install root, with harmless programs in it that run until the test ends (nth installer PLAN.md 10,
/// unit U4): a copy of Windows' own ping.exe, renamed to what the test needs and left pinging the local machine. Nothing here is nth, and
/// ending a copy ends nothing else. Disposing it ends every program it started and deletes the folder.
/// </summary>
public sealed class TempNthInstall : IDisposable
{
    private readonly List<Process> started = [];

    public TempNthInstall()
    {
        Root = Path.Combine(Path.GetTempPath(), "nth-process-test-" + Guid.NewGuid().ToString("N"), "Programs", "nth");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Copies ping.exe to <paramref name="relativeProgram"/> below the root (if it is not there yet) and starts it.</summary>
    public Process Start(string relativeProgram)
    {
        var program = Path.Combine(Root, relativeProgram);
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        if (!File.Exists(program)) File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), program);
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add("127.0.0.1");
        var process = Process.Start(info) ?? throw new InvalidOperationException("The test program did not start.");
        process.BeginOutputReadLine();
        lock (started) started.Add(process);
        return process;
    }

    public void Dispose()
    {
        lock (started)
        {
            foreach (var process in started)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
                process.Dispose();
            }
            started.Clear();
        }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Root))!, recursive: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100); // the program's file is released a moment after it ends
            }
        }
    }
}
