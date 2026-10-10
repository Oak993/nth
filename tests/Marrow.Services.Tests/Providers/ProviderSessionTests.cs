using System.Diagnostics;
using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderSession.swift (B3 52bc792), :1-230.
//
// The Swift file declares the shapes every provider adapter builds and consumes (TurnInput,
// SessionConfiguration, TranscriptMessage, AgentSpawn, ProviderEvent, ToolUpdate), the ProviderSession
// protocol itself, ProviderError's three cases, and the ToolTitles helpers (unwrapShell, relativePath,
// diffStats). Nothing here is persisted directly; these are the wire shapes adapters and the runtime pass
// between each other. B3 adds ToolText.blocks (:211-219) and ApprovalRequest.Kind.grantKey (:221-230),
// both relocated here from the Claude, Pi and Command Code sessions (lane A7).
public sealed class ProviderSessionTests
{
    // ---- PR-RUNTIME-001: ProviderEvent, 31 cases in declaration order, tokenSpend after usage ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-001")] // ProviderSession.swift:82-124
    public void ProviderEvent_declares_exactly_31_cases_in_source_order_with_tokenSpend_directly_after_usage()
    {
        var expectedOrder = new[]
        {
            "SessionReady", "TurnStarted", "MessageDelta", "MessageCompleted", "ReasoningDelta",
            "ReasoningCompleted", "ToolStarted", "ToolOutput", "ToolUpdated", "PlanDelta", "PlanCompleted",
            "Todos", "Approval", "Question", "RequestResolved", "Usage", "TokenSpend", "Diff", "Notice",
            "UsageLimit", "ModeChanged", "Models", "Commands", "Title", "AssistantMessageID", "TurnCompleted",
            "Exited", "AgentStarted", "AgentEvent", "AgentProgress", "AgentFinished",
        };
        Assert.Equal(31, expectedOrder.Length);

        var actualOrder = typeof(ProviderEvent)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsSealed && typeof(ProviderEvent).IsAssignableFrom(t))
            .OrderBy(t => t.MetadataToken)
            .Select(t => t.Name)
            .ToArray();

        // DEVIATION OD-0134 / ED-NEW-ignis-contract: the port-only Ignis case CacheUsage follows the 31 Swift cases.
        Assert.Equal(expectedOrder.Append("CacheUsage").ToArray(), actualOrder);

        var usageIndex = Array.IndexOf(expectedOrder, "Usage");
        var tokenSpendIndex = Array.IndexOf(expectedOrder, "TokenSpend");
        Assert.Equal(usageIndex + 1, tokenSpendIndex);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-001")] // ProviderSession.swift:99-104, :119 indirect case agentEvent
    public void ProviderEvent_agentEvent_nests_a_ProviderEvent_and_tokenSpend_carries_a_TokenSpend()
    {
        var inner = new ProviderEvent.MessageDelta("id-1", "hi");
        var nested = new ProviderEvent.AgentEvent("head-1", inner);
        Assert.Same(inner, nested.Value);

        var spend = new TokenSpend(ProviderKind.Codex, 42);
        var tokenSpend = new ProviderEvent.TokenSpend(spend);
        Assert.Same(spend, tokenSpend.Value);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-001")] // ProviderSession.swift:82-124 - every case constructs and matches by type
    public void ProviderEvent_every_declared_case_is_constructible_and_covered_by_a_switch()
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        var update = new ToolUpdate();
        var approval = new ApprovalRequest("a", ApprovalRequest.Kind.Command, "t", ValueList<ApprovalRequest.Option>.Empty);
        var question = new QuestionRequest("q", ValueList<QuestionRequest.Question>.Empty);
        var usage = new ContextUsage { UsedTokens = 0 };
        var spend = new TokenSpend(ProviderKind.Codex, 1);
        var notice = new Notice { NoticeLevel = Notice.Level.Info, Message = "m" };
        var model = new ModelOption("id", "name");
        var command = new SlashCommand { Name = "n", Detail = "d" };
        var todo = new TodoStep { Text = "t", TodoStepStatus = TodoStep.Status.Pending };
        var spawn = new AgentSpawn { Id = "id", Description = "d" };

        var samples = UnionCoverage.AssertCovers<ProviderEvent>(
            new ProviderEvent.SessionReady("s"),
            new ProviderEvent.TurnStarted(null),
            new ProviderEvent.MessageDelta("id", "t"),
            new ProviderEvent.MessageCompleted("id", "t"),
            new ProviderEvent.ReasoningDelta("id", "t"),
            new ProviderEvent.ReasoningCompleted("id", "t"),
            new ProviderEvent.ToolStarted("id", call),
            new ProviderEvent.ToolOutput("id", "t"),
            new ProviderEvent.ToolUpdated("id", update),
            new ProviderEvent.PlanDelta("id", "t"),
            new ProviderEvent.PlanCompleted("id", "md"),
            new ProviderEvent.Todos(new ValueList<TodoStep>([todo])),
            new ProviderEvent.Approval(approval),
            new ProviderEvent.Question(question),
            new ProviderEvent.RequestResolved("id"),
            new ProviderEvent.Usage(usage),
            new ProviderEvent.TokenSpend(spend),
            new ProviderEvent.Diff("d"),
            new ProviderEvent.Notice(notice),
            new ProviderEvent.UsageLimit(null),
            new ProviderEvent.ModeChanged(InteractionMode.Build),
            new ProviderEvent.Models(new ValueList<ModelOption>([model]), "id"),
            new ProviderEvent.Commands(new ValueList<SlashCommand>([command])),
            new ProviderEvent.Title("t"),
            new ProviderEvent.AssistantMessageID("id"),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            new ProviderEvent.Exited(null),
            new ProviderEvent.AgentStarted(spawn),
            new ProviderEvent.AgentEvent("id", new ProviderEvent.Title("t")),
            new ProviderEvent.AgentProgress("id", "s", "t", 1, 2),
            new ProviderEvent.AgentFinished("id", TurnStatus.Completed, "s"),
            // DEVIATION OD-0134 / ED-NEW-ignis-contract: the port-only Ignis case.
            new ProviderEvent.CacheUsage(global::Marrow.Core.Port.Ignis.IgnisTurnUsage.NotReported(ProviderKind.Claude, null, null)));

        foreach (var sample in samples)
        {
            var described = sample switch
            {
                ProviderEvent.SessionReady => true,
                ProviderEvent.TurnStarted => true,
                ProviderEvent.MessageDelta => true,
                ProviderEvent.MessageCompleted => true,
                ProviderEvent.ReasoningDelta => true,
                ProviderEvent.ReasoningCompleted => true,
                ProviderEvent.ToolStarted => true,
                ProviderEvent.ToolOutput => true,
                ProviderEvent.ToolUpdated => true,
                ProviderEvent.PlanDelta => true,
                ProviderEvent.PlanCompleted => true,
                ProviderEvent.Todos => true,
                ProviderEvent.Approval => true,
                ProviderEvent.Question => true,
                ProviderEvent.RequestResolved => true,
                ProviderEvent.Usage => true,
                ProviderEvent.TokenSpend => true,
                ProviderEvent.Diff => true,
                ProviderEvent.Notice => true,
                ProviderEvent.UsageLimit => true,
                ProviderEvent.ModeChanged => true,
                ProviderEvent.Models => true,
                ProviderEvent.Commands => true,
                ProviderEvent.Title => true,
                ProviderEvent.AssistantMessageID => true,
                ProviderEvent.TurnCompleted => true,
                ProviderEvent.Exited => true,
                ProviderEvent.AgentStarted => true,
                ProviderEvent.AgentEvent => true,
                ProviderEvent.AgentProgress => true,
                ProviderEvent.AgentFinished => true,
                ProviderEvent.CacheUsage => true,
                _ => throw new UnreachableException(),
            };
            Assert.True(described);
        }
    }

    // ---- PR-RUNTIME-002: ToolUpdate ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-002")] // ProviderSession.swift:126-134
    public void ToolUpdate_every_field_defaults_to_null()
    {
        var update = new ToolUpdate();
        Assert.Null(update.Title);
        Assert.Null(update.Detail);
        Assert.Null(update.Output);
        Assert.Null(update.Status);
        Assert.Null(update.ExitCode);
        Assert.Null(update.Edits);
        Assert.Null(update.Kind);
    }

    // ---- PR-RUNTIME-003: AgentSpawn ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-003")] // ProviderSession.swift:61-79
    public void AgentSpawn_isBackground_defaults_to_true_and_other_optionals_default_to_null()
    {
        var spawn = new AgentSpawn { Id = "id-1", Description = "desc" };
        Assert.True(spawn.IsBackground);
        Assert.Null(spawn.TaskID);
        Assert.Null(spawn.ToolUseID);
        Assert.Null(spawn.Prompt);
        Assert.Null(spawn.Model);
        Assert.Null(spawn.Profile);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-003")] // ProviderSession.swift:77-78: a foreground head passes isBackground false explicitly
    public void AgentSpawn_can_declare_a_foreground_head()
    {
        var spawn = new AgentSpawn { Id = "id-1", Description = "desc", IsBackground = false };
        Assert.False(spawn.IsBackground);
    }

    // ---- PR-RUNTIME-004: SessionConfiguration ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-004")] // ProviderSession.swift:22-47
    public void SessionConfiguration_defaults_are_fastMode_false_transcript_empty_isHydraHead_false_antiSlopEnabled_true()
    {
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        Assert.False(config.FastMode);
        Assert.Empty(config.Transcript);
        Assert.False(config.IsHydraHead);
        Assert.True(config.AntiSlopEnabled);
        Assert.Null(config.Executable);
        Assert.Null(config.ResumeID);
        Assert.Null(config.ResumeAt);
        Assert.Null(config.Model);
        Assert.Null(config.Effort);
        Assert.Null(config.ApiKey);
        Assert.Null(config.Hydra);
    }

    // ---- PR-RUNTIME-005: TurnInput ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-005")] // ProviderSession.swift:4-19
    public void TurnInput_defaults_are_serviceTier_null_fastMode_false_isFinalReport_false()
    {
        var input = new TurnInput
        {
            Text = "hello",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        Assert.Null(input.ServiceTier);
        Assert.False(input.FastMode);
        Assert.False(input.IsFinalReport);
        Assert.Null(input.Model);
        Assert.Null(input.Effort);
        Assert.Null(input.Command);
    }

    // ---- PR-RUNTIME-006: TranscriptMessage ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-006")] // ProviderSession.swift:50-58
    public void TranscriptMessage_role_has_exactly_the_two_cases_user_and_assistant()
    {
        var roles = Enum.GetValues<TranscriptMessage.Role>();
        Assert.Equal([TranscriptMessage.Role.User, TranscriptMessage.Role.Assistant], roles);

        var message = new TranscriptMessage { TranscriptMessageRole = TranscriptMessage.Role.Assistant, Text = "hi" };
        Assert.Equal(TranscriptMessage.Role.Assistant, message.TranscriptMessageRole);
        Assert.Equal("hi", message.Text);
    }

    // ---- PR-RUNTIME-007: the ProviderSession protocol ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-007")] // ProviderSession.swift:136-151
    public void ProviderSession_interface_declares_the_two_properties_and_eight_methods()
    {
        var type = typeof(ProviderSession);
        Assert.True(type.IsInterface);

        Assert.NotNull(type.GetProperty("OnEvent"));
        Assert.NotNull(type.GetProperty("IsRunning"));
        Assert.Null(type.GetProperty("IsRunning")!.SetMethod);

        var start = type.GetMethod("Start");
        Assert.NotNull(start);
        Assert.Equal(typeof(Task<string>), start!.ReturnType);

        var send = type.GetMethod("Send");
        Assert.NotNull(send);
        Assert.Equal([typeof(TurnInput)], send!.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(Task), send.ReturnType);

        var interrupt = type.GetMethod("Interrupt");
        Assert.NotNull(interrupt);
        Assert.Equal(typeof(Task), interrupt!.ReturnType);

        var resolveApproval = type.GetMethod("ResolveApproval", [typeof(string), typeof(string)]); // SK-12 adds a three-argument default overload; the Mac surface is the two-argument one
        Assert.NotNull(resolveApproval);
        Assert.Equal(typeof(void), resolveApproval!.ReturnType);
        Assert.Equal([typeof(string), typeof(string)], resolveApproval.GetParameters().Select(p => p.ParameterType));

        var answerQuestion = type.GetMethod("AnswerQuestion");
        Assert.NotNull(answerQuestion);
        Assert.Equal(typeof(void), answerQuestion!.ReturnType);
        Assert.Equal(typeof(ValueMap<string, ValueList<string>>), answerQuestion.GetParameters()[1].ParameterType);

        var compact = type.GetMethod("Compact");
        Assert.NotNull(compact);
        Assert.Equal(typeof(Task), compact!.ReturnType);

        var stop = type.GetMethod("Stop");
        Assert.NotNull(stop);
        Assert.Equal(typeof(void), stop!.ReturnType);

        var stopAgent = type.GetMethod("StopAgent");
        Assert.NotNull(stopAgent);
        Assert.Equal(typeof(Task<bool>), stopAgent!.ReturnType);
    }

    // ---- PR-RUNTIME-008: stopAgent's protocol default ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-008")] // ProviderSession.swift:153-155
    public async Task StopAgent_default_implementation_returns_false_when_not_overridden()
    {
        ProviderSession session = new MinimalProviderSession();
        Assert.False(await session.StopAgent("head-1"));
    }

    // ---- PR-RUNTIME-009 through PR-RUNTIME-012: ProviderError ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-009")] // ProviderSession.swift:164-166
    public void NotInstalled_for_an_API_key_provider_names_Settings_and_the_provider_twice()
    {
        var error = new ProviderError(new ProviderError.Case.NotInstalled(ProviderKind.Deepseek));
        Assert.Equal(
            "DeepSeek needs an API key. Add one in Settings \U00002192 Providers \U00002192 DeepSeek.", // U+2192 RIGHTWARDS ARROW
            error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-010")] // ProviderSession.swift:167-169
    public void NotInstalled_for_a_CLI_provider_names_the_login_command_in_backticks()
    {
        var error = new ProviderError(new ProviderError.Case.NotInstalled(ProviderKind.Codex));
        Assert.Equal(
            "Codex is not installed. Install it and sign in with `codex login`.",
            error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-011")] // ProviderSession.swift:170-171
    public void NotRunning_reads_the_agent_session_is_not_running()
    {
        var error = new ProviderError(new ProviderError.Case.NotRunning());
        Assert.Equal("The agent session is not running.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-012")] // ProviderSession.swift:172-173
    public void Failed_passes_its_message_through_unchanged()
    {
        var error = new ProviderError(new ProviderError.Case.Failed("Could not land the changes: conflict in a.txt"));
        Assert.Equal("Could not land the changes: conflict in a.txt", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-009")] // ProviderSession.swift:157-160 - every case constructs and switches
    public void ProviderError_case_union_is_fully_covered()
    {
        UnionCoverage.AssertCovers<ProviderError.Case>(
            new ProviderError.Case.NotInstalled(ProviderKind.Codex),
            new ProviderError.Case.NotRunning(),
            new ProviderError.Case.Failed("x"));
    }

    // ---- PR-RUNTIME-013: ToolTitles.unwrapShell ----

    [Theory]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:180-192
    [InlineData("/bin/zsh -lc 'echo hi'", "echo hi")]
    [InlineData("/bin/bash -lc 'echo hi'", "echo hi")]
    [InlineData("bash -lc 'echo hi'", "echo hi")]
    [InlineData("zsh -lc 'echo hi'", "echo hi")]
    [InlineData("/bin/sh -c 'echo hi'", "echo hi")]
    [InlineData("sh -c 'echo hi'", "echo hi")]
    public void UnwrapShell_strips_the_matching_posix_wrapper_and_its_single_quotes(string command, string expected)
    {
        Assert.Equal(expected, ToolTitles.UnwrapShell(command));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:181-182: whitespace and newlines trimmed first
    public void UnwrapShell_trims_surrounding_whitespace_and_newlines_before_matching_a_prefix()
    {
        Assert.Equal("echo hi", ToolTitles.UnwrapShell("  \n/bin/zsh -lc 'echo hi'\n  "));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:183: only the first matching prefix, in listed order
    public void UnwrapShell_removes_only_the_first_matching_prefix()
    {
        // "/bin/zsh -lc " is tried before "bash -lc " (order in the prefix list), and only one is dropped.
        Assert.Equal("bash -lc 'echo hi'", ToolTitles.UnwrapShell("/bin/zsh -lc bash -lc 'echo hi'"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:186-187: double quotes are stripped but not unescaped
    public void UnwrapShell_strips_matching_double_quotes_without_unescaping()
    {
        Assert.Equal("echo \\\"hi\\\"", ToolTitles.UnwrapShell("/bin/zsh -lc \"echo \\\"hi\\\"\""));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:187: only single-quoted bodies get the POSIX escape undone
    public void UnwrapShell_undoes_the_posix_single_quote_escape_only_inside_single_quotes()
    {
        Assert.Equal("it's here", ToolTitles.UnwrapShell("/bin/zsh -lc 'it'\\''s here'"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:185: a remainder shorter than two characters keeps its quotes
    public void UnwrapShell_leaves_a_remainder_shorter_than_two_characters_untouched()
    {
        Assert.Equal("'", ToolTitles.UnwrapShell("/bin/zsh -lc '"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:185: mismatched first/last quote characters keep both
    public void UnwrapShell_leaves_mismatched_quote_characters_untouched()
    {
        Assert.Equal("'echo hi\"", ToolTitles.UnwrapShell("/bin/zsh -lc 'echo hi\""));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-013")] // ProviderSession.swift:191: no matching prefix returns the trimmed input
    public void UnwrapShell_returns_the_trimmed_input_when_no_prefix_matches()
    {
        Assert.Equal("git status", ToolTitles.UnwrapShell("  git status  "));
    }

    // ---- PR-RUNTIME-014: ToolTitles.relativePath ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-014")] // ProviderSession.swift:194-197
    public void RelativePath_drops_the_root_prefix_when_the_path_is_under_it()
    {
        Assert.Equal("src/main.swift", ToolTitles.RelativePath("C:/repo/src/main.swift", "C:/repo"));
        Assert.Equal("src/main.swift", ToolTitles.RelativePath("C:/repo/src/main.swift", "C:/repo/"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-014")] // ProviderSession.swift:196: a path not under the root is returned whole
    public void RelativePath_returns_a_path_outside_the_root_unchanged()
    {
        Assert.Equal("C:/other/main.swift", ToolTitles.RelativePath("C:/other/main.swift", "C:/repo"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-014")]
    // DEVIATION ED-019(3)/ED-137/ED-039(2): the port's root-containment test is the same case-insensitive,
    // forward-slashed comparison TouchedPaths.Relative already applies (settled at SwiftPath.TailUnder, whose
    // own doc cites this site by name), in place of Swift's byte-exact "/"-suffixed prefix test.
    public void RelativePath_shortens_a_path_under_the_root_spelled_with_different_case()
    {
        Assert.Equal("src/main.swift", ToolTitles.RelativePath("C:/Repo/src/main.swift", "c:/repo"));
    }

    // ---- PR-RUNTIME-015: ToolTitles.diffStats ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-015")] // ProviderSession.swift:199-208
    public void DiffStats_counts_plus_and_minus_lines_and_skips_the_file_headers()
    {
        var diff = "--- a/x.txt\n+++ b/x.txt\n@@ -1,2 +1,2 @@\n-old line\n+new line\n+another new line\n unchanged\n";
        var (additions, deletions) = ToolTitles.DiffStats(diff);
        Assert.Equal(2, additions);
        Assert.Equal(1, deletions);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-015")] // ProviderSession.swift:204: a body line spelled +++ or --- is also skipped
    public void DiffStats_skips_a_body_line_that_itself_begins_with_the_header_markers()
    {
        var diff = "+++not a header line\n---also not a header\n+real addition\n";
        var (additions, deletions) = ToolTitles.DiffStats(diff);
        Assert.Equal(1, additions);
        Assert.Equal(0, deletions);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-015")] // ProviderSession.swift:203: split keeps empty lines, which match neither prefix
    public void DiffStats_keeps_empty_lines_from_the_split_without_counting_them()
    {
        var (additions, deletions) = ToolTitles.DiffStats("+one\n\n-two\n");
        Assert.Equal(1, additions);
        Assert.Equal(1, deletions);
    }

    // ---- PR-PROVCLI-053 (re-cited): ToolText.Blocks, moved here from the Claude session (B3, lane A7) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ProviderSession.swift:211-219 (B3)
    public void ToolText_Blocks_returns_empty_string_for_null_or_a_JSON_null()
    {
        Assert.Equal("", ToolText.Blocks(null));
        Assert.Equal("", ToolText.Blocks(new JSONValue.Null()));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ProviderSession.swift:214: a bare string is returned as-is
    public void ToolText_Blocks_returns_a_bare_string_unchanged()
    {
        Assert.Equal("hello", ToolText.Blocks(new JSONValue.String("hello")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ProviderSession.swift:216-217: an array's text blocks joined by one newline
    public void ToolText_Blocks_joins_the_text_fields_of_a_block_array_with_a_single_newline()
    {
        var value = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "first")),
            JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "second")));
        Assert.Equal("first\nsecond", ToolText.Blocks(value));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ProviderSession.swift:217: a block with no "text" field is skipped, not blanked
    public void ToolText_Blocks_skips_a_block_with_no_text_field_when_joining()
    {
        var value = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "image")),
            JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "second")));
        Assert.Equal("second", ToolText.Blocks(value));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ProviderSession.swift:216: neither a string nor an array reads as empty
    public void ToolText_Blocks_returns_empty_string_for_a_value_that_is_neither_a_string_nor_an_array()
    {
        Assert.Equal("", ToolText.Blocks(new JSONValue.Bool(true)));
    }

    // ---- PR-PROVCLI-248, PR-PROVCLI-285 (re-cited): ApprovalRequest.Kind.GrantKey, shared here (B3, lane A7) ----

    [Theory]
    [Trait("Req", "PR-PROVCLI-248")] // ProviderSession.swift:221-230 (B3)
    [InlineData(ApprovalRequest.Kind.Command, "command")]
    [InlineData(ApprovalRequest.Kind.FileChange, "fileChange")]
    [InlineData(ApprovalRequest.Kind.Tool, "tool")]
    [InlineData(ApprovalRequest.Kind.Permissions, "tool")]
    [InlineData(ApprovalRequest.Kind.Plan, "tool")]
    public void GrantKey_maps_each_kind_to_its_grant_string(ApprovalRequest.Kind kind, string expected)
    {
        Assert.Equal(expected, kind.GrantKey());
    }
}
