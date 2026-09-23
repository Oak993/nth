using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/Timeline.swift (B2 1fbf81e), :1-366.
//
// Tests for part 1 of Core.Models.Timeline (work packet part1.md): the records listed there, minus
// TurnRecord.tokenSpends and ThreadDocument.continuation, both reported as blocked in Timeline.cs's header comment
// (Core.Models.TokenSpend and Core.Models.ThreadContinuation own their own Codable, a sibling skeleton this part
// does not touch).
//
// Expectations are derived from the Swift source quoted in the records, never from running the port. JSON shapes
// come from Coding.swift's JSONEncoder.storage (sorted keys, compact, "/" escaped) as PR-CHAT-017 and PR-CHAT-019
// name for ThreadDocument's persistence, and from SwiftDecoding/Coding's documented strict-vs-lenient split
// (R-CODE-1, R-CODE-2) for which keys are required.
public sealed class TimelineTests
{
    private static string Encode<T>(T value) => Encoding.UTF8.GetString(SwiftJson.Encode(value, Coding.StorageOptions, SwiftJsonStyle.Storage));

    private static T Decode<T>(string json) => Coding.Decode<T>(Encoding.UTF8.GetBytes(json))!;

    // ---- PR-CHAT-001: TimelineItem's four stored fields, id/date defaults ----

    [Fact]
    [Trait("Req", "PR-CHAT-001")] // Core/Models/Timeline.swift:4-15
    public void A_timeline_item_defaults_a_fresh_uppercase_UUID_string_id_and_the_current_date()
    {
        using var runtime = new TestRuntime();
        var item = new TimelineItem(turnID: null, content: new TimelineItem.Content.User(new UserMessage { Text = "hi" }));
        Assert.True(Guid.TryParseExact(item.Id, "D", out _));
        Assert.Equal(item.Id, item.Id.ToUpperInvariant());
        Assert.Equal(runtime.Time.GetUtcNow(), item.Date);
        Assert.Null(item.TurnID);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-001")] // Core/Models/Timeline.swift:4-26
    public void A_timeline_item_round_trips_every_content_case_under_its_own_case_name()
    {
        var turnID = Guid.NewGuid();
        var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        TimelineItem.Content[] cases =
        [
            new TimelineItem.Content.User(new UserMessage { Text = "hi" }),
            new TimelineItem.Content.Assistant(new AssistantMessage { Text = "hi" }),
            new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "hi" }),
            new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "t", StartedAt = date }),
            new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "m", ProposedPlanState = ProposedPlan.State.Drafting }),
            new TimelineItem.Content.Todos(new ValueList<TodoStep>([new TodoStep { Text = "a", TodoStepStatus = TodoStep.Status.Pending }])),
            new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "m" }),
            new TimelineItem.Content.TurnEnd(new TurnSummary
            {
                TurnID = turnID, Status = TurnStatus.Completed, Duration = 1.5, FilesChanged = 1, Additions = 1, Deletions = 0,
            }),
        ];
        string[] caseNames = ["user", "assistant", "reasoning", "tool", "plan", "todos", "notice", "turnEnd"];
        for (var i = 0; i < cases.Length; i++)
        {
            var item = new TimelineItem(turnID, cases[i], id: "fixed-id", date: date);
            var json = Encode(item);
            using var document = JsonDocument.Parse(json);
            Assert.True(document.RootElement.GetProperty("content").TryGetProperty(caseNames[i], out var payload));
            Assert.True(payload.TryGetProperty("_0", out _));
            var reloaded = Decode<TimelineItem>(json);
            Assert.Equal(cases[i], reloaded.TimelineItemContent);
            Assert.Equal("fixed-id", reloaded.Id);
            Assert.Equal(turnID, reloaded.TurnID);
            Assert.Equal(date, reloaded.Date);
        }
    }

    // ---- PR-CHAT-002: isEmptyReasoning trims Unicode whitespace-and-newlines ----

    [Theory]
    [Trait("Req", "PR-CHAT-002")] // Core/Models/Timeline.swift:28-32; Core/Support/Text.swift:132
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t \n")]
    [InlineData(" ")] // non-breaking space: whitespacesAndNewlines includes it
    public void A_reasoning_block_whose_text_trims_to_nothing_is_empty_reasoning(string text)
    {
        var item = new TimelineItem(null, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text }));
        Assert.True(item.IsEmptyReasoning);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-002")] // Core/Models/Timeline.swift:28-32
    public void Non_reasoning_content_and_non_empty_reasoning_are_never_empty_reasoning()
    {
        Assert.False(new TimelineItem(null, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "hmm" })).IsEmptyReasoning);
        Assert.False(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "" })).IsEmptyReasoning);
    }

    // ---- PR-CHAT-003 / PR-CHAT-169: cleanedOfEmDashes rewrites only assistant, reasoning and plan ----

    [Fact]
    [Trait("Req", "PR-CHAT-003")] // Core/Models/Timeline.swift:35-56
    [Trait("Req", "PR-CHAT-169")] // Core/Models/Timeline.swift:35-38 doc comment: lossy rewrite on read, reproduced
    public void CleanedOfEmDashes_rewrites_assistant_reasoning_and_plan_and_leaves_every_other_case_untouched()
    {
        var em = "before—after"; // U+2014 EM DASH
        var turnID = Guid.NewGuid();
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var assistant = new TimelineItem(turnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = em }), "id", date).CleanedOfEmDashes;
        Assert.Equal(TextCleanup.WithoutEmDashes(em), ((TimelineItem.Content.Assistant)assistant.TimelineItemContent).Value.Text);
        Assert.Equal("id", assistant.Id);
        Assert.Equal(turnID, assistant.TurnID);
        Assert.Equal(date, assistant.Date);

        var reasoning = new TimelineItem(turnID, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = em })).CleanedOfEmDashes;
        Assert.Equal(TextCleanup.WithoutEmDashes(em), ((TimelineItem.Content.Reasoning)reasoning.TimelineItemContent).Value.Text);

        var plan = new TimelineItem(turnID, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = em, ProposedPlanState = ProposedPlan.State.Proposed })).CleanedOfEmDashes;
        Assert.Equal(TextCleanup.WithoutEmDashes(em), ((TimelineItem.Content.Plan)plan.TimelineItemContent).Value.Markdown);

        var user = new TimelineItem(turnID, new TimelineItem.Content.User(new UserMessage { Text = em })).CleanedOfEmDashes;
        Assert.Equal(em, ((TimelineItem.Content.User)user.TimelineItemContent).Value.Text);

        var toolOutput = new TimelineItem(turnID, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t", Output = em })).CleanedOfEmDashes;
        Assert.Equal(em, ((TimelineItem.Content.Tool)toolOutput.TimelineItemContent).Value.Output);

        var notice = new TimelineItem(turnID, new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = em })).CleanedOfEmDashes;
        Assert.Equal(em, ((TimelineItem.Content.Notice)notice.TimelineItemContent).Value.Message);
    }

    // ---- PR-CHAT-004: UserMessage Hydra routing precedence ----

    [Fact]
    [Trait("Req", "PR-CHAT-004")] // Core/Models/Timeline.swift:58-70; App/Views/Chat/TimelineRows.swift:79-83
    public void HydraHeads_present_even_as_an_empty_array_takes_precedence_over_hydraBrief()
    {
        var reportEvenEmpty = new UserMessage { Text = "hi", HydraHeads = ValueList<long>.Empty, HydraBrief = true };
        Assert.True(reportEvenEmpty.IsFromHydra);
        Assert.True(reportEvenEmpty.IsHydraBrief); // both true; the row order (isFromHydra first) picks the report row

        var brief = new UserMessage { Text = "hi", HydraBrief = true };
        Assert.False(brief.IsFromHydra);
        Assert.True(brief.IsHydraBrief);

        var plain = new UserMessage { Text = "hi" };
        Assert.False(plain.IsFromHydra);
        Assert.False(plain.IsHydraBrief);

        var falseBrief = new UserMessage { Text = "hi", HydraBrief = false };
        Assert.False(falseBrief.IsHydraBrief); // == true, not "not false"
    }

    [Fact]
    [Trait("Req", "PR-CHAT-004")] // Core/Models/Timeline.swift:62-66: optional keys absent on an older message
    public void HydraHeads_and_hydraBrief_are_optional_keys_that_decode_as_null_when_absent()
    {
        var reloaded = Decode<UserMessage>("{\"text\":\"hi\",\"attachments\":[]}");
        Assert.Null(reloaded.HydraHeads);
        Assert.Null(reloaded.HydraBrief);
        Assert.False(reloaded.IsFromHydra);
        Assert.False(reloaded.IsHydraBrief);
    }

    // ---- PR-CHAT-005 / PR-CHAT-190: Attachment.isImage, base64Images, downscaledJPEG ----

    [Theory]
    [Trait("Req", "PR-CHAT-005")] // Core/Models/Timeline.swift:96
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("IMAGE/png", false)] // literal prefix test, not case-insensitive
    [InlineData("text/plain", false)]
    [InlineData("application/pdf", false)]
    public void IsImage_is_a_literal_prefix_test_on_the_mime_type(string mimeType, bool expected) =>
        Assert.Equal(expected, new Attachment { Name = "a", Path = "a", MimeType = mimeType }.IsImage);

    [Fact]
    [Trait("Req", "PR-CHAT-005")] // Core/Models/Timeline.swift:102-111
    public async Task Base64Images_drops_a_non_image_and_a_file_it_cannot_read_and_keeps_the_rest_in_order()
    {
        var root = Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"))).FullName;
        var smallPath = Path.Combine(root, "small.bmp");
        WriteBmp(smallPath, 4, 2);
        var attachments = new[]
        {
            new Attachment { Name = "small", Path = smallPath, MimeType = "image/bmp" },
            new Attachment { Name = "not-image", Path = smallPath, MimeType = "text/plain" },
            new Attachment { Name = "missing", Path = Path.Combine(root, "absent.bmp"), MimeType = "image/bmp" },
        };
        var result = await Attachment.Base64Images(attachments);
        Assert.Single(result);
        Assert.Equal("small", result[0].Image.Name);
        Assert.Equal("image/bmp", result[0].Image.MimeType); // too small to downscale: mime type unchanged
        Assert.Equal(Convert.ToBase64String(File.ReadAllBytes(smallPath)), result[0].Base64);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-005")] // Core/Models/Timeline.swift:106-109
    [Trait("Req", "PR-CHAT-190")] // Core/Models/Timeline.swift:113-137
    public async Task An_image_past_2000_pixels_on_its_longest_side_is_sent_as_a_downscaled_JPEG()
    {
        var root = Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"))).FullName;
        var bigPath = Path.Combine(root, "big.bmp");
        WriteBmp(bigPath, 2500, 500); // 2500 > 2000 on the longest side; aspect ratio 5:1
        var attachment = new Attachment { Name = "big", Path = bigPath, MimeType = "image/bmp" };

        var result = await Attachment.Base64Images([attachment]);
        Assert.Single(result);
        Assert.Equal("image/jpeg", result[0].Image.MimeType); // :108 mimeType becomes image/jpeg once downscaled
        Assert.Equal("big", result[0].Image.Name); // the stored attachment's other fields are untouched
        Assert.Equal(attachment.Path, result[0].Image.Path);

        var jpegBytes = Convert.FromBase64String(result[0].Base64);
        Assert.True(jpegBytes.Length > 4);
        Assert.Equal(0xFF, jpegBytes[0]);
        Assert.Equal(0xD8, jpegBytes[1]); // JPEG SOI marker
        Assert.Equal(0xFF, jpegBytes[^2]);
        Assert.Equal(0xD9, jpegBytes[^1]); // JPEG EOI marker

        var jpegPath = Path.Combine(root, "big.jpg");
        File.WriteAllBytes(jpegPath, jpegBytes);
        var decoded = WicImageDecoder.Decode(jpegPath, 4096, false);
        Assert.NotNull(decoded);
        Assert.True(Math.Max(decoded.Width, decoded.Height) <= 2000); // :125 no more than maxPixelSize on the longest side
        Assert.Equal(2000, decoded.Width); // the longer side (width, 2500) scales to exactly maxPixelSize
        Assert.InRange(decoded.Height, 390, 410); // 500 * 2000 / 2500 = 400, aspect preserved
    }

    [Fact]
    [Trait("Req", "PR-CHAT-190")] // Core/Models/Timeline.swift:113-137: nothing is written to disk
    public void DownscaleToJpeg_returns_null_for_an_image_already_at_or_under_the_threshold()
    {
        var root = Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "exact.bmp");
        WriteBmp(path, 2000, 1000); // exactly maxPixelSize on the longest side: strictly greater is required
        var data = File.ReadAllBytes(path);
        Assert.Null(WicImageDecoder.DownscaleToJpeg(data, 2000, 0.8));
    }

    // ---- PR-CHAT-006: ToolCall.Kind (8), ToolCall.Status (4), and the defaults ----

    [Fact]
    [Trait("Req", "PR-CHAT-006")] // Core/Models/Timeline.swift:151-179
    public void ToolCall_has_exactly_eight_kinds_and_four_statuses_as_their_lowercase_raw_strings()
    {
        Assert.Equal(
            ["command", "read", "edit", "search", "web", "mcp", "agent", "other"],
            SwiftEnum<ToolCall.Kind>.AllCases.Select(SwiftEnum<ToolCall.Kind>.RawValue));
        Assert.Equal(
            ["running", "completed", "failed", "declined"],
            SwiftEnum<ToolCall.Status>.AllCases.Select(SwiftEnum<ToolCall.Status>.RawValue));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-006")] // Core/Models/Timeline.swift:169-179
    public void ToolCall_defaults_output_status_edits_and_startedAt_and_leaves_the_rest_null()
    {
        using var runtime = new TestRuntime();
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        Assert.Equal("", call.Output);
        Assert.Equal(ToolCall.Status.Running, call.ToolCallStatus);
        Assert.Empty(call.Edits);
        Assert.Equal(runtime.Time.GetUtcNow(), call.StartedAt);
        Assert.Null(call.Detail);
        Assert.Null(call.ExitCode);
        Assert.Null(call.FinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-006")] // Core/Models/Timeline.swift:171-179: raw strings on the wire
    public void ToolCall_round_trips_through_the_lowercase_raw_kind_and_status_strings()
    {
        var call = new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Mcp, Title = "t", Detail = "d", Output = "o", ToolCallStatus = ToolCall.Status.Failed,
            ExitCode = 2, StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero),
        };
        var json = Encode(call);
        Assert.Contains("\"kind\":\"mcp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"failed\"", json, StringComparison.Ordinal);
        Assert.Equal(call, Decode<ToolCall>(json));
    }

    // ---- PR-CHAT-007 / PR-RUNTIME-150: the 60000-byte ellipsis trim, byte-vs-character mismatch ----

    [Fact]
    [Trait("Req", "PR-CHAT-007")] // Core/Models/Timeline.swift:169, :181-199
    [Trait("Req", "PR-RUNTIME-150")] // Core/Models/Timeline.swift:196-199
    public void Output_at_or_under_the_60000_byte_ceiling_is_kept_whole()
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        var exact = new string('a', 60_000);
        Assert.Equal(exact, call.SetOutput(exact).Output);
        var appended = call.AppendOutput(new string('b', 59_000)).AppendOutput(new string('c', 1_000)).Output;
        Assert.Equal(60_000, appended.Length);
        Assert.DoesNotContain('…', appended);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-007")] // Core/Models/Timeline.swift:196-199
    [Trait("Req", "PR-RUNTIME-150")] // Core/Models/Timeline.swift:196-199
    public void Output_over_the_ceiling_becomes_an_ellipsis_plus_the_last_30000_characters()
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        var body = new string('x', 60_001);
        var trimmed = call.SetOutput(body).Output;
        Assert.StartsWith("…", trimmed, StringComparison.Ordinal); // U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal(30_000, trimmed.Length - 1);
        Assert.Equal(new string('x', 30_000), trimmed[1..]);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-007")] // Core/Models/Timeline.swift:196-199: guard counts UTF-8 bytes, suffix counts Characters
    [Trait("Req", "PR-RUNTIME-150")] // Core/Models/Timeline.swift:196-199: reproduce the unit mismatch, not "fix" it
    public void The_trimmed_result_can_still_exceed_60000_bytes_for_non_ASCII_output()
    {
        // Each character is 3 UTF-8 bytes (U+2014 EM DASH); 30000 of them are 90000 bytes, well past 60000.
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        var body = new string('—', 60_001);
        var trimmed = call.SetOutput(body).Output;
        Assert.True(SwiftString.Utf8Count(trimmed) > 60_000);
        Assert.Equal(30_000, trimmed.Length - 1);
    }

    // ---- PR-CHAT-008: finish() overwrites status every time, stamps finishedAt only the first time ----

    [Fact]
    [Trait("Req", "PR-CHAT-008")] // Core/Models/Timeline.swift:191-194
    public void Finish_overwrites_status_every_call_but_stamps_finishedAt_only_the_first_time()
    {
        using var runtime = new TestRuntime();
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "t" };
        var completed = call.Finish(ToolCall.Status.Completed);
        Assert.Equal(ToolCall.Status.Completed, completed.ToolCallStatus);
        var firstFinishedAt = completed.FinishedAt;
        Assert.NotNull(firstFinishedAt);

        runtime.Advance(TimeSpan.FromSeconds(5));
        var failedLater = completed.Finish(ToolCall.Status.Failed);
        Assert.Equal(ToolCall.Status.Failed, failedLater.ToolCallStatus);
        Assert.Equal(firstFinishedAt, failedLater.FinishedAt); // unchanged: elapsed duration never grows
    }

    // ---- PR-CHAT-009: ProposedPlan (4 states) and TodoStep (3 statuses), only text/status stored ----

    [Fact]
    [Trait("Req", "PR-CHAT-009")] // Core/Models/Timeline.swift:209-230
    public void ProposedPlan_has_four_states_and_TodoStep_three_and_a_step_stores_only_text_and_status()
    {
        Assert.Equal(
            ["drafting", "proposed", "accepted", "dismissed"],
            SwiftEnum<ProposedPlan.State>.AllCases.Select(SwiftEnum<ProposedPlan.State>.RawValue));
        Assert.Equal(
            ["pending", "active", "done"],
            SwiftEnum<TodoStep.Status>.AllCases.Select(SwiftEnum<TodoStep.Status>.RawValue));

        var step = new TodoStep { Text = "write tests", TodoStepStatus = TodoStep.Status.Active };
        var json = Encode(step);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["status", "text"], document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(step, Decode<TodoStep>(json));
    }

    // ---- PR-CHAT-010: Notice has three levels; limit is a real, optional field even though never set ----

    [Fact]
    [Trait("Req", "PR-CHAT-010")] // Core/Models/Timeline.swift:232-250
    public void Notice_has_three_levels_and_an_optional_limit_that_round_trips_when_present_and_is_omitted_when_absent()
    {
        Assert.Equal(["info", "warning", "error"], SwiftEnum<Notice.Level>.AllCases.Select(SwiftEnum<Notice.Level>.RawValue));

        var plain = new Notice { NoticeLevel = Notice.Level.Warning, Message = "careful" };
        var plainJson = Encode(plain);
        Assert.DoesNotContain("\"limit\"", plainJson, StringComparison.Ordinal); // nil optional omitted
        Assert.Equal(plain, Decode<Notice>(plainJson));

        var threadID = Guid.NewGuid();
        var marked = plain with { Limit = new LimitNotice { ThreadID = threadID } };
        var markedJson = Encode(marked);
        var reloaded = Decode<Notice>(markedJson);
        Assert.Equal(threadID, reloaded.Limit?.ThreadID);
    }

    // ---- PR-CHAT-012 / PR-USAGE-084 (context usage is exercised in ThreadDocument tests below): TurnSummary ----

    [Fact]
    [Trait("Req", "PR-CHAT-012")] // Core/Models/Timeline.swift:252-267
    public void TurnSummary_stores_the_four_statuses_the_counts_and_an_optional_changes()
    {
        Assert.Equal(
            ["running", "completed", "interrupted", "failed"],
            SwiftEnum<TurnStatus>.AllCases.Select(SwiftEnum<TurnStatus>.RawValue));

        var withoutChanges = new TurnSummary
        {
            TurnID = Guid.NewGuid(), Status = TurnStatus.Completed, Duration = 12.5, FilesChanged = 0, Additions = 0, Deletions = 0,
        };
        var json = Encode(withoutChanges);
        Assert.DoesNotContain("\"changes\"", json, StringComparison.Ordinal);
        Assert.Equal(withoutChanges, Decode<TurnSummary>(json));

        var changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("a.txt", 1, 0, false)]), 1, 0);
        var withChanges = withoutChanges with { Changes = changes };
        var reloaded = Decode<TurnSummary>(Encode(withChanges));
        Assert.Equal(changes, reloaded.Changes);
    }

    // ---- PR-CHAT-013 / PR-GIT-148: TurnRecord's per-field lenient decode ----

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:283-307, :309-329
    [Trait("Req", "PR-GIT-148")] // Core/Models/Timeline.swift:290-291, :294, :296, :299, :306
    public void A_turn_record_stored_with_only_the_oldest_fields_still_decodes_with_every_default()
    {
        // An older build's turn: only index, startedAt and status - none of the newer git or Hydra fields.
        const string oldJson = "{\"index\":3,\"startedAt\":\"2026-01-01T00:00:00Z\",\"status\":\"completed\"}";
        var turn = Decode<TurnRecord>(oldJson);
        Assert.True(Guid.TryParseExact(turn.Id.ToString(), "D", out _)); // fresh UUID
        Assert.Equal(3, turn.Index);
        Assert.Null(turn.ProviderTurnID);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), turn.StartedAt);
        Assert.Null(turn.CompletedAt);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Null(turn.BaseCheckpoint);
        Assert.Null(turn.EndCheckpoint);
        Assert.Null(turn.UserItemID);
        Assert.Null(turn.ProviderDiff);
        Assert.Null(turn.ProviderAnchor);
        Assert.Null(turn.TouchedPaths);
        Assert.False(turn.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:309-329: every default, an entirely empty object
    public void An_entirely_empty_turn_record_decodes_with_every_field_defaulted()
    {
        using var runtime = new TestRuntime();
        var turn = Decode<TurnRecord>("{}");
        Assert.NotEqual(Guid.Empty, turn.Id);
        Assert.Equal(0, turn.Index);
        Assert.Equal(runtime.Time.GetUtcNow(), turn.StartedAt);
        Assert.Equal(TurnStatus.Running, turn.Status);
        Assert.False(turn.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-013")] // Core/Models/Timeline.swift:311: a wrong-typed field falls back to its default
    public void A_field_of_the_wrong_type_takes_its_default_rather_than_failing_the_record()
    {
        var turn = Decode<TurnRecord>("{\"index\":\"three\",\"hydraMerged\":\"yes\"}");
        Assert.Equal(0, turn.Index);
        Assert.False(turn.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-GIT-148")] // Core/Models/Timeline.swift:290-291, :294, :296, :299, :306: exact key names
    public void The_git_fields_persist_under_their_exact_key_names_and_touchedPaths_round_trips()
    {
        var turn = new TurnRecord
        {
            Index = 1, BaseCheckpoint = "base", EndCheckpoint = "end", ProviderDiff = "diff", ProviderAnchor = "anchor",
            TouchedPaths = new ValueList<string>(["src/a.cs", "src/b.cs"]), HydraMerged = true,
        };
        var json = Encode(turn);
        foreach (var key in new[] { "baseCheckpoint", "endCheckpoint", "providerDiff", "providerAnchor", "touchedPaths", "hydraMerged" })
            Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
        var reloaded = Decode<TurnRecord>(json);
        Assert.Equal(turn.BaseCheckpoint, reloaded.BaseCheckpoint);
        Assert.Equal(turn.EndCheckpoint, reloaded.EndCheckpoint);
        Assert.Equal(turn.ProviderDiff, reloaded.ProviderDiff);
        Assert.Equal(turn.ProviderAnchor, reloaded.ProviderAnchor);
        Assert.Equal(turn.TouchedPaths, reloaded.TouchedPaths);
        Assert.True(reloaded.HydraMerged);
    }

    // ---- PR-CHAT-014: ContextUsage.fraction ----

    [Fact]
    [Trait("Req", "PR-CHAT-014")] // Core/Models/Timeline.swift:331-339
    public void Fraction_is_null_without_a_positive_window_and_otherwise_used_over_window_clamped_to_1()
    {
        Assert.Null(new ContextUsage { UsedTokens = 10 }.Fraction);
        Assert.Null(new ContextUsage { UsedTokens = 10, WindowTokens = 0 }.Fraction);
        Assert.Null(new ContextUsage { UsedTokens = 10, WindowTokens = -5 }.Fraction);
        Assert.Equal(0.25, new ContextUsage { UsedTokens = 25, WindowTokens = 100 }.Fraction);
        Assert.Equal(1.0, new ContextUsage { UsedTokens = 250, WindowTokens = 100 }.Fraction); // clamped
        Assert.Equal(1.0, new ContextUsage { UsedTokens = 100, WindowTokens = 100 }.Fraction);
    }

    // ---- PR-USAGE-085: ContextUsage.fraction is nil without a positive window and is otherwise clamped to at most one ----

    [Fact]
    [Trait("Req", "PR-USAGE-085")] // Core/Models/Timeline.swift:335-338
    public void Fraction_is_nil_for_a_missing_zero_or_negative_window_and_otherwise_used_over_window_capped_at_one()
    {
        Assert.Null(new ContextUsage { UsedTokens = 500 }.Fraction);
        Assert.Null(new ContextUsage { UsedTokens = 500, WindowTokens = 0 }.Fraction);
        Assert.Null(new ContextUsage { UsedTokens = 500, WindowTokens = -1 }.Fraction);
        Assert.Equal(0.5, new ContextUsage { UsedTokens = 50, WindowTokens = 100 }.Fraction);
        Assert.Equal(1.0, new ContextUsage { UsedTokens = 100, WindowTokens = 100 }.Fraction);
        Assert.Equal(1.0, new ContextUsage { UsedTokens = 1_000, WindowTokens = 100 }.Fraction); // capped, never above 1
    }

    [Fact]
    [Trait("Req", "PR-USAGE-085")] // Core/Models/Timeline.swift:335-338; no lower clamp on usedTokens
    public void Fraction_has_no_lower_clamp_so_a_negative_usedTokens_yields_a_negative_fraction()
    {
        Assert.Equal(-0.5, new ContextUsage { UsedTokens = -50, WindowTokens = 100 }.Fraction);
    }

    // ---- PR-CHAT-015: FollowUpPrompt.isEmpty ----

    [Fact]
    [Trait("Req", "PR-CHAT-015")] // Core/Models/Timeline.swift:75-87
    public void A_prompt_with_any_attachment_is_never_empty_whatever_its_text()
    {
        var withAttachment = new FollowUpPrompt { Text = "   ", Attachments = new ValueList<Attachment>([new Attachment { Name = "a", Path = "a", MimeType = "image/png" }]) };
        Assert.False(withAttachment.IsEmpty);
    }

    [Theory]
    [Trait("Req", "PR-CHAT-015")] // Core/Models/Timeline.swift:84-86
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\n\t", true)]
    [InlineData(" ", true)] // non-breaking space is White_Space
    [InlineData("hi", false)]
    [InlineData("  x", false)]
    public void A_prompt_with_no_attachment_is_empty_exactly_when_every_character_is_whitespace(string text, bool expected) =>
        Assert.Equal(expected, new FollowUpPrompt { Text = text }.IsEmpty);

    // ---- PR-CHAT-016: bundleID groups prompts; null is never equal to null for this purpose ----

    [Fact]
    [Trait("Req", "PR-CHAT-016")] // Core/Models/Timeline.swift:80-82
    public void BundleID_is_optional_and_two_prompts_share_a_bundle_only_when_it_is_equal_and_non_null()
    {
        var bundle = Guid.NewGuid();
        var a = new FollowUpPrompt { Text = "a", BundleID = bundle };
        var b = new FollowUpPrompt { Text = "b", BundleID = bundle };
        var unbundled1 = new FollowUpPrompt { Text = "c" };
        var unbundled2 = new FollowUpPrompt { Text = "d" };
        Assert.Equal(a.BundleID, b.BundleID);
        Assert.NotEqual(a.BundleID, unbundled1.BundleID); // bundle vs null
        Assert.Null(unbundled1.BundleID);
        Assert.Null(unbundled2.BundleID);
        // Two nulls are not treated as the same bundle: compare via a lifted equality that a real grouping would use.
        Assert.False(unbundled1.BundleID is { } && unbundled1.BundleID == unbundled2.BundleID);
    }

    // ---- PR-CHAT-017: ThreadDocument's seven keys and defaults ----

    [Fact]
    [Trait("Req", "PR-CHAT-017")] // Core/Models/Timeline.swift:341-354
    public void A_freshly_created_thread_document_has_only_the_required_threadID_and_every_other_field_empty_or_null()
    {
        var threadID = Guid.NewGuid();
        var document = new ThreadDocument(threadID);
        Assert.Equal(threadID, document.ThreadID);
        Assert.Empty(document.Items);
        Assert.Empty(document.Turns);
        Assert.Null(document.Usage);
        Assert.Empty(document.FollowUps);
        Assert.Empty(document.HydraMerges);
        Assert.Null(document.Continuation);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-017")] // Core/Models/Timeline.swift:356-358: a missing threadID is the one non-lenient field
    public void A_document_with_no_threadID_fails_to_decode()
    {
        Assert.Throws<DecodingError>(() => Decode<ThreadDocument>("{}"));
    }

    // ---- PR-CHAT-019: array members decode leniently, dropping only the broken element ----

    [Fact]
    [Trait("Req", "PR-CHAT-019")] // Core/Models/Timeline.swift:356-365
    public void An_unreadable_item_is_dropped_and_every_readable_item_around_it_survives_in_order()
    {
        var threadID = Guid.NewGuid();
        var good1 = "{\"id\":\"first\",\"date\":\"2026-01-01T00:00:00Z\",\"content\":{\"user\":{\"_0\":{\"text\":\"a\",\"attachments\":[]}}}}";
        var badKind = "{\"id\":\"x\",\"date\":\"2026-01-01T00:00:00Z\",\"content\":{\"tool\":{\"_0\":{\"kind\":\"nope\",\"title\":\"t\",\"output\":\"\",\"status\":\"running\",\"edits\":[],\"startedAt\":\"2026-01-01T00:00:00Z\"}}}}";
        var good2 = "{\"id\":\"second\",\"date\":\"2026-01-01T00:00:00Z\",\"content\":{\"user\":{\"_0\":{\"text\":\"b\",\"attachments\":[]}}}}";
        var json = $"{{\"threadID\":\"{SwiftUUID.UuidString(threadID)}\",\"items\":[{good1},{badKind},{good2}]}}";

        var document = Decode<ThreadDocument>(json);
        Assert.Equal(2, document.Items.Count);
        Assert.Equal("first", document.Items[0].Id);
        Assert.Equal("second", document.Items[1].Id);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-019")] // Core/Models/Timeline.swift:359-363: turns, followUps and hydraMerges are lenient too
    public void Turns_followUps_and_hydraMerges_also_decode_leniently_and_usage_defaults_to_null_when_absent()
    {
        var threadID = Guid.NewGuid();
        var json =
            "{\"threadID\":\"" + SwiftUUID.UuidString(threadID) + "\"," +
            "\"turns\":[{\"index\":0},\"not-an-object\",{\"index\":1}]," +
            "\"followUps\":[{\"id\":\"" + SwiftUUID.UuidString(Guid.NewGuid()) + "\",\"text\":\"ok\",\"attachments\":[]," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\"},{\"attachments\":\"not-array\"}]," +
            "\"hydraMerges\":[{\"at\":\"2026-01-01T00:00:00Z\",\"outcome\":\"merged\",\"files\":1}," +
            "{\"at\":\"2026-01-01T00:00:00Z\",\"outcome\":\"not-a-real-outcome\",\"files\":1}]}";
        var document = Decode<ThreadDocument>(json);
        Assert.Equal(2, document.Turns.Count);
        Assert.Equal([0, 1], document.Turns.Select(t => t.Index));
        Assert.Single(document.FollowUps);
        Assert.Equal("ok", document.FollowUps[0].Text);
        Assert.Single(document.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Merged, document.HydraMerges[0].HydraMergeRecordOutcome);
        Assert.Null(document.Usage);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-017")] // Core/Models/Timeline.swift:341-365: full round trip through every non-blocked field
    [Trait("Req", "PR-USAGE-084")] // Core/Models/Timeline.swift:345, :361; ContextUsage :331-339
    public void A_document_with_every_field_populated_round_trips()
    {
        var threadID = Guid.NewGuid();
        var item = new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "hi" }), "item-1",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var turn = new TurnRecord { Index = 0 };
        var usage = new ContextUsage { UsedTokens = 10, WindowTokens = 100 };
        var followUp = new FollowUpPrompt { Text = "next" };
        var merge = new HydraMergeRecord { At = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Settled, Files = 0 };
        var document = new ThreadDocument(threadID)
        {
            Items = new ValueList<TimelineItem>([item]),
            Turns = new ValueList<TurnRecord>([turn]),
            Usage = usage,
            FollowUps = new ValueList<FollowUpPrompt>([followUp]),
            HydraMerges = new ValueList<HydraMergeRecord>([merge]),
        };
        var reloaded = Decode<ThreadDocument>(Encode(document));
        Assert.Equal(threadID, reloaded.ThreadID);
        Assert.Single(reloaded.Items);
        Assert.Equal("item-1", reloaded.Items[0].Id);
        Assert.Single(reloaded.Turns);
        Assert.Equal(usage, reloaded.Usage);
        Assert.Single(reloaded.FollowUps);
        Assert.Equal("next", reloaded.FollowUps[0].Text);
        Assert.Single(reloaded.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Settled, reloaded.HydraMerges[0].HydraMergeRecordOutcome);
    }

    // ---- PR-CHAT-018 / PR-HYDRAMERGE-081: HydraMergeRecord's five outcomes and seven fields ----

    [Fact]
    [Trait("Req", "PR-CHAT-018")] // Core/Models/Timeline.swift:269-281
    [Trait("Req", "PR-HYDRAMERGE-081")] // Core/Models/Timeline.swift:269-281
    public void HydraMergeRecord_has_five_outcomes_and_stores_every_optional_field_when_present()
    {
        Assert.Equal(
            ["merged", "failed", "stray", "held", "settled"],
            SwiftEnum<HydraMergeRecord.Outcome>.AllCases.Select(SwiftEnum<HydraMergeRecord.Outcome>.RawValue));

        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var url = SwiftURL.Create("https://example.com/mr/1")!;
        var full = new HydraMergeRecord
        {
            At = at, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Held, Project = "proj", Label = "label", Url = url, Files = 3, Detail = "held detail",
        };
        var json = Encode(full);
        var reloaded = Decode<HydraMergeRecord>(json);
        Assert.Equal(full.At, reloaded.At);
        Assert.Equal(HydraMergeRecord.Outcome.Held, reloaded.HydraMergeRecordOutcome);
        Assert.Equal("proj", reloaded.Project);
        Assert.Equal("label", reloaded.Label);
        Assert.Equal(url, reloaded.Url);
        Assert.Equal(3, reloaded.Files);
        Assert.Equal("held detail", reloaded.Detail);

        var minimal = new HydraMergeRecord { At = at, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Settled, Files = 0 };
        var minimalJson = Encode(minimal);
        foreach (var key in new[] { "project", "label", "url", "detail" })
            Assert.DoesNotContain($"\"{key}\"", minimalJson, StringComparison.Ordinal);
        Assert.Equal(minimal, Decode<HydraMergeRecord>(minimalJson));
    }

    // A minimal uncompressed 24bpp BMP, matching Support/ThumbnailCacheTests.cs's own helper: any WIC-capable
    // Windows decodes it, so it is enough to drive downscaledJPEG's dimension logic without a real photo.
    private static void WriteBmp(string path, int width, int height)
    {
        var stride = checked((width * 3 + 3) & ~3);
        var pixels = checked(stride * height);
        var bytes = new byte[checked(54 + pixels)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = 54 + y * stride + x * 3;
                bytes[i] = (byte)(17 + x % 64); bytes[i + 1] = (byte)(47 + y % 64); bytes[i + 2] = 91;
            }
        File.WriteAllBytes(path, bytes);
    }
}
