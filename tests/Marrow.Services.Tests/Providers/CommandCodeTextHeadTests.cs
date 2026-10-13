using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Providers;
[Trait("Decision", "ED-NEW-dextra-open-7")]
public sealed class CommandCodeTextHeadTests
{
    [Fact]
    public async Task Cancellation_when_the_completed_exit_is_read_never_returns_an_answer()
    {
        using var stop = new CancellationTokenSource();
        var fixture = new CommandCodeTextHeadFixture("good") { OnExit = stop.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, stop.Token, null, fixture.Make));
        Assert.True(fixture.Stopped);
    }
    [Fact]
    public async Task Action_content_after_a_text_delta_is_still_denied()
    {
        var fixture = new CommandCodeTextHeadFixture("good", "content-action");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, CancellationToken.None, null, fixture.Make));
        Assert.DoesNotContain("SECRET-SENTINEL", error.Message, StringComparison.Ordinal);
        Assert.True(fixture.Stopped);
    }
    [Fact]
    public async Task Cleanup_deletes_only_the_opened_policy_when_the_child_tries_to_replace_its_folder_with_a_junction()
    {
        var fixture = new CommandCodeTextHeadFixture("good");
        string? folder = null, moved = null, outside = null;
        fixture.OnStop = () =>
        {
            folder = fixture.Environment["HOME"]; moved = folder + "-moved"; outside = folder + "-outside";
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "text-only.mjs"), "OUTSIDE-SENTINEL");
            // Windows may refuse directory rename while a DeleteOnClose child handle is retained.
            // That refusal also protects the outside sentinel; platforms allowing rename must delete only the retained object.
            try { Directory.Move(folder, moved); }
            catch (IOException) { return; }
            Marrow.Services.Tests.Port.Skills.Store.Fixtures.WindowsJunction.Create(Marrow.Testing.TestDataDirectory.Read()!, folder, outside);
        };
        try
        {
            Assert.Equal("answer", await CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, CancellationToken.None, null, fixture.Make));
            Assert.Equal("OUTSIDE-SENTINEL", File.ReadAllText(Path.Combine(outside!, "text-only.mjs")));
            Assert.False(File.Exists(Path.Combine(moved!, "text-only.mjs")));
        }
        finally
        {
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder);
            if (moved is not null && Directory.Exists(moved)) { File.Delete(Path.Combine(moved, "text-only.mjs")); Directory.Delete(moved); }
            if (outside is not null && Directory.Exists(outside)) { File.Delete(Path.Combine(outside, "text-only.mjs")); Directory.Delete(outside); }
        }
    }

    [Fact]
    public async Task A_stop_failure_still_closes_and_removes_the_owned_policy()
    {
        var fixture = new CommandCodeTextHeadFixture("good") { OnStop = () => throw new IOException("fixture stop failure") };
        await Assert.ThrowsAsync<IOException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, CancellationToken.None, null, fixture.Make));
        Assert.False(File.Exists(Path.Combine(fixture.Environment["HOME"], "text-only.mjs")));
    }
    [Theory]
    [InlineData("recorded-delta")]
    [InlineData("recorded-final")]
    public async Task The_sourced_Command_Code_delta_and_run_end_final_text_protocols_return_the_answer(string sequence)
    {
        // CommandCodeSession.cs:439-441 and :620-627; the latter is recorded in CommandCodeSessionTurnTests.
        var fixture = new CommandCodeTextHeadFixture("good", sequence);
        Assert.Equal("answer", await CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, CancellationToken.None, null, fixture.Make));
        Assert.True(fixture.Stopped);
    }
    [Fact]
    public async Task The_actual_private_policy_blocks_native_MCP_agents_and_unknown_tools_before_any_executor_runs()
    {
        using var rig = new CommandCodePolicyRig();
        var result = await rig.Run();
        Assert.Equal(0, result["executions"]!.JSONValueInt);
        Assert.True(result["refused"]!.JSONValueBool);
        Assert.All(result["blocked"]!.JSONValueArray!, item => Assert.True(item.JSONValueBool));
        var proof = Assert.Single(result["proofs"]!.JSONValueArray!);
        Assert.Equal("fixture-nonce", proof["nonce"]!.JSONValueString);
        Assert.Empty(proof["tools"]!.JSONValueArray!);
    }
    [Fact]
    public async Task The_private_mod_must_prove_all_tools_blocked_before_the_question_is_written_and_usage_is_receipted()
    {
        var fixture = new CommandCodeTextHeadFixture("good");
        var spend = new List<TokenSpend>();
        var answer = await CommandCodeTextHead.RunAsync(fixture.Configuration, "QUESTION-SENTINEL", () => true, CancellationToken.None, spend.Add, fixture.Make);
        Assert.Equal("answer", answer);
        Assert.True(fixture.ProofReadBeforeWrite);
        Assert.Equal("QUESTION-SENTINEL", fixture.Prompt);
        Assert.True(fixture.Stopped);
        Assert.Equal(8, spend.Single().TotalTokens);
        Assert.Equal("chosen-model", spend[0].Model);
        Assert.Contains("chosen-model", fixture.Arguments);
        Assert.DoesNotContain("--yolo", fixture.Arguments);
        Assert.DoesNotContain("--tools-enable", fixture.Arguments);
        Assert.DoesNotContain("--resume", fixture.Arguments);
        Assert.Contains("--mod", fixture.Arguments);
        Assert.DoesNotContain("DROPPY_CODE_MCP_BRIDGE", fixture.Environment.Keys);
        Assert.Equal(fixture.Environment["HOME"], fixture.Environment["APPDATA"]);
        Assert.Equal("fixture-credential", fixture.Environment["COMMAND_CODE_API_KEY"]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-nonce")]
    [InlineData("tools")]
    [InlineData("early-model")]
    public async Task An_unproved_runtime_receives_no_question_and_is_stopped(string proof)
    {
        var fixture = new CommandCodeTextHeadFixture(proof);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "NEVER SEND", () => true, CancellationToken.None, null, fixture.Make));
        Assert.Null(fixture.Prompt);
        Assert.True(fixture.Stopped);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("error")]
    [InlineData("long")]
    public async Task A_runtime_action_failure_or_unbounded_reply_never_becomes_an_answer(string failure)
    {
        var fixture = new CommandCodeTextHeadFixture("good", failure);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "Q", () => true, CancellationToken.None, null, fixture.Make));
        Assert.True(fixture.Stopped);
    }

    [Fact]
    public async Task Revocation_after_the_proof_and_cancellation_before_launch_send_nothing()
    {
        var fixture = new CommandCodeTextHeadFixture("good");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommandCodeTextHead.RunAsync(fixture.Configuration, "NEVER SEND", () => !fixture.ProofRead, CancellationToken.None, null, fixture.Make));
        Assert.Null(fixture.Prompt);
        Assert.True(fixture.Stopped);
        var cancelled = new CommandCodeTextHeadFixture("good");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandCodeTextHead.RunAsync(cancelled.Configuration, "NEVER SEND", () => true, stop.Token, null, cancelled.Make));
        Assert.Null(cancelled.Prompt);
        Assert.False(cancelled.Started);
    }
}
