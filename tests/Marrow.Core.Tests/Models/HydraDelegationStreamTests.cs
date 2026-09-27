using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/HydraDelegationStream.swift (B2 1fbf81e),
// unit Core.Models.HydraDelegationStream, part 1 of 1. Every expected value is transcribed
// from the cited Swift lines or the record itself, never from the C# under test (R-TEST-4).
// Records proved: PR-HYDRA-081, PR-HYDRA-086, PR-HYDRA-087, PR-HYDRA-094, PR-HYDRA-095,
// PR-HYDRA-096.
public sealed class HydraDelegationStreamTests
{
    // ---- PR-HYDRA-081: the streaming opener accepts ```hydra and ```hydra-sent but not ```hydra-leaving ----

    [Trait("Req", "PR-HYDRA-081")] // DroppyCode/Core/Models/HydraDelegationStream.swift:17
    [Fact]
    public void Streaming_opener_matches_plain_hydra_fence()
    {
        var result = HydraPrompts.StreamedDelegationsIn("```hydra\n[]\n```");

        Assert.NotNull(result);
    }

    [Trait("Req", "PR-HYDRA-081")] // DroppyCode/Core/Models/HydraDelegationStream.swift:17
    [Fact]
    public void Streaming_opener_matches_hydra_sent_fence()
    {
        var result = HydraPrompts.StreamedDelegationsIn("```hydra-sent\n[]\n```");

        Assert.NotNull(result);
    }

    [Trait("Req", "PR-HYDRA-081")] // DroppyCode/Core/Models/HydraDelegationStream.swift:17
    [Fact]
    public void Streaming_opener_does_not_match_hydra_leaving_fence()
    {
        var result = HydraPrompts.StreamedDelegationsIn("```hydra-leaving\n[]\n```");

        Assert.Null(result);
    }

    [Trait("Req", "PR-HYDRA-081")] // DroppyCode/Core/Models/Hydra.swift:1092-1094
    [Fact]
    public void The_whole_message_parser_accepts_all_three_dialects_unlike_the_streaming_opener()
    {
        Assert.True(HydraPrompts.HasDelegationOpener("```hydra\n"));
        Assert.True(HydraPrompts.HasDelegationOpener("```hydra-sent\n"));
        Assert.True(HydraPrompts.HasDelegationOpener("```hydra-leaving\n"));

        var leaving = HydraPrompts.Delegations("```hydra-leaving\n[]\n```");
        Assert.NotNull(leaving);
        Assert.Empty(leaving);
    }

    // ---- PR-HYDRA-086: delegation(fromEntry:) recognises exactly seven field names, drops a promptless entry ----

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:110-120
    [Fact]
    public void An_entry_with_no_prompt_field_is_dropped()
    {
        var entry = JSONValue.Parse("""{"task":"do it"}""");

        Assert.Null(HydraPrompts.Delegation(entry!));
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:111
    [Fact]
    public void An_entry_whose_prompt_is_only_whitespace_after_trimming_is_dropped()
    {
        var entry = JSONValue.Parse("""{"prompt":"   \n\t  "}""");

        Assert.Null(HydraPrompts.Delegation(entry!));
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:110-119
    [Fact]
    public void An_entry_with_all_seven_fields_uses_the_primary_key_over_its_alias()
    {
        var entry = JSONValue.Parse(
            """{"prompt":"fix the bug","task":"fix it","name":"Otto","head":"Nova","project":"gaze-site","repo":"other-repo","profile":"deep"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Equal("fix it", delegation!.Task);
        Assert.Equal("fix the bug", delegation.Prompt);
        Assert.Equal("Otto", delegation.Name);
        Assert.Equal("gaze-site", delegation.Project);
        Assert.Equal("deep", delegation.Profile);
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:113-118
    [Fact]
    public void An_entry_with_only_the_alias_fields_falls_back_to_them()
    {
        var entry = JSONValue.Parse("""{"prompt":"do it","head":"Nova","repo":"gaze-site"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Equal("Nova", delegation!.Name);
        Assert.Equal("gaze-site", delegation.Project);
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:112,119; Support/Text.cs:106-118
    [Fact]
    public void An_entry_with_no_task_is_titled_from_the_prompt_via_TextCleanup_SingleLine_at_limit_60()
    {
        var prompt = new string('x', 80);
        var entry = JSONValue.Parse($$"""{"prompt":"{{prompt}}"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        // TextCleanup.SingleLine(prompt, 60): 59 kept characters plus U+2026 HORIZONTAL ELLIPSIS.
        var expected = new string('x', 59) + "…";
        Assert.Equal(expected, delegation!.Task);
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:112
    [Fact]
    public void An_entry_whose_task_is_only_whitespace_still_falls_back_to_the_prompt_title()
    {
        var entry = JSONValue.Parse("""{"prompt":"do it","task":"   "}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Equal("do it", delegation!.Task);
    }

    [Trait("Req", "PR-HYDRA-086")] // DroppyCode/Core/Models/HydraDelegationStream.swift:113-118
    [Fact]
    public void An_entry_with_neither_name_nor_project_fields_leaves_them_nil()
    {
        var entry = JSONValue.Parse("""{"prompt":"do it"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Null(delegation!.Name);
        Assert.Null(delegation.Project);
        Assert.Null(delegation.Profile);
    }

    // ---- PR-HYDRA-087: an empty-string name or project SHADOWS its alias instead of falling through ----

    [Trait("Req", "PR-HYDRA-087")] // DroppyCode/Core/Models/HydraDelegationStream.swift:113-117
    [Fact]
    public void An_empty_name_shadows_the_head_alias_leaving_the_head_unnamed()
    {
        var entry = JSONValue.Parse("""{"prompt":"do it","name":"","head":"Otto"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Null(delegation!.Name);
    }

    [Trait("Req", "PR-HYDRA-087")] // DroppyCode/Core/Models/HydraDelegationStream.swift:116-117
    [Fact]
    public void An_empty_project_shadows_the_repo_alias_leaving_the_project_nil()
    {
        var entry = JSONValue.Parse("""{"prompt":"do it","project":"","repo":"gaze-site"}""");

        var delegation = HydraPrompts.Delegation(entry!);

        Assert.NotNull(delegation);
        Assert.Null(delegation!.Project);
    }

    // ---- PR-HYDRA-094: streamedDelegations(inBody:) walks UTF-8 bytes and emits each entry the moment its outermost brace closes ----

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:33-34
    [Fact]
    public void Leading_JSON_whitespace_space_newline_cr_tab_is_skipped_before_the_shape_check()
    {
        var result = HydraPrompts.StreamedDelegationsInBody(" \n\r\t[]");

        Assert.True(result.IsComplete);
        Assert.Empty(result.Delegations);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:43-44
    [Fact]
    public void A_body_that_opens_with_neither_bracket_nor_brace_is_an_empty_incomplete_result()
    {
        var result = HydraPrompts.StreamedDelegationsInBody("not json at all");

        Assert.False(result.IsComplete);
        Assert.Empty(result.Delegations);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:66-75
    [Fact]
    public void An_array_entry_is_emitted_as_soon_as_its_outermost_brace_closes_even_before_the_array_closes()
    {
        var result = HydraPrompts.StreamedDelegationsInBody("""[{"prompt":"first"}""");

        Assert.False(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("first", result.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:86-88
    [Fact]
    public void A_closing_bracket_at_depth_zero_marks_the_block_complete()
    {
        var result = HydraPrompts.StreamedDelegationsInBody("""[{"prompt":"first"},{"prompt":"second"}]""");

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Delegations.Count);
        Assert.Equal("first", result.Delegations[0].Prompt);
        Assert.Equal("second", result.Delegations[1].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:76-79
    [Fact]
    public void A_bare_object_body_completes_the_instant_its_single_entry_closes()
    {
        var result = HydraPrompts.StreamedDelegationsInBody("""{"prompt":"solo"} trailing text after""");

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("solo", result.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:80-84
    [Fact]
    public void An_unbalanced_closing_brace_at_negative_depth_marks_the_block_complete()
    {
        // The array opens, then an extra "}" with nothing open drives depth to -1.
        var result = HydraPrompts.StreamedDelegationsInBody("[}");

        Assert.True(result.IsComplete);
        Assert.Empty(result.Delegations);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:89-92
    [Fact]
    public void A_backtick_at_line_start_at_depth_zero_closes_the_block_as_the_fence()
    {
        var result = HydraPrompts.StreamedDelegationsInBody("[{\"prompt\":\"first\"}\n```");

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:89 (atLineStart, not mid-line)
    [Fact]
    public void A_backtick_that_does_not_open_a_line_does_not_close_the_block()
    {
        // The backtick sits mid-string, so it is a plain string byte, not a fence closer.
        var result = HydraPrompts.StreamedDelegationsInBody("""[{"prompt":"has a ` backtick"}]""");

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("has a ` backtick", result.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:54-61
    [Fact]
    public void Braces_inside_a_string_including_an_escaped_quote_do_not_affect_depth_tracking()
    {
        var result = HydraPrompts.StreamedDelegationsInBody(
            "[{\"prompt\":\"has a \\\"quoted\\\" brace } and a backslash \\\\ too\"}]");

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("has a \"quoted\" brace } and a backslash \\ too", result.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:29-101 windows_path
    [Fact]
    public void A_multi_byte_UTF8_character_in_a_string_is_never_mistaken_for_an_ASCII_delimiter()
    {
        // U+1F600 GRINNING FACE (a 4-byte UTF-8 sequence) sits right next to where an ASCII
        // brace would matter; a byte-blind walk could split it and misread a delimiter.
        var body = "[{\"prompt\":\"emoji \U0001F600 done\"}]";

        var result = HydraPrompts.StreamedDelegationsInBody(body);

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("emoji \U0001F600 done", result.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-094")] // DroppyCode/Core/Models/HydraDelegationStream.swift:73-75 (compactMap-equivalent skip)
    [Fact]
    public void An_entry_that_fails_delegation_fromEntry_is_skipped_and_does_not_stop_later_entries()
    {
        var result = HydraPrompts.StreamedDelegationsInBody(
            """[{"task":"no prompt here"},{"prompt":"has one"}]""");

        Assert.True(result.IsComplete);
        Assert.Single(result.Delegations);
        Assert.Equal("has one", result.Delegations[0].Prompt);
    }

    // ---- PR-HYDRA-095: HydraDelegationStreamParser is a stateful byte machine fed only newly appended text ----

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:144-147
    [Fact]
    public void A_fresh_parser_carries_its_turn_and_entry_ids_and_starts_empty()
    {
        var turnID = Guid.NewGuid();
        var parser = new HydraDelegationStreamParser(turnID, "entry-1");

        Assert.Equal(turnID, parser.TurnID);
        Assert.Equal("entry-1", parser.EntryID);
        Assert.Empty(parser.Delegations);
        Assert.False(parser.IsComplete);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:157-177
    [Fact]
    public void The_parser_recognises_the_plain_hydra_opener_fed_in_one_chunk()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("```hydra\n[{\"prompt\":\"go\"}]");

        Assert.True(parser.IsComplete);
        Assert.Single(parser.Delegations);
        Assert.Equal("go", parser.Delegations[0].Prompt);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:149-155
    [Fact]
    public void The_parser_accumulates_state_across_many_ingest_calls_fed_byte_by_byte()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        var full = "```hydra\n[{\"prompt\":\"first\"},{\"prompt\":\"second\"}]";

        foreach (var ch in full)
            parser.Ingest(ch.ToString());

        Assert.Equal(2, parser.Delegations.Count);
        Assert.Equal("first", parser.Delegations[0].Prompt);
        Assert.Equal("second", parser.Delegations[1].Prompt);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:162-165 (state 3 tolerates an extra backtick)
    [Fact]
    public void The_opener_tolerates_an_extra_backtick_before_hydra()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("````hydra\n[]");

        Assert.True(parser.IsComplete);
        Assert.Empty(parser.Delegations);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:160-161, 167-168 (reset-to-1-on-backtick)
    [Fact]
    public void A_false_start_before_the_fence_does_not_prevent_the_real_fence_matching()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("not a fence ```hydra\n[{\"prompt\":\"go\"}]");

        Assert.True(parser.IsComplete);
        Assert.Single(parser.Delegations);
        Assert.Equal("go", parser.Delegations[0].Prompt);
    }

    [Theory]
    [InlineData("```hyd```hydra\n[]")]
    [InlineData("```hydra-se```hydra\n[]")]
    [InlineData("```hydra\r```hydra\n[]")]
    [InlineData("```hydra```hydra\n[]")]
    [Trait("Req", "PR-HYDRA-095")] // Core/Models/HydraDelegationStream.swift:166-177,187 reset the mismatching backtick to opener 1
    public void Review_mismatching_backtick_starts_the_next_real_fence(string input)
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        parser.Ingest(input);
        Assert.True(parser.IsComplete);
        Assert.Empty(parser.Delegations);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-095")] // Core/Models/HydraDelegationStream.swift:199-202,220 only depth-zero line-start backticks finish
    public void Review_backtick_completion_requires_line_start_and_zero_object_depth()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        parser.Ingest("```hydra\n[ x`");
        Assert.False(parser.IsComplete);
        parser.Ingest("\n`");
        Assert.True(parser.IsComplete);
        var nested = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        nested.Ingest("```hydra\n[{\n`");
        Assert.False(nested.IsComplete);
        var insideString = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        insideString.Ingest("```hydra\n[{\"prompt\":\"has a ` backtick\"}");
        Assert.False(insideString.IsComplete);
        Assert.Equal("has a ` backtick", Assert.Single(insideString.Delegations).Prompt);
        insideString.Ingest("]"); Assert.True(insideString.IsComplete);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:169 (the -sent suffix)
    [Fact]
    public void The_parser_recognises_the_hydra_sent_opener()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("```hydra-sent\n[{\"prompt\":\"go\"}]");

        Assert.True(parser.IsComplete);
        Assert.Single(parser.Delegations);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:failure_cases (never recognises hydra-leaving)
    [Fact]
    public void The_parser_never_recognises_the_hydra_leaving_opener_and_stays_incomplete()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("```hydra-leaving\n[{\"prompt\":\"go\"}]\n```");

        Assert.False(parser.IsComplete);
        Assert.Empty(parser.Delegations);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:150 (guard !isComplete else return)
    [Fact]
    public void Ingest_after_completion_is_a_no_op()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        parser.Ingest("```hydra\n[{\"prompt\":\"go\"}]");
        Assert.True(parser.IsComplete);

        parser.Ingest("more text that would otherwise matter {\"prompt\":\"ignored\"}");

        Assert.Single(parser.Delegations);
    }

    [Trait("Req", "PR-HYDRA-095")] // DroppyCode/Core/Models/HydraDelegationStream.swift:191-198 (a bare-object body completes on its own close)
    [Fact]
    public void The_parser_completes_a_bare_object_body_the_instant_it_closes()
    {
        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");

        parser.Ingest("```hydra\n{\"prompt\":\"solo\"}");

        Assert.True(parser.IsComplete);
        Assert.Single(parser.Delegations);
        Assert.Equal("solo", parser.Delegations[0].Prompt);
    }

    // ---- PR-HYDRA-096: the two streaming readers and the whole-message parser agree on order and count ----

    [Trait("Req", "PR-HYDRA-096")] // DroppyCode/Core/Models/HydraDelegationStream.swift:21-28
    [Theory]
    [InlineData("""[{"prompt":"alpha"},{"prompt":"beta"},{"prompt":"gamma"}]""")]
    [InlineData("""[{"prompt":"solo one"}]""")]
    [InlineData("""[]""")]
    [InlineData("""{"prompt":"bare object"}""")]
    [InlineData("""[{"task":"no prompt"},{"prompt":"kept"},{"prompt":"also kept"}]""")]
    public void The_body_walker_and_the_byte_fed_parser_agree_on_order_and_count_over_a_corpus(string body)
    {
        var bodyResult = HydraPrompts.StreamedDelegationsInBody(body);

        var parser = new HydraDelegationStreamParser(Guid.NewGuid(), "e1");
        parser.Ingest("```hydra\n" + body);

        Assert.Equal(bodyResult.Delegations.Select(d => d.Prompt), parser.Delegations.Select(d => d.Prompt));
        Assert.Equal(bodyResult.Delegations.Select(d => d.Task), parser.Delegations.Select(d => d.Task));
    }

    [Trait("Req", "PR-HYDRA-096")] // DroppyCode/Core/Models/HydraDelegationStream.swift:21-28
    [Theory]
    [InlineData("""[{"prompt":"alpha"},{"prompt":"beta"}]""")]
    [InlineData("""[{"prompt":"solo one"}]""")]
    [InlineData("""{"prompt":"bare object"}""")]
    public void The_streaming_reader_and_the_whole_message_parser_agree_on_order_and_count_over_a_corpus(string body)
    {
        var streamed = HydraPrompts.StreamedDelegationsIn("```hydra\n" + body + "\n```");
        var whole = HydraPrompts.Delegations("```hydra\n" + body + "\n```");

        Assert.NotNull(streamed);
        Assert.NotNull(whole);
        Assert.Equal(streamed!.Delegations.Select(d => d.Prompt), whole!.Select(d => d.Prompt));
        Assert.Equal(streamed.Delegations.Select(d => d.Task), whole.Select(d => d.Task));
    }

    // PR-HYDRA-096's watermark claim ("a head is never dispatched twice, because the runtime keeps a
    // watermark of how many entries it has already sent") is runtime behaviour in the thread/turn loop
    // that owns HydraDelegationStreamParser instances, not anything this file computes; it is untestable
    // at this unit (HydraDelegationStream.swift declares no watermark) and is left for the runtime unit
    // that owns the turn loop, matching HydraTests.cs's note on PR-HYDRA-016.
}
