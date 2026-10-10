using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Shell.swift:1-134 and :218-325: ShellResult, ShellError, Shell.run with its deadline and termination ladder, run(tool:),
// OnceContinuation and OutputCollector. Expectations come from the Swift lines cited on each test and from the records;
// the Windows ladder is ED-047 (a) as ED-126 applies it (CTRL_BREAK_EVENT to the child's group, TerminateJobObject one
// second later). Which exit status a killed child reports is an open owner question (ED-126), so no test pins it
// beyond "non-zero". Every test that runs Shell.run starts a TestRuntime, so the 120 s deadline, the 1 s kill and the
// 3 s drain move only when the test advances the manual clock (R-TEST-5).
public sealed class ShellTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static ShellResult Result(int status, string stdout = "", string stderr = "") =>
        new(status, ShellScripts.Utf8(stdout), ShellScripts.Utf8(stderr));

    private static bool Eventually(Func<bool> condition) => SpinWait.SpinUntil(condition, Bound);

    // A failed launch is a failure now, rather than a blind 60-second poll for an impossible signal.
    private static async Task WaitForArmed(Task pending, string armed)
    {
        Assert.True(Eventually(() => File.Exists(armed) || pending.IsCompleted), "the child never armed its Ctrl+Break handler");
        if (pending.IsCompleted) await pending;
        Assert.True(File.Exists(armed), "the child never armed its Ctrl+Break handler");
    }

    // ---- ShellResult and ShellError (:4-29) ----

    [Fact]
    [Trait("Req", "PR-GIT-006")] // Core/Support/Shell.swift:14-19
    [Trait("Req", "PR-TERM-082")] // Core/Support/Shell.swift:14-19
    public void FailureMessage_prefers_trimmed_stderr_then_trimmed_stdout_then_the_exit_code_sentence()
    {
        Assert.Equal("boom", Result(1, "out", " \n boom \r\n").FailureMessage);
        Assert.Equal("fatal: not a git repository\nsecond line",
            Result(128, "", "fatal: not a git repository\nsecond line\n").FailureMessage);
        // Whitespace-only stderr counts as empty (trimmed of whitespace AND newlines, Text.swift:132).
        Assert.Equal("result", Result(2, "\n result \n", "\t \r\n").FailureMessage);
        Assert.Equal("The command failed with exit code 3.", Result(3, " \n", "\n\t").FailureMessage);
        Assert.Equal("The command failed with exit code 1.", Result(1).FailureMessage);
        Assert.Equal("The command failed with exit code 0.", Result(0).FailureMessage);
        Assert.Equal("The command failed with exit code -1073741510.", Result(-1073741510).FailureMessage);
    }

    [Fact]
    [Trait("Req", "PR-TERM-082")] // Core/Support/Shell.swift:18
    public void The_exit_code_sentence_writes_a_negative_status_with_an_ASCII_hyphen_whatever_the_culture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.NumberFormat.NegativeSign = "\U00002212"; // U+2212 MINUS SIGN
        using var scope = CultureScope.Of(culture);
        Assert.Equal("The command failed with exit code -5.", Result(-5).FailureMessage);
        // R-TEST-4: the obvious .NET interpolation follows the culture and gives another string.
        var status = -5;
        Assert.Equal("The command failed with exit code \U000022125.", $"The command failed with exit code {status}.");
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:4-12
    public void ShellResult_keeps_raw_bytes_and_decodes_UTF8_lossily_and_succeeds_only_on_status_zero()
    {
        var result = new ShellResult(0, [0x61, 0xFF, 0x62], [0x20, 0x0A, 0xE2, 0x80]);
        Assert.True(result.Succeeded);
        Assert.Equal("a\U0000FFFDb", result.Output); // U+FFFD REPLACEMENT CHARACTER
        Assert.Equal(" \n\U0000FFFD", result.ErrorOutput); // U+FFFD REPLACEMENT CHARACTER
        Assert.Equal("value", Result(0, "\r\n\t value \n\n").TrimmedOutput);
        Assert.Equal("\r\n\t value \n\n", Result(0, "\r\n\t value \n\n").Output);
        Assert.False(Result(1).Succeeded);
        Assert.False(Result(-1).Succeeded);
        Assert.False(Result(9).Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-048")] // Core/Support/Shell.swift:4-20
    public void Install_failure_text_is_the_trimmed_stderr_else_the_trimmed_stdout_else_the_exit_code_sentence()
    {
        Assert.Equal("npm ERR! code E404", Result(1, "added 0 packages\n", "\nnpm ERR! code E404\n").FailureMessage);
        Assert.Equal("added 0 packages", Result(1, "\n added 0 packages \n", " \t\r\n").FailureMessage);
        Assert.Equal("The command failed with exit code 254.", Result(254, "\r\n", "").FailureMessage);
        // succeeded is status == 0 alone, whatever was printed.
        Assert.True(Result(0, "", "warning: deprecated").Succeeded);
        Assert.False(Result(254).Succeeded);
    }

    [Fact]
    [Trait("Rule", "R-ERR-3")]
    public void ShellError_describes_itself_with_its_message_verbatim()
    {
        // Shell.swift:22-29: errorDescription returns the message.
        var error = new ShellError("gh was not found on your PATH.");
        Assert.Equal("gh was not found on your PATH.", error.ErrorDescription);
        Assert.Equal("gh was not found on your PATH.", error.Message);
        Assert.Equal("gh was not found on your PATH.", SwiftErrors.LocalizedDescription(error));
    }

    [Fact]
    [Trait("Req", "PR-GIT-051")] // Core/Support/Shell.swift:128-131: a tool not on the PATH throws "<name> was not found on your PATH." and spawns nothing
    public async Task A_tool_missing_from_the_path_throws_the_exact_sentence_without_starting_anything()
    {
        const string tool = "marrow-test-no-such-forge-cli";
        Assert.Null(LoginEnvironment.Which(tool)); // nothing by this name anywhere on the PATH (PATHEXT included)

        var error = await Assert.ThrowsAsync<ShellError>(() => Shell.RunTool(tool, ["pr", "create"]));

        Assert.Equal(tool + " was not found on your PATH.", error.Message);
        Assert.Equal(error.Message, SwiftErrors.LocalizedDescription(error));
    }

    // PR-GIT-051's verification_method is "for each of the three names": gh, glab and tea (the forge CLIs Git.swift:174-199 and :231-248 run by name).
    // The lookup is over a PATH with nothing on it, so a real gh the owner has installed cannot make the call succeed.
    [Theory]
    [InlineData("gh")]
    [InlineData("glab")]
    [InlineData("tea")]
    [Trait("Req", "PR-GIT-051")] // Core/Support/Shell.swift:128-131: "<name> was not found on your PATH." for each forge CLI, nothing spawned, nothing armed
    public async Task A_missing_forge_CLI_throws_its_exact_sentence_for_each_of_the_three_names(string tool)
    {
        using var runtime = new TestRuntime();
        using var emptyPath = LoginEnvironment.PathForTesting.Override("");
        Assert.Null(LoginEnvironment.Which(tool));

        var error = await Assert.ThrowsAsync<ShellError>(() => Shell.RunTool(tool, ["--version"]));

        Assert.Equal(tool + " was not found on your PATH.", error.Message);
        Assert.Equal(tool + " was not found on your PATH.", error.ErrorDescription);
        Assert.Equal(error.Message, SwiftErrors.LocalizedDescription(error));
        Assert.Equal(0, runtime.Time.ActiveTimers); // no process started, so no deadline was armed (:132 is never reached)
    }

    // ---- Shell.run (:36-108) ----

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:36-56,105-107
    public async Task Run_returns_the_raw_exit_status_and_both_streams_as_bytes()
    {
        using var runtime = new TestRuntime();
        byte[] stdout = [0x6F, 0x75, 0x74, 0xFF, 0x00, 0x0A];
        byte[] stderr = [0x65, 0x72, 0x72, 0x0D, 0x0A];
        var result = await ShellScripts.Run(ShellScripts.Write("Output", stdout) + ShellScripts.Write("Error", stderr) + "exit 7")
            .WaitAsync(Bound);
        Assert.Equal(7, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(stdout, result.Stdout);
        Assert.Equal(stderr, result.Stderr);
        Assert.Equal("err", result.FailureMessage);
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:48
    public async Task Without_a_directory_the_child_inherits_the_apps_working_directory_and_with_one_runs_there()
    {
        using var runtime = new TestRuntime();
        var script = ShellScripts.WriteText("[Environment]::CurrentDirectory");
        var inherited = await ShellScripts.Run(script).WaitAsync(Bound);
        Assert.Equal(Environment.CurrentDirectory, inherited.Output);
        var folder = ProcessTestFolders.Create("shell cwd");
        var given = await ShellScripts.Run(script, directory: folder).WaitAsync(Bound);
        // Windows keeps a working directory in its full form, so a '/' in MARROW_PORT_DATA_DIR comes back as '\'.
        Assert.Equal(Path.GetFullPath(folder), given.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:49
    public async Task The_given_environment_replaces_the_childs_whole_environment()
    {
        using var runtime = new TestRuntime();
        var environment = ShellScripts.HostEnvironment();
        environment.Remove("MARROW_PORT_DATA_DIR");
        environment["MARROW_SHELL_PROBE"] = "probe-value";
        var result = await ShellScripts.Run(ShellScripts.WriteText("\"$env:MARROW_SHELL_PROBE|$env:MARROW_PORT_DATA_DIR|\""),
            environment: environment).WaitAsync(Bound);
        Assert.Equal(0, result.Status);
        Assert.Equal("probe-value||", result.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:40,49
    [Trait("Decision", "ED-170")]
    public async Task Without_an_environment_the_child_gets_LoginEnvironment_current()
    {
        using var runtime = new TestRuntime();
        var names = new[] { "PATH", "LANG", "MARROW_PORT_DATA_DIR" }.Concat(LoginEnvironmentSamples.ExcludedKeys).ToArray();
        var result = await Shell.Run(SystemTools.PowerShell, ShellScripts.Arguments(ShellScripts.ReportVariables(names)))
            .WaitAsync(Bound);
        Assert.Equal(0, result.Status);
        var host = ShellScripts.HostEnvironment();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // current is the host's environment without the fifteen excluded keys, with the augmented PATH and a LANG
        // (Shell.swift:455-460 as ED-170 (2) applies it), so a child that inherited the host's own would differ.
        var expected = new List<string>
        {
            "PATH=" + LoginEnvironmentSamples.ExpectedAugmentedPath(host.GetValueOrDefault("PATH"), home),
            "LANG=" + (host.GetValueOrDefault("LANG") ?? "en_US.UTF-8"),
            "MARROW_PORT_DATA_DIR=" + host["MARROW_PORT_DATA_DIR"],
        };
        expected.AddRange(LoginEnvironmentSamples.ExcludedKeys.Select(name => name + "!"));
        Assert.Equal(expected, result.Output.Split('\n'));
        Assert.Equal(LoginEnvironmentSamples.ExpectedAugmentedPath(host.GetValueOrDefault("PATH"), home), LoginEnvironment.Current["PATH"]);
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:53,56
    public async Task Without_input_stdin_is_the_null_device_so_a_child_reading_it_sees_end_of_file_at_once()
    {
        using var runtime = new TestRuntime();
        var result = await ShellScripts.Run("$t=[Console]::In.ReadToEnd(); " + ShellScripts.WriteText("'[' + $t.Length + ']'"))
            .WaitAsync(Bound);
        Assert.Equal("[0]", result.Output);
        Assert.Equal(0, result.Status);
    }

    [Fact]
    [Trait("Req", "PR-TERM-081")] // Core/Support/Shell.swift:76-80,86-88
    public async Task A_launch_failure_is_rethrown_and_produces_no_result()
    {
        using var runtime = new TestRuntime();
        var missing = Path.Join(ProcessTestFolders.Create("shell missing"), "no-such-tool.exe");
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            Shell.Run(missing, [], null, ShellScripts.HostEnvironment()).WaitAsync(Bound));
        Assert.IsNotType<CancellationError>(error);
        Assert.True(SwiftTry.IsThrownError(error));
        // No deadline was started for a process that never ran.
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-TERM-088")] // Core/Support/Shell.swift:53,90-95
    public async Task Input_is_written_to_stdin_and_then_closed_so_the_child_sees_its_end()
    {
        using var runtime = new TestRuntime();
        var input = new byte[100_000];
        ShellScripts.Pattern(input.Length).CopyTo(input, 0);
        input[0] = 0x00;
        input[1] = 0xFF;
        ShellScripts.Utf8("\U000000E9").CopyTo(input, 2); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        var result = await ShellScripts.Run(ShellScripts.Echo, input: input).WaitAsync(Bound);
        Assert.Equal(0, result.Status);
        Assert.Equal(input, result.Stdout);
    }

    [Fact]
    [Trait("Req", "PR-TERM-088")] // Core/Support/Shell.swift:90-95
    public async Task A_child_that_exits_without_reading_its_input_gives_its_own_status_and_no_error()
    {
        using var runtime = new TestRuntime();
        var result = await ShellScripts.Run("exit 5", input: ShellScripts.Pattern(4_000_000)).WaitAsync(Bound);
        Assert.Equal(5, result.Status);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    [Trait("Req", "PR-TERM-083")] // Core/Support/Shell.swift:32-35,43,57-60,282-288
    public async Task Without_a_limit_every_byte_of_both_streams_is_kept()
    {
        using var runtime = new TestRuntime();
        var result = await ShellScripts.Run(ShellScripts.WritePattern("Output", 300_000, 7_000) +
            ShellScripts.WritePattern("Error", 70_000, 3_000)).WaitAsync(Bound);
        Assert.Equal(ShellScripts.Pattern(300_000), result.Stdout);
        Assert.Equal(ShellScripts.Pattern(70_000), result.Stderr);
    }

    [Fact]
    [Trait("Req", "PR-TERM-083")] // Core/Support/Shell.swift:282-288
    public async Task With_a_limit_each_stream_keeps_a_tail_between_the_limit_and_twice_the_limit()
    {
        using var runtime = new TestRuntime();
        // No more than twice the limit: nothing is trimmed, so the result is longer than the limit.
        var small = await ShellScripts.Run(ShellScripts.Write("Output", ShellScripts.Pattern(1_500)) +
            ShellScripts.Write("Error", ShellScripts.Pattern(1_999, 3)), outputLimit: 1_000).WaitAsync(Bound);
        Assert.Equal(ShellScripts.Pattern(1_500), small.Stdout);
        Assert.Equal(ShellScripts.Pattern(1_999, 3), small.Stderr);
        // Far more: the head is dropped and the tail kept, at least the limit and at most twice it.
        var full = ShellScripts.Pattern(200_000);
        var large = await ShellScripts.Run(ShellScripts.WritePattern("Output", 200_000, 700), outputLimit: 1_000).WaitAsync(Bound);
        Assert.InRange(large.Stdout.Length, 1_000, 2_000);
        Assert.Equal(full[^large.Stdout.Length..], large.Stdout);
        Assert.Empty(large.Stderr);
    }

    [Fact]
    [Trait("Req", "PR-TERM-084")] // Core/Support/Shell.swift:63-68,81-85,103
    public async Task A_command_that_finishes_cancels_its_deadline_so_it_is_never_killed_late()
    {
        using var runtime = new TestRuntime();
        var result = await ShellScripts.Run("exit 0").WaitAsync(Bound);
        Assert.True(result.Succeeded);
        Assert.True(Eventually(() => runtime.Time.ActiveTimers == 0), "the deadline stayed armed after the exit");
        runtime.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-TERM-084")] // Core/Support/Shell.swift:42,81-85
    [Trait("Req", "PR-TERM-085")] // Core/Support/Shell.swift:110-113
    [Trait("Req", "PR-GIT-004")] // Core/Support/Shell.swift:81-85,105-107
    [Trait("Decision", "ED-047")]
    public async Task At_the_default_120_second_timeout_the_child_is_asked_to_stop_and_a_cooperating_one_exits()
    {
        using var runtime = new TestRuntime();
        var armed = Path.Join(ProcessTestFolders.Create("shell timeout"), "armed");
        var run = ShellScripts.Run(ShellScripts.BreakChild(exitsOnBreak: true, armed));
        await WaitForArmed(run, armed);
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(120))), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(120) - TimeSpan.FromTicks(1));
        Assert.False(run.IsCompleted);
        runtime.Advance(TimeSpan.FromTicks(1));
        var result = await run.WaitAsync(Bound);
        Assert.Equal(ShellScripts.BreakExitCode, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(ShellScripts.BreakSeen, result.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-084")] // Core/Support/Shell.swift:81-85
    [Trait("Req", "PR-TERM-085")] // Core/Support/Shell.swift:110-118
    [Trait("Req", "PR-GIT-004")] // Core/Support/Shell.swift:81-85,105-118
    [Trait("Decision", "ED-047")]
    public async Task A_child_that_ignores_the_stop_request_is_killed_exactly_one_second_later_and_a_failed_result_returns()
    {
        using var runtime = new TestRuntime();
        var armed = Path.Join(ProcessTestFolders.Create("shell kill"), "armed");
        var received = Path.Join(Path.GetDirectoryName(armed)!, "received");
        var run = ShellScripts.Run(ShellScripts.BreakChild(exitsOnBreak: false, armed, received));
        await WaitForArmed(run, armed);
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(120))), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(120));
        // The deadline starts when the stop is requested (Shell.swift:112-113).
        // Keep fake time frozen until the real child has received and flushed the signal.
        // Scheduling the kill does not prove asynchronous helper startup has finished.
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(121))), "the kill was never scheduled");
        Assert.True(Eventually(() => File.Exists(received) || run.IsCompleted), "the child never received the graceful signal");
        if (run.IsCompleted) await run;
        Assert.True(File.Exists(received), "the child never received the graceful signal");
        Assert.False(SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromMilliseconds(500)), "the child ended on the stop request");
        runtime.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
        Assert.False(run.IsCompleted);
        runtime.Advance(TimeSpan.FromTicks(1));
        var result = await run.WaitAsync(Bound);
        Assert.NotEqual(0, result.Status);
        Assert.NotEqual(ShellScripts.BreakExitCode, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(ShellScripts.BreakIgnored, result.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-086")] // Core/Support/Shell.swift:69-70
    public async Task A_task_cancelled_before_the_launch_throws_CancellationError_and_never_tries_to_launch()
    {
        using var runtime = new TestRuntime();
        // The executable does not exist: an attempted launch would throw its own error, not CancellationError.
        var missing = Path.Join(ProcessTestFolders.Create("shell cancelled"), "no-such-tool.exe");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = TaskHandle.DetachedThrowing(async () =>
        {
            await gate.Task;
            return await Shell.Run(missing, [], null, ShellScripts.HostEnvironment());
        });
        handle.Cancel();
        gate.SetResult();
        await Assert.ThrowsAsync<CancellationError>(() => handle.Value.WaitAsync(Bound));
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-TERM-086")] // Core/Support/Shell.swift:97-106
    [Trait("Req", "PR-TERM-085")] // Core/Support/Shell.swift:110-113
    public async Task Cancelling_after_the_launch_stops_the_child_and_throws_CancellationError_instead_of_a_result()
    {
        using var runtime = new TestRuntime();
        var armed = Path.Join(ProcessTestFolders.Create("shell cancel"), "armed");
        var handle = TaskHandle.DetachedThrowing(() => ShellScripts.Run(ShellScripts.BreakChild(exitsOnBreak: true, armed)));
        await WaitForArmed(handle.Value, armed);
        handle.Cancel();
        await Assert.ThrowsAsync<CancellationError>(() => handle.Value.WaitAsync(Bound));
    }

    [Fact]
    [Trait("Req", "PR-TERM-086")] // Core/Support/Shell.swift:97-102
    [Trait("Req", "PR-TERM-085")] // Core/Support/Shell.swift:110-118
    [Trait("Decision", "ED-047")]
    public async Task Cancelling_a_child_that_ignores_the_stop_request_kills_it_one_second_later_and_throws()
    {
        using var runtime = new TestRuntime();
        var armed = Path.Join(ProcessTestFolders.Create("shell cancel kill"), "armed");
        var handle = TaskHandle.DetachedThrowing(() => ShellScripts.Run(ShellScripts.BreakChild(exitsOnBreak: false, armed)));
        await WaitForArmed(handle.Value, armed);
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(120))), "no deadline was armed");
        handle.Cancel();
        // The deadline sleep is not the kill: the kill is a second timer, one second out.
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.SequenceEqual([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(120)])),
            "the one-second kill must be scheduled alongside the still-pending 120-second deadline");
        Assert.False(SpinWait.SpinUntil(() => handle.Value.IsCompleted, TimeSpan.FromMilliseconds(500)), "the child ended on the stop request");
        runtime.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<CancellationError>(() => handle.Value.WaitAsync(Bound));
    }

    // ---- Shell.run(tool:) (:120-133) ----

    [Fact]
    [Trait("Req", "PR-TERM-090")] // Core/Support/Shell.swift:120-131
    public async Task RunTool_throws_the_named_not_found_error_when_the_tool_is_not_on_the_login_PATH()
    {
        using var runtime = new TestRuntime();
        var name = "marrow-no-such-tool-" + Guid.NewGuid().ToString("N");
        // An async throw: the call returns a task, and the error comes out of the await.
        var run = Shell.RunTool(name, ["--version"]);
        var error = await Assert.ThrowsAsync<ShellError>(() => run.WaitAsync(Bound));
        Assert.Equal(name + " was not found on your PATH.", error.ErrorDescription);
        Assert.Equal(name + " was not found on your PATH.", SwiftErrors.LocalizedDescription(error));
        // Nothing was launched, so no deadline was armed.
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-TERM-090")] // Core/Support/Shell.swift:129
    public async Task RunTool_looks_the_tool_up_on_the_login_PATH_not_on_the_PATH_of_the_environment_it_is_given()
    {
        using var runtime = new TestRuntime();
        var folder = ProcessTestFolders.Create("shell tool elsewhere");
        var name = "marrow-tool-" + Guid.NewGuid().ToString("N");
        ProcessTestFolders.Touch(folder, name + ".exe");
        var environment = ShellScripts.HostEnvironment();
        environment["PATH"] = folder;
        // which(name) is called without the environment (:129), so only current's PATH is searched.
        var error = await Assert.ThrowsAsync<ShellError>(() => Shell.RunTool(name, [], environment: environment).WaitAsync(Bound));
        Assert.Equal(name + " was not found on your PATH.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-TERM-090")] // Core/Support/Shell.swift:129,132
    public async Task RunTool_runs_the_resolved_tool_with_the_given_directory_environment_and_input()
    {
        using var runtime = new TestRuntime();
        var folder = ProcessTestFolders.Create("shell tool run");
        var environment = ShellScripts.HostEnvironment();
        environment["MARROW_SHELL_PROBE"] = "tool-probe";
        var script = "$i=[Console]::In.ReadToEnd(); " +
            ShellScripts.WriteText("$i + '|' + $env:MARROW_SHELL_PROBE + '|' + [Environment]::CurrentDirectory");
        // A name holding '\' is a path to which, found without a PATH search (ED-031 (3)).
        var result = await Shell.RunTool(SystemTools.PowerShell, ShellScripts.Arguments(script), folder, environment,
            ShellScripts.Utf8("stdin-text")).WaitAsync(Bound);
        Assert.Equal(0, result.Status);
        Assert.Equal("stdin-text|tool-probe|" + Path.GetFullPath(folder), result.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-090")] // Core/Support/Shell.swift:127,132
    [Trait("Req", "PR-TERM-084")] // Core/Support/Shell.swift:81-85
    [Trait("Decision", "ED-047")]
    public async Task RunTool_forwards_its_timeout_to_the_deadline()
    {
        using var runtime = new TestRuntime();
        var armed = Path.Join(ProcessTestFolders.Create("shell tool timeout"), "armed");
        var run = Shell.RunTool(SystemTools.PowerShell, ShellScripts.Arguments(ShellScripts.BreakChild(exitsOnBreak: true, armed)),
            timeout: 7);
        await WaitForArmed(run, armed);
        Assert.True(Eventually(() => runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(7))), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        Assert.False(run.IsCompleted);
        runtime.Advance(TimeSpan.FromTicks(1));
        var result = await run.WaitAsync(Bound);
        Assert.Equal(ShellScripts.BreakExitCode, result.Status);
        Assert.Equal(ShellScripts.BreakSeen, result.Output);
    }

    [Fact]
    [Trait("Req", "PR-TERM-091")] // Core/Support/Shell.swift:120-133
    public async Task RunTool_has_no_outputLimit_so_a_tool_run_by_name_keeps_its_whole_output()
    {
        using var runtime = new TestRuntime();
        var parameters = typeof(Shell).GetMethod(nameof(Shell.RunTool))!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "tool", "arguments", "in", "environment", "input", "timeout" }, parameters);
        var result = await Shell.RunTool(SystemTools.PowerShell, ShellScripts.Arguments(
            ShellScripts.WritePattern("Output", 300_000, 7_000) + ShellScripts.WritePattern("Error", 70_000, 3_000))).WaitAsync(Bound);
        Assert.Equal(ShellScripts.Pattern(300_000), result.Stdout);
        Assert.Equal(ShellScripts.Pattern(70_000), result.Stderr);
    }

    // ---- OnceContinuation (:218-243) ----

    [Fact]
    [Trait("Req", "PR-TERM-089")] // Core/Support/Shell.swift:218-243
    public async Task OnceContinuation_takes_the_first_resume_and_ignores_every_later_one()
    {
        var value = await CheckedContinuation<int>.With(continuation =>
        {
            var once = new OnceContinuation<int>(continuation);
            once.Resume(1);
            once.Resume(2);
            once.Resume(new InvalidOperationException("late"));
        }).WaitAsync(Bound);
        Assert.Equal(1, value);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CheckedContinuation<int>.With(continuation =>
        {
            var once = new OnceContinuation<int>(continuation);
            once.Resume(new InvalidOperationException("first"));
            once.Resume(3);
        }).WaitAsync(Bound));
        Assert.Equal("first", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-TERM-089")] // Core/Support/Shell.swift:218-243
    public async Task OnceContinuation_resumes_exactly_once_when_many_threads_race()
    {
        for (var round = 0; round < 50; round++)
        {
            OnceContinuation<int>? once = null;
            var task = CheckedContinuation<int>.With(continuation => once = new OnceContinuation<int>(continuation));
            using var start = new ManualResetEventSlim();
            var racers = Enumerable.Range(1, 8).Select(i => Task.Run(() =>
            {
                start.Wait();
                once!.Resume(i);
            })).ToArray();
            start.Set();
            await Task.WhenAll(racers).WaitAsync(Bound);
            Assert.InRange(await task.WaitAsync(Bound), 1, 8);
        }
    }

    // ---- OutputCollector (:245-325) ----

    [Fact]
    [Trait("Req", "PR-TERM-083")] // Core/Support/Shell.swift:265-288
    public async Task Without_a_limit_the_collector_keeps_every_chunk_in_order()
    {
        var collector = new OutputCollector(null);
        var stdout = collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe());
        var stderr = collector.Reader(OutputCollector.Stream.Stderr, new ProcessPipe());
        for (var i = 0; i < 100; i++) stdout(ShellScripts.Pattern(1_000, i * 1_000));
        stderr([0x01, 0x02]);
        stdout([]);
        stderr([]);
        var (outBytes, errBytes) = await collector.Finished(within: 3).WaitAsync(Bound);
        Assert.Equal(ShellScripts.Pattern(100_000), outBytes);
        Assert.Equal(new byte[] { 0x01, 0x02 }, errBytes);
    }

    [Fact]
    [Trait("Req", "PR-TERM-083")] // Core/Support/Shell.swift:282-288
    public async Task With_a_limit_a_buffer_is_cut_to_its_last_limit_bytes_only_once_it_holds_more_than_twice_the_limit()
    {
        var collector = new OutputCollector(10);
        var stdout = collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe());
        var stderr = collector.Reader(OutputCollector.Stream.Stderr, new ProcessPipe());
        var stream = ShellScripts.Pattern(21);
        stdout(stream[..15]);
        stdout(stream[15..20]); // 20 bytes: exactly twice the limit, not more, so nothing is cut
        stderr(ShellScripts.Pattern(50, 100)); // one chunk of more than twice the limit
        stdout([]);
        stderr([]);
        var (outBytes, errBytes) = await collector.Finished(within: 3).WaitAsync(Bound);
        Assert.Equal(stream[..20], outBytes);
        Assert.Equal(ShellScripts.Pattern(50, 100)[40..], errBytes);

        var cut = new OutputCollector(10);
        var reader = cut.Reader(OutputCollector.Stream.Stdout, new ProcessPipe());
        reader(stream[..20]);
        reader(stream[20..21]); // 21 bytes: more than twice the limit, cut to the last 10
        reader(ShellScripts.Pattern(5, 200));
        reader([]);
        cut.Reader(OutputCollector.Stream.Stderr, new ProcessPipe())([]);
        var (tail, _) = await cut.Finished(within: 3).WaitAsync(Bound);
        Assert.Equal(stream[11..21].Concat(ShellScripts.Pattern(5, 200)).ToArray(), tail);
    }

    [Fact]
    [Trait("Req", "PR-TERM-087")] // Core/Support/Shell.swift:290-291
    public async Task Finished_returns_at_once_when_both_streams_have_already_ended()
    {
        using var runtime = new TestRuntime();
        var collector = new OutputCollector(null);
        collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe())([0x41]);
        collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe())([]);
        collector.Reader(OutputCollector.Stream.Stderr, new ProcessPipe())([]);
        var finished = collector.Finished(within: 3);
        Assert.True(finished.IsCompletedSuccessfully);
        Assert.Equal(new byte[] { 0x41 }, (await finished).Stdout);
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "PR-TERM-087")] // Core/Support/Shell.swift:105,292-298,316-324
    public async Task Finished_waits_three_seconds_for_a_stream_that_never_ends_then_returns_what_it_has()
    {
        using var runtime = new TestRuntime();
        var collector = new OutputCollector(null);
        var stdout = collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe());
        var stderr = collector.Reader(OutputCollector.Stream.Stderr, new ProcessPipe());
        stdout([0x61]);
        stdout([]);
        stderr([0x62]);
        var finished = collector.Finished(within: 3);
        Assert.True(Eventually(() => runtime.Time.ActiveTimers == 1), "no drain deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromTicks(1));
        Assert.False(finished.IsCompleted);
        runtime.Advance(TimeSpan.FromTicks(1));
        var (outBytes, errBytes) = await finished.WaitAsync(Bound);
        Assert.Equal(new byte[] { 0x61 }, outBytes);
        Assert.Equal(new byte[] { 0x62 }, errBytes);
    }

    [Fact]
    [Trait("Req", "PR-TERM-087")] // Core/Support/Shell.swift:299-313,316-324
    public async Task Every_waiter_wakes_once_when_the_second_stream_ends_and_a_repeated_end_wakes_nobody()
    {
        using var runtime = new TestRuntime();
        var collector = new OutputCollector(null);
        var stdout = collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe());
        var stderr = collector.Reader(OutputCollector.Stream.Stderr, new ProcessPipe());
        stdout([]);
        var first = collector.Finished(within: 3);
        var second = collector.Finished(within: 3);
        Assert.True(Eventually(() => runtime.Time.ActiveTimers == 2), "the drain deadlines were not armed");
        stdout([]); // the same stream again: not the second stream, so nobody wakes
        Assert.False(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromMilliseconds(200)));
        Assert.False(second.IsCompleted);
        stderr([0x7A]);
        stderr([]);
        var (_, a) = await first.WaitAsync(Bound);
        var (_, b) = await second.WaitAsync(Bound);
        Assert.Equal(new byte[] { 0x7A }, a);
        Assert.Equal(new byte[] { 0x7A }, b);
        // Each deadline is cancelled on the way out, so nothing is left to fire.
        Assert.True(Eventually(() => runtime.Time.ActiveTimers == 0), "a drain deadline stayed armed");
    }

    [Fact]
    [Trait("Req", "PR-TERM-087")] // Core/Support/Shell.swift:309-312
    public async Task Cancelling_the_waiting_task_closes_both_streams_at_once()
    {
        using var runtime = new TestRuntime();
        var collector = new OutputCollector(null);
        collector.Reader(OutputCollector.Stream.Stdout, new ProcessPipe())([0x31]);
        var handle = TaskHandle.Detached(() => collector.Finished(within: 3));
        Assert.True(Eventually(() => runtime.Time.ActiveTimers == 1), "no drain deadline was armed");
        handle.Cancel();
        var (outBytes, errBytes) = await handle.Value.WaitAsync(Bound);
        Assert.Equal(new byte[] { 0x31 }, outBytes);
        Assert.Empty(errBytes);
        // Both streams now count as ended, so a later wait returns at once.
        Assert.True(collector.Finished(within: 3).IsCompletedSuccessfully);
    }
}
