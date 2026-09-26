using System.Text;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture. Small scripted "MCP servers" over stdio for MCPProbeTests: Windows system binaries only
// (powershell.exe running a script, R-TEST-10), never a real AI CLI. Each script speaks the same wire format
// MCPProbe.probeStdio does: one JSON object per line, terminated by 0x0A.
public static class ProbeProcessScripts
{
    public static string PowerShell => Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// The argv for powershell.exe running one script from a temp .ps1 file, with no extra positional arguments.
    /// A real file (not -EncodedCommand) keeps the process's own command line short regardless of the script's size,
    /// and -ExecutionPolicy Bypass lets it load under this machine's policy without changing that policy.
    /// </summary>
    public static IReadOnlyList<string> Args(string script) => Args(script, []);

    /// <summary>The same, with trailing positional arguments the script sees as its own $args (R-PROC-1: a real
    /// argument vector, so an element holding a space arrives as one element, never re-split).</summary>
    public static IReadOnlyList<string> Args(string script, IReadOnlyList<string> extra)
    {
        var path = Path.Combine(Path.GetTempPath(), "marrow-mcpprobe-test-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, script, Encoding.UTF8);
        return ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path, .. extra];
    }

    /// <summary>A minimal read loop every script below shares: one line in, dispatch on "method".</summary>
    private const string ReadLoop = """
        $ErrorActionPreference = 'Stop'
        while ($true) {
          $line = [Console]::In.ReadLine()
          if ($null -eq $line) { break }
          if ($line.Length -eq 0) { continue }
          $msg = $line | ConvertFrom-Json
        __BODY__
        }
        """;

    private static void WriteLine(StringBuilder into, string body) =>
        into.Append("    [Console]::Out.Write(").Append(body).Append(" + \"`n\"); [Console]::Out.Flush()\n");

    /// <summary>initialize's serverInfo.name is every element of $args joined by '|'; serverInfo.version is
    /// $env:MCP_PROBE_TEST_VAR; tools/list answers an empty page. Proves args arrive as a vector (R-PROC-1) and that
    /// server.env layers over LoginEnvironment.current (ED-128).</summary>
    public static string EchoArgsAndEnvAndCwd()
    {
        // One backslash and one double-quote character, built as single-char literals so the PowerShell
        // regex/replacement text below is unambiguous to read and verify: JSON-escape a value by first
        // doubling every backslash (regex pattern "one backslash" = two backslash chars; replacement "two
        // backslash chars" is written literally, PowerShell's -replace right side has no backslash meta
        // meaning), then escaping every quote as backslash-quote.
        const string bs = "\\";
        const string dq = "\"";
        var body = new StringBuilder();
        body.Append("  if ($msg.method -eq 'initialize') {\n");
        body.Append("    $joinedArgs = (($args | ForEach-Object { $_ -replace '" + bs + bs + "', '" + bs + bs + "' -replace '" + dq + "', '" + bs + dq + "' }) -join '|')\n");
        body.Append("    $var = [string]$env:MCP_PROBE_TEST_VAR\n");
        body.Append("    $cwd = ((Get-Location).Path) -replace '" + bs + bs + "', '" + bs + bs + "'\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"serverInfo\":{\"name\":\"' + $joinedArgs + '|' + $var + '\",\"version\":\"' + $cwd + '\"}}}'");
        body.Append("  } elseif ($msg.method -eq 'tools/list') {\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"tools\":[]}}'");
        body.Append("  }\n");
        return ReadLoop.Replace("__BODY__", body.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Every tools/list reply carries a nextCursor one higher than the one it received (or "1" for the first
    /// page), forever: proves the probe stops itself at 10 pages (PR-MCP-108) instead of trusting the server.</summary>
    public static string EndlessPages()
    {
        var body = new StringBuilder();
        body.Append("  if ($msg.method -eq 'initialize') {\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"serverInfo\":{\"name\":\"Endless\",\"version\":\"1\"}}}'");
        body.Append("  } elseif ($msg.method -eq 'tools/list') {\n");
        body.Append("    $cursor = [string]$msg.params.cursor\n");
        body.Append("    $next = if ($cursor -eq '') { 1 } else { [int]$cursor + 1 }\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"tools\":[{\"name\":\"t' + $next + '\"}],\"nextCursor\":\"' + $next + '\"}}'");
        body.Append("  }\n");
        return ReadLoop.Replace("__BODY__", body.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Never answers initialize: writes the given stderr lines (joined by a real LF; a single-quoted
    /// PowerShell string needs no backtick escaping for a literal newline), then exits with the given code.</summary>
    public static string ExitsWithStderr(int code, params string[] stderrLines)
    {
        var escaped = string.Join("\n", stderrLines.Select(l => l.Replace("'", "''", StringComparison.Ordinal)));
        return $"[Console]::Error.Write('{escaped}' + \"`n\"); [Console]::Error.Flush(); exit {code}";
    }

    /// <summary>Writes 70,000 'x' bytes (well past the 64,000-byte stderr cap) generated inside the script itself,
    /// so the process's own command line stays short, then the given final line, then exits with the given code.</summary>
    public static string ExitsWithOversizedStderr(int code, string finalLine)
    {
        var escapedFinal = finalLine.Replace("'", "''", StringComparison.Ordinal);
        return "$filler = 'x' * 70000\n" +
               $"[Console]::Error.Write($filler + \"`n\" + '{escapedFinal}' + \"`n\"); [Console]::Error.Flush(); exit {code}";
    }

    /// <summary>Writes "Unauthorized" to stderr, then more than 4,000 bytes of filler generated inside the script
    /// itself, then exits with the given code: proves errorTail's last-4,000-byte window (PR-MCP-110, PR-MCP-236)
    /// no longer carries a marker written before it, even though the full stderr buffer still does.</summary>
    public static string ExitsWithStaleUnauthorizedMarker(int code)
    {
        return "[Console]::Error.Write('Unauthorized' + \"`n\"); [Console]::Error.Flush()\n" +
               "$filler = 'y' * 4500\n" +
               $"[Console]::Error.Write($filler); [Console]::Error.Flush(); exit {code}";
    }

    /// <summary>Answers initialize with a stray reply for id 999 (an id nobody is waiting on) immediately before
    /// the real reply for the request's own id, both on the same read: proves MCPStdioConnection.Handle buffers a
    /// reply nobody is waiting on rather than discarding it, and the awaited reply still arrives (PR-MCP-109).
    /// serverInfo.name is "real" and .version is "v1" so the test can tell the real reply from the stray one.</summary>
    public static string EchoWithBufferedExtraReply()
    {
        var body = new StringBuilder();
        body.Append("  if ($msg.method -eq 'initialize') {\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":999,\"result\":{\"serverInfo\":{\"name\":\"stray\",\"version\":\"stray\"}}}'");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"serverInfo\":{\"name\":\"real\",\"version\":\"v1\"}}}'");
        body.Append("  } elseif ($msg.method -eq 'tools/list') {\n");
        WriteLine(body, "'{\"jsonrpc\":\"2.0\",\"id\":' + $msg.id + ',\"result\":{\"tools\":[]}}'");
        body.Append("  }\n");
        return ReadLoop.Replace("__BODY__", body.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Answers initialize normally (LF-terminated), then writes the tools/list reply with NO trailing
    /// newline before the process exits: the reply can only reach the probe through the child's end-of-stream
    /// flush (StdioProcess.swift Deliver/FinishOutput), not a 0x0A. Does not use the shared ReadLoop/WriteLine
    /// helpers, which always append a newline.</summary>
    public static string EchoThenOmitFinalNewlineBeforeExit() => """
        $ErrorActionPreference = 'Stop'
        while ($true) {
          $line = [Console]::In.ReadLine()
          if ($null -eq $line) { break }
          if ($line.Length -eq 0) { continue }
          $msg = $line | ConvertFrom-Json
          if ($msg.method -eq 'initialize') {
            [Console]::Out.Write('{"jsonrpc":"2.0","id":' + $msg.id + ',"result":{"serverInfo":{"name":"n","version":"v"}}}' + "`n")
            [Console]::Out.Flush()
          } elseif ($msg.method -eq 'tools/list') {
            [Console]::Out.Write('{"jsonrpc":"2.0","id":' + $msg.id + ',"result":{"tools":[]}}')
            [Console]::Out.Flush()
            exit 0
          }
        }
        """;
}
