using System.Diagnostics;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class StdioProcessTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static StdioProcess Child(string script, StdioFramer.Framing framing = StdioFramer.Framing.Lines) =>
        new(SystemTools.PowerShell, SystemTools.PowerShellScript(script), Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase), framing);

    private static async Task<JSONValue[]> All(StdioProcess child)
    {
        var messages = new List<JSONValue>();
        await foreach (var message in child.Messages) messages.Add(message);
        return messages.ToArray();
    }

    [Fact]
    [Trait("Req", "PR-TERM-107")] // Core/Support/StdioProcess.swift:13-45
    [Trait("Req", "PR-TERM-108")] // Core/Support/StdioProcess.swift:55-87 (B3)
    [Trait("Req", "PR-PROVACP-162")] // Core/Support/StdioProcess.swift:80-87 (B3)
    public async Task Spawn_failure_finishes_messages_and_rethrows()
    {
        using var runtime = new TestRuntime();
        var child = new StdioProcess(Path.Join(Path.GetTempPath(), "missing-stdio-fixture.exe"), [],
            Environment.CurrentDirectory, new Dictionary<string, string>());
        Assert.ThrowsAny<Exception>(child.Start);
        Assert.Empty(await All(child).WaitAsync(Bound));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-155")] // Core/Support/StdioProcess.swift:147-152 (B3)
    [Trait("Req", "PR-PROVACP-156")] // Core/Support/StdioProcess.swift:50-53,159-166 (B3)
    [Trait("Req", "PR-PROVACP-158")] // Core/Support/StdioProcess.swift:154-157 (B3)
    [Trait("Req", "PR-PROVACP-159")] // Core/Support/StdioProcess.swift:168-183 (B3)
    [Trait("Req", "PR-PROVACP-160")] // Core/Support/StdioProcess.swift:56-78,90-104,142-152 (B3)
    [Trait("Req", "PR-PROVACP-163")] // Core/Support/StdioProcess.swift:69-76 (B3)
    [Trait("Req", "PR-TERM-112")] // Core/Support/StdioProcess.swift:132-140,168-175 (B3)
    [Trait("Req", "PR-TERM-113")] // Core/Support/StdioProcess.swift:147-152 (B3)
    [Trait("Req", "PR-TERM-114")] // Core/Support/StdioProcess.swift:154-157,177-182 (B3)
    [Trait("Req", "PR-TERM-115")] // Core/Support/StdioProcess.swift:50-53,159-166 (B3)
    public async Task Stdout_drops_noise_and_flushes_a_final_json_frame_while_stderr_stays_raw()
    {
        using var runtime = new TestRuntime();
        var output = Convert.ToBase64String(Encoding.UTF8.GetBytes("banner\n{\"ok\":1}  \r\n{\"last\":2}"));
        var error = Convert.ToBase64String(Encoding.UTF8.GetBytes("err\r\n"));
        var child = Child("$o=[Console]::OpenStandardOutput(); " +
            $"$b=[Convert]::FromBase64String('{output}'); $o.Write($b,0,$b.Length); $o.Flush(); " +
            $"$e=[Console]::OpenStandardError(); $v=[Convert]::FromBase64String('{error}'); $e.Write($v,0,$v.Length); $e.Flush(); exit 7");
        child.Start();
        var messages = await All(child).WaitAsync(Bound);
        Assert.Equal(2, messages.Length);
        Assert.Equal(1, messages[0]["ok"]?.JSONValueInt);
        Assert.Equal(2, messages[1]["last"]?.JSONValueInt);
        Assert.Equal(7, await child.WaitForExit().WaitAsync(Bound));
        Assert.Equal(7, await child.WaitForExit().WaitAsync(Bound));
        Assert.Equal("err\r\n", child.ErrorTail);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-154")] // Core/Support/StdioProcess.swift:90-104 (B3)
    [Trait("Req", "PR-PROVACP-161")] // Core/Support/StdioProcess.swift:106-140 (B3)
    [Trait("Req", "PR-TERM-109")] // Core/Support/StdioProcess.swift:90-104 (B3)
    [Trait("Req", "PR-TERM-110")] // Core/Support/StdioProcess.swift:106-118 (B3)
    public async Task Send_and_raw_write_keep_order_and_close_input_after_the_bytes()
    {
        using var runtime = new TestRuntime();
        var child = Child("$i=[Console]::In; $o=[Console]::Out; while(($line=$i.ReadLine()) -ne $null){$o.WriteLine($line)}");
        child.Start();
        child.Send(new JSONValue.String("https://example.com"));
        child.Write(Encoding.UTF8.GetBytes("42\n"));
        child.CloseInput();
        var messages = await All(child).WaitAsync(Bound);
        Assert.Equal("https://example.com", messages[0].JSONValueString);
        Assert.Equal(42, messages[1].JSONValueInt);
        Assert.Equal(0, await child.WaitForExit().WaitAsync(Bound));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-160")] // Core/Support/StdioProcess.swift:56-78,90-104,142-152 (B3)
    [Trait("Req", "PR-TERM-109")] // Core/Support/StdioProcess.swift:90-104 (B3)
    public async Task Back_to_back_sends_each_get_their_reply_in_order_over_fifty_rounds()
    {
        // ClaudeSession.Interrupt's wire shape (ClaudeSession.swift:238-247): a line that gets no reply (the tool
        // deny), then with no await between, requests whose replies are awaited. The child answers only lines
        // holding "ask", each flushed at once, so every reply the test waits for was really written.
        using var runtime = new TestRuntime();
        var child = Child("$i=[Console]::In; $o=[Console]::Out; " +
            "while(($l=$i.ReadLine()) -ne $null){ if($l.Contains('ask')){ $o.WriteLine($l); $o.Flush() } }");
        child.Start();
        await using var reader = child.Messages.GetAsyncEnumerator();
        for (var round = 0; round < 50; round++)
        {
            child.Send(new JSONValue.String($"note {round}"));
            child.Send(new JSONValue.String($"ask {round} a"));
            child.Send(new JSONValue.String($"ask {round} b"));
            Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Bound));
            Assert.Equal($"ask {round} a", reader.Current.JSONValueString);
            Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Bound));
            Assert.Equal($"ask {round} b", reader.Current.JSONValueString);
        }
        child.CloseInput();
        Assert.False(await reader.MoveNextAsync().AsTask().WaitAsync(Bound));
        Assert.Equal(0, await child.WaitForExit().WaitAsync(Bound));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-157")] // Core/Support/StdioProcess.swift:120-129 (B3)
    [Trait("Req", "PR-TERM-111")] // Core/Support/StdioProcess.swift:120-129 (B3)
    public async Task Terminate_closes_input_and_stops_the_child()
    {
        using var runtime = new TestRuntime();
        var child = Child("[Console]::In.ReadToEnd() | Out-Null; exit 0");
        child.Start();
        child.Terminate();
        _ = await child.WaitForExit().WaitAsync(Bound);
        Assert.False(child.IsRunning);
    }

    [Fact]
    [Trait("Decision", "ED-194")] // Core/Support/StdioProcess.swift:48 (B3): the new processIdentifier member
    public async Task ProcessIdentifier_is_the_pid_the_child_itself_reports()
    {
        using var runtime = new TestRuntime();
        var child = Child("[Console]::Out.WriteLine('{\"pid\":' + $PID + '}'); [Console]::Out.Flush(); [Console]::In.ReadToEnd() | Out-Null");
        child.Start();
        await using var reader = child.Messages.GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Bound));
        Assert.True(child.ProcessIdentifier > 0);
        Assert.Equal((long)child.ProcessIdentifier, reader.Current["pid"]?.JSONValueInt);
        child.Terminate();
        _ = await child.WaitForExit().WaitAsync(Bound);
    }

    [Fact]
    [Trait("Decision", "ED-194")] // Core/Support/StdioProcess.swift:48 (B3): the job object at spawn, no limits, no
                                  // KILL_ON_JOB_CLOSE (ProcessLauncher.cs); stop keeps the ED-126 ladder unchanged.
    public async Task ProcessControl_KillTree_reaches_a_grandchild_that_the_ED_126_ladder_would_spare()
    {
        using var runtime = new TestRuntime();
        var child = Child(
            "$p = Start-Process -FilePath powershell.exe -ArgumentList " +
            "'-NoProfile','-NonInteractive','-Command','Start-Sleep 60' -NoNewWindow -PassThru; " +
            "[Console]::Out.WriteLine('{\"grandchild\":' + $p.Id + '}'); [Console]::Out.Flush(); Start-Sleep 60");
        child.Start();
        await using var reader = child.Messages.GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Bound));
        var grandchildPid = checked((int)(reader.Current["grandchild"]?.JSONValueInt ?? 0));
        Assert.True(grandchildPid > 0);
        var grandchild = Process.GetProcessById(grandchildPid);
        _ = grandchild.SafeHandle; // retain the native handle before exit, as ProcessLauncherSpawnTests does
        try
        {
            child.ProcessControl.KillTree();
            _ = await child.WaitForExit().WaitAsync(Bound);
            Assert.True(grandchild.WaitForExit(2_000), "the grandchild survived the job's tree kill");
        }
        finally
        {
            grandchild.Refresh();
            if (!grandchild.HasExited) grandchild.Kill();
        }
    }

    [Theory]
    [InlineData(StdioFramer.Framing.Lines)]
    [InlineData(StdioFramer.Framing.ContentLength)]
    [Trait("Req", "PR-PROVACP-154")] // Core/Support/StdioProcess.swift:90-104 (B3)
    [Trait("Req", "PR-TERM-109")] // Core/Support/StdioProcess.swift:90-104 (B3)
    public async Task Send_emits_the_exact_frame_bytes(StdioFramer.Framing framing)
    {
        using var runtime = new TestRuntime();
        var response = framing == StdioFramer.Framing.Lines
            ? "[Console]::WriteLine($j)"
            : "$o=[Console]::OpenStandardOutput(); $b=[Text.Encoding]::UTF8.GetBytes($j); " +
              "$h=[Text.Encoding]::UTF8.GetBytes('Content-Length: '+$b.Length+" +
              "[char]13+[char]10+[char]13+[char]10); $o.Write($h,0,$h.Length); $o.Write($b,0,$b.Length); $o.Flush()";
        var child = Child("$i=[Console]::OpenStandardInput(); $m=[IO.MemoryStream]::new(); $i.CopyTo($m); " +
            "$s=[Convert]::ToBase64String($m.ToArray()); $j='\"'+$s+'\"'; " + response, framing);
        child.Start();
        child.Send(new JSONValue.String("https://example.com"));
        child.CloseInput();
        var message = Assert.Single(await All(child).WaitAsync(Bound));
        var actual = Convert.FromBase64String(Assert.IsType<JSONValue.String>(message).Value);
        var body = Encoding.UTF8.GetBytes("\"https://example.com\"");
        byte[] expected = framing == StdioFramer.Framing.Lines
            ? [.. body, (byte)0x0A]
            : Encoding.UTF8.GetBytes($"Content-Length: {body.Length}\r\n\r\n\"https://example.com\"");
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-156")] // Core/Support/StdioProcess.swift:50-53,159-166 (B3)
    [Trait("Req", "PR-TERM-115")] // Core/Support/StdioProcess.swift:50-53,159-166 (B3)
    public async Task Stderr_tail_is_last_4000_bytes_after_64000_byte_buffer_trims_to_16000()
    {
        using var runtime = new TestRuntime();
        var child = Child("$e=[Console]::OpenStandardError(); $a=[byte[]]::new(65001); " +
            "for($n=0;$n -lt $a.Length;$n++){$a[$n]=[byte](65+($n%26))}; " +
            "$e.Write($a,0,$a.Length); $e.Flush(); exit 0");
        child.Start();
        _ = await All(child).WaitAsync(Bound);
        Assert.Equal(0, await child.WaitForExit().WaitAsync(Bound));
        var expected = new string(Enumerable.Range(61_001, 4_000).Select(n => (char)(65 + n % 26)).ToArray());
        Assert.Equal(expected, child.ErrorTail);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-159")] // Core/Support/StdioProcess.swift:168-183 (B3)
    [Trait("Req", "PR-TERM-112")] // Core/Support/StdioProcess.swift:132-140,168-175 (B3)
    [Trait("Req", "PR-TERM-114")] // Core/Support/StdioProcess.swift:177-182 (B3)
    public async Task Exit_resumes_waiters_before_the_one_second_forced_stream_finish()
    {
        using var runtime = new TestRuntime();
        var child = new StdioProcess("unused", [], Environment.CurrentDirectory, new Dictionary<string, string>());
        await using var reader = child.Messages.GetAsyncEnumerator();
        var next = reader.MoveNextAsync().AsTask();
        var waiter = child.WaitForExit();
        var didExit = typeof(StdioProcess).GetMethod("DidExit", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(didExit);
        didExit.Invoke(child, [17]);
        Assert.Equal(17, await waiter.WaitAsync(Bound));
        Assert.Equal(17, await child.WaitForExit().WaitAsync(Bound));
        runtime.AdvanceMilliseconds(999);
        Assert.False(next.IsCompleted);
        runtime.AdvanceMilliseconds(1);
        Assert.False(await next.WaitAsync(Bound));
    }
}
