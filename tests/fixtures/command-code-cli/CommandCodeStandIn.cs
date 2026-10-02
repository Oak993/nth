// PORT-ONLY test stand-in, no Swift twin: an offline, scripted stand-in for the Command Code CLI (`cmd`) used by the
// Services.Providers.CommandCodeAPI tests of authStatus and listModels (R-TEST-8, R-TEST-10). It reaches no network
// and runs no real AI CLI. Compiled with the system's .NET Framework csc.exe
// (tests/Marrow.Services.Tests/Fixtures/CommandCodeStandIns.cs), so it is a real .exe: Shell.run spawns the given
// executable with CreateProcessW directly (R-PROC-1), so a .cmd or .ps1 script cannot stand in for it.
//
// Controlled by COMMAND_CODE_STANDIN_DIR (an existing folder a test creates), which holds:
//   argv-<n>.txt     written by the stand-in itself: every argument of call n (0-based), NUL-separated.
//   stdout.bin       the bytes written to stdout (missing: nothing).
//   stderr.bin       the bytes written to stderr (missing: nothing).
//   exit-status.txt  the exit code (missing: 0).
// Session-turn controls (PORT-ONLY, for the CommandCodeSession tests; all absent by default, which leaves the behaviour above untouched):
//   cwd-<n>.txt      written by the stand-in itself: the working directory call n was started in.
//   capture-stdin.txt  when present, the stand-in reads its stdin to the END (so it only proceeds once the client has closed
//                    the pipe) and writes the exact bytes to stdin-<n>.bin, then writes stdin-closed-<n>.txt. Without this
//                    file stdin is never read. A run that never sees stdin close gives up after 30 real seconds.
//   hold.txt         when present, after writing its output the stand-in waits until release.txt exists (or 60 real seconds
//                    pass) before exiting, so a test can inspect a turn that is still running.
// Without COMMAND_CODE_STANDIN_DIR set, the run does nothing and exits 0.
using System;
using System.IO;
using System.Text;

internal static class CommandCodeStandIn
{
    private static int Main(string[] args)
    {
        string dir = Environment.GetEnvironmentVariable("COMMAND_CODE_STANDIN_DIR");
        if (string.IsNullOrEmpty(dir)) return 0;
        Directory.CreateDirectory(dir);

        int call = 0;
        while (File.Exists(Path.Combine(dir, "argv-" + call + ".txt"))) call++;
        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        string pendingArgv = Path.Combine(dir, "argv-" + call + ".txt.tmp");
        File.WriteAllBytes(pendingArgv, argv.ToArray());
        File.Move(pendingArgv, Path.Combine(dir, "argv-" + call + ".txt"));

        File.WriteAllText(Path.Combine(dir, "cwd-" + call + ".txt"), Directory.GetCurrentDirectory(), new UTF8Encoding(false));

        if (File.Exists(Path.Combine(dir, "capture-stdin.txt")))
        {
            MemoryStream captured = new MemoryStream();
            System.Threading.Thread reader = new System.Threading.Thread(delegate ()
            {
                Stream stdin = Console.OpenStandardInput();
                byte[] buffer = new byte[4096];
                int read;
                while ((read = stdin.Read(buffer, 0, buffer.Length)) > 0) captured.Write(buffer, 0, read);
            });
            reader.IsBackground = true;
            reader.Start();
            if (!reader.Join(30000)) return 99;
            string pendingStdin = Path.Combine(dir, "stdin-" + call + ".bin.tmp");
            File.WriteAllBytes(pendingStdin, captured.ToArray());
            File.Move(pendingStdin, Path.Combine(dir, "stdin-" + call + ".bin"));
            File.WriteAllText(Path.Combine(dir, "stdin-closed-" + call + ".txt"), "1");
        }

        string stdoutPath = Path.Combine(dir, "stdout.bin");
        if (File.Exists(stdoutPath))
        {
            Stream stdout = Console.OpenStandardOutput();
            byte[] bytes = File.ReadAllBytes(stdoutPath);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
        string stderrPath = Path.Combine(dir, "stderr.bin");
        if (File.Exists(stderrPath))
        {
            Stream stderr = Console.OpenStandardError();
            byte[] bytes = File.ReadAllBytes(stderrPath);
            stderr.Write(bytes, 0, bytes.Length);
            stderr.Flush();
        }
        if (File.Exists(Path.Combine(dir, "hold.txt")))
        {
            DateTime holdDeadline = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(Path.Combine(dir, "release.txt")) && DateTime.UtcNow < holdDeadline) System.Threading.Thread.Sleep(20);
        }
        string statusPath = Path.Combine(dir, "exit-status.txt");
        if (File.Exists(statusPath))
        {
            return int.Parse(File.ReadAllText(statusPath).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        return 0;
    }
}
