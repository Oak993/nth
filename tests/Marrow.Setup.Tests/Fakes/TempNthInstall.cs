using System.ComponentModel;
using System.Diagnostics;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A temporary folder that stands in for an install root, with harmless programs in it that run until the test ends (nth installer PLAN.md 10,
/// unit U4): a copy of Windows' own ping.exe, renamed to what the test needs and left pinging the local machine. Nothing here is nth, and
/// ending a copy ends nothing else. Disposing it ends every program it started and deletes the folder.
/// <para>
/// A program that outlived its test must never hold up the test run, and the first version of this class let five do it: a test that wrote
/// <c>using var process = install.Start(...)</c> after <c>using var install = new TempNthInstall()</c> disposed its Process object first, and a
/// disposed Process no longer knows its id, so the install could not end it and the program ran on, sharing the test host's pipes, which kept
/// <c>dotnet test</c> waiting for the end of the run for as long as it lived. Now the install ends a program by its id and start time
/// whatever became of the test's Process object, all three standard streams are redirected so a program inherits no handle of the test host,
/// it stops by itself after ten minutes, and every program still running when the test process ends is ended then.
/// </para>
/// </summary>
public sealed class TempNthInstall : IDisposable
{
    private static readonly List<Started> Everything = [];

    static TempNthInstall()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (Everything)
            {
                foreach (var item in Everything) End(item);
            }
        };
    }

    private readonly List<Started> started = [];

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
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-n");
        info.ArgumentList.Add("600"); // about ten minutes, then it ends by itself
        info.ArgumentList.Add("127.0.0.1");
        var process = Process.Start(info) ?? throw new InvalidOperationException("The test program did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var item = new Started(process, process.Id, process.StartTime);
        lock (started) started.Add(item);
        lock (Everything) Everything.Add(item);
        return process;
    }

    // Ends the program by its id and start time: the test may have disposed its Process object already, and an id that now belongs to another program is left alone.
    private static void End(Started item)
    {
        try
        {
            using var live = Process.GetProcessById(item.Id);
            if (live.StartTime != item.StartTime) return;
            live.Kill();
            live.WaitForExit(5000);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        lock (started)
        {
            foreach (var item in started)
            {
                End(item);
                lock (Everything) Everything.Remove(item);
                item.Process.Dispose();
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

    private sealed record Started(Process Process, int Id, DateTime StartTime);
}
