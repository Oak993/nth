using System.Collections;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

// Helpers for ShellTests (Core.Support.Shell). Helper types live here, never inside a test class (R-TEST-3). The
// child processes are Windows PowerShell running one-line scripts (a system binary, R-TEST-10); nothing here starts
// an AI CLI.

/// <summary>Child scripts for Shell.run, passed with -EncodedCommand so they need no quoting.</summary>
public static class ShellScripts
{
    /// <summary>What the Ctrl+Break child writes from its handler when it exits on the event.</summary>
    public const string BreakSeen = "BREAK-SEEN";

    /// <summary>What the Ctrl+Break child writes from its handler when it ignores the event and keeps running.</summary>
    public const string BreakIgnored = "BREAK-IGNORED";

    /// <summary>
    /// The exit code the cooperating child leaves with on CTRL_BREAK_EVENT: 131, as in probe P6 and
    /// <see cref="ProcessScripts.BreakExitCode"/>.
    /// </summary>
    public const int BreakExitCode = 131;

    /// <summary>A script that writes <paramref name="bytes"/> to the named standard stream ("Output" or "Error").</summary>
    public static string Write(string stream, byte[] bytes) =>
        $"$s=[Console]::OpenStandard{stream}(); $b=[Convert]::FromBase64String('{Convert.ToBase64String(bytes)}'); " +
        "$s.Write($b,0,$b.Length); $s.Flush(); ";

    /// <summary>A script that writes the PowerShell string <paramref name="expression"/> as UTF-8 to standard output.</summary>
    public static string WriteText(string expression) =>
        $"$s=[Console]::OpenStandardOutput(); $b=[Text.Encoding]::UTF8.GetBytes({expression}); $s.Write($b,0,$b.Length); $s.Flush(); ";

    /// <summary>
    /// A script that writes <see cref="Pattern"/>(<paramref name="count"/>) to the named standard stream in writes of
    /// <paramref name="chunk"/> bytes, built in the child because the command line cannot carry that many bytes.
    /// </summary>
    public static string WritePattern(string stream, int count, int chunk) =>
        $"$s=[Console]::OpenStandard{stream}(); $b=New-Object byte[] {count}; for($i=0;$i -lt {count};$i++){{$b[$i]=$i%251}}; " +
        $"for($o=0;$o -lt {count};$o+={chunk}){{$n=[Math]::Min({chunk},{count}-$o); $s.Write($b,$o,$n)}}; $s.Flush(); ";

    /// <summary>A script that copies all of standard input to standard output, byte for byte.</summary>
    public const string Echo =
        "$i=[Console]::OpenStandardInput(); $m=New-Object IO.MemoryStream; $i.CopyTo($m); " +
        "$o=[Console]::OpenStandardOutput(); $b=$m.ToArray(); $o.Write($b,0,$b.Length); $o.Flush(); ";

    /// <summary>
    /// A child that registers a console control handler for CTRL_BREAK_EVENT (1), then creates
    /// <paramref name="armedFile"/> and sleeps 120 s. With <paramref name="exitsOnBreak"/> the handler writes
    /// <see cref="BreakSeen"/> and calls ExitProcess(131); otherwise it writes <see cref="BreakIgnored"/> and returns
    /// true, so the event is handled and the child keeps sleeping until it is killed. Handlers run last-registered
    /// first, so this one runs before powershell.exe's own (the shape of <see cref="ProcessScripts.CtrlBreakCooperating"/>).
    /// </summary>
    public static string BreakChild(bool exitsOnBreak, string armedFile) =>
        """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class MarrowShellBreakFixture
        {
            public delegate bool Handler(uint type);
            [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
            [DllImport("kernel32.dll")] private static extern void ExitProcess(uint code);
            private static Handler keep;
            private static bool exits;
            public static void Arm(bool exitsOnBreak)
            {
                exits = exitsOnBreak;
                keep = OnSignal;
                SetConsoleCtrlHandler(keep, true);
            }
            private static bool OnSignal(uint type)
            {
                if (type != 1) return false;
                Console.Out.Write(exits ? "BREAK-SEEN" : "BREAK-IGNORED");
                Console.Out.Flush();
                if (exits) ExitProcess(131);
                return true;
            }
        }
        '@
        """ +
        $"\n[MarrowShellBreakFixture]::Arm(${(exitsOnBreak ? "true" : "false")})\n" +
        $"[IO.File]::WriteAllText('{armedFile.Replace("'", "''", StringComparison.Ordinal)}', 'armed')\n" +
        "Start-Sleep 120\n";

    /// <summary>The argv powershell.exe takes to run <paramref name="script"/>.</summary>
    public static IReadOnlyList<string> Arguments(string script) => SystemTools.PowerShellEncoded(script);

    /// <summary>The test host's own environment as a case-insensitive dictionary (ED-128).</summary>
    public static Dictionary<string, string> HostEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Shell.run of a PowerShell script with the host's environment unless one is given.</summary>
    public static Task<ShellResult> Run(string script, string? directory = null, IReadOnlyDictionary<string, string>? environment = null,
        byte[]? input = null, int? outputLimit = null) =>
        Shell.Run(SystemTools.PowerShell, Arguments(script), directory, environment ?? HostEnvironment(), input, outputLimit: outputLimit);

    /// <summary>Bytes 0, 1, 2 ... wrapping at 251, so every window of the stream is distinct from its neighbours.</summary>
    public static byte[] Pattern(int count, int offset = 0)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++) bytes[i] = (byte)((i + offset) % 251);
        return bytes;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
