// PORT-ONLY test stand-in, no Swift twin: the scripted, offline glab, gh and tea of the Services.Git.Git tests
// (R-TEST-8, R-TEST-10). It reaches no network and runs no real forge CLI. The test fixture
// (tests/Marrow.Services.Tests/Fixtures/ForgeFixtures.cs) compiles this file with the system's .NET Framework csc.exe
// and copies the result as glab.exe, gh.exe and tea.exe into a folder it puts first on the test process's PATH.
//
// Each run works in its working directory, which Shell.run(tool:) sets to the Git value's directory:
//   .git/forge-stand-in/call-<n>.info   exe=<file name>, cwd=<dir>, stdin=<file type>, and the two git variables
//   .git/forge-stand-in/call-<n>.argv   every argument followed by a NUL (written last: its presence means the call
//                                       is fully recorded)
// and answers from the reply files the test wrote, for call n (0-based) or else the shared ones:
//   reply-<n>.out / reply.out     bytes written to stdout as they are
//   reply-<n>.err / reply.err     bytes written to stderr as they are
//   reply-<n>.status / reply.status   the exit status (default 0), or "stall" to sleep for ten minutes
// Written for C# 5 (the Framework compiler), so no newer syntax.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class ForgeStandIn
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern int GetFileType(IntPtr handle);

    private static int Main(string[] args)
    {
        string folder = Path.Combine(Path.Combine(Environment.CurrentDirectory, ".git"), "forge-stand-in");
        Directory.CreateDirectory(folder);
        int call = 0;
        while (File.Exists(Path.Combine(folder, "call-" + call + ".argv"))) call++;

        int type = GetFileType(GetStdHandle(-10));
        string stdin = type == 2 ? "char" : type == 3 ? "pipe" : type == 1 ? "disk" : "other-" + type;
        StringBuilder info = new StringBuilder();
        // The image's name as it is on disk: the path a caller launched it by carries whatever case that caller's
        // PATHEXT spelled (".EXE", ED-171), and Windows file names are case-insensitive, so only the directory entry
        // says which of glab.exe, gh.exe and tea.exe ran.
        string image = System.Reflection.Assembly.GetEntryAssembly().Location;
        string exe = new DirectoryInfo(Path.GetDirectoryName(image)).GetFiles(Path.GetFileName(image))[0].Name;
        info.Append("exe=").Append(exe).Append('\n');
        info.Append("cwd=").Append(Environment.CurrentDirectory).Append('\n');
        info.Append("stdin=").Append(stdin).Append('\n');
        info.Append("GIT_TERMINAL_PROMPT=").Append(Environment.GetEnvironmentVariable("GIT_TERMINAL_PROMPT") ?? "<unset>").Append('\n');
        info.Append("GIT_OPTIONAL_LOCKS=").Append(Environment.GetEnvironmentVariable("GIT_OPTIONAL_LOCKS") ?? "<unset>").Append('\n');
        File.WriteAllBytes(Path.Combine(folder, "call-" + call + ".info"), new UTF8Encoding(false).GetBytes(info.ToString()));

        MemoryStream argv = new MemoryStream();
        foreach (string argument in args)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(argument);
            argv.Write(bytes, 0, bytes.Length);
            argv.WriteByte(0);
        }
        string pending = Path.Combine(folder, "call-" + call + ".argv.tmp");
        File.WriteAllBytes(pending, argv.ToArray());
        File.Move(pending, Path.Combine(folder, "call-" + call + ".argv"));

        byte[] stdout = Reply(folder, call, "out");
        byte[] stderr = Reply(folder, call, "err");
        byte[] statusBytes = Reply(folder, call, "status");
        string status = statusBytes == null ? "0" : Encoding.UTF8.GetString(statusBytes).Trim();
        if (stdout != null) { Stream o = Console.OpenStandardOutput(); o.Write(stdout, 0, stdout.Length); o.Flush(); }
        if (stderr != null) { Stream e = Console.OpenStandardError(); e.Write(stderr, 0, stderr.Length); e.Flush(); }
        if (status == "stall")
        {
            Thread.Sleep(TimeSpan.FromMinutes(10));
            return 0;
        }
        return int.Parse(status);
    }

    private static byte[] Reply(string folder, int call, string kind)
    {
        string own = Path.Combine(folder, "reply-" + call + "." + kind);
        if (File.Exists(own)) return File.ReadAllBytes(own);
        string shared = Path.Combine(folder, "reply." + kind);
        if (File.Exists(shared)) return File.ReadAllBytes(shared);
        return null;
    }
}
