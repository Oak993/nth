using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Library.swift (B2 1fbf81e), unit Core.Models.Library, part 3
// of 3. Every expected value is transcribed from the cited Swift lines or the record itself, never from the C#
// under test (R-TEST-4). Records: PR-STORE-165, PR-STORE-166, PR-TERM-058, PR-TERM-059, UI-STORE-003,
// UI-STORE-006, UI-STORE-007, UI-STORE-008, UI-STORE-009.
public sealed class LibraryThirdTests
{
    private static string Uuid(Guid id) => SwiftUUID.UuidString(id);

    private static JsonElement ThreadElement(Guid id, Guid projectID, string extraJson = "")
    {
        var json = "{\"id\":\"" + Uuid(id) + "\",\"projectID\":\"" + Uuid(projectID) + "\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static ChatThread Decode(string extraJson = "") =>
        ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), extraJson));

    private static JsonElement ProjectElement(Guid id, string path, string extraJson = "")
    {
        var json = "{\"id\":\"" + Uuid(id) + "\",\"path\":\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    // ---- PR-STORE-165: hydraSpawnCount decodes to 0 when absent, null or the wrong type ----

    [Trait("Req", "PR-STORE-165")] // Core/Models/Library.swift:262, :183-184; Support/Coding.swift:5-7
    [Fact]
    public void HydraSpawnCount_absent_null_or_wrong_type_decodes_to_zero()
    {
        Assert.Equal(0, Decode().HydraSpawnCount);
        Assert.Equal(0, Decode("\"hydraSpawnCount\":null").HydraSpawnCount);
        Assert.Equal(0, Decode("\"hydraSpawnCount\":\"nope\"").HydraSpawnCount);
        Assert.Equal(0, Decode("\"hydraSpawnCount\":[1,2]").HydraSpawnCount);
    }

    [Trait("Req", "PR-STORE-165")] // Core/Models/Library.swift:262
    [Fact]
    public void HydraSpawnCount_present_decodes_and_round_trips()
    {
        var thread = Decode("\"hydraSpawnCount\":5");
        Assert.Equal(5, thread.HydraSpawnCount);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.Equal(5, reEncoded.HydraSpawnCount);
    }

    // ---- PR-STORE-166: hydra decodes to nil when absent, null or the wrong type ----

    [Trait("Req", "PR-STORE-166")] // Core/Models/Library.swift:263, :185-186, :192; Support/Coding.swift:5-7
    [Fact]
    public void Hydra_absent_null_or_a_non_object_value_decodes_to_nil_and_isHydraHead_is_false()
    {
        Assert.Null(Decode().Hydra);
        Assert.False(Decode().IsHydraHead);

        Assert.Null(Decode("\"hydra\":null").Hydra);
        Assert.False(Decode("\"hydra\":null").IsHydraHead);

        // HydraHeadInfo.init(from:) opens a keyed container first (Hydra.swift:399); a JSON string cannot
        // supply one, so this throws typeMismatch there and container.value(.hydra, default: nil) (:263)
        // swallows it to nil, exactly as a missing key or an explicit null does.
        Assert.Null(Decode("\"hydra\":\"nope\"").Hydra);
        Assert.False(Decode("\"hydra\":\"nope\"").IsHydraHead);

        Assert.Null(Decode("\"hydra\":42").Hydra);
        Assert.Null(Decode("\"hydra\":[1,2]").Hydra);
    }

    [Trait("Req", "PR-STORE-166")] // Core/Models/Library.swift:263, :399-407; Support/Coding.swift:5-7
    [Fact]
    public void A_hydra_object_missing_index_task_kind_or_origin_still_decodes_with_their_own_field_defaults()
    {
        // NOTE on PR-STORE-166: the record's source_evidence claims a "partially written ... HydraHeadInfo
        // payload is a type mismatch" that "yields nil". Reading Hydra.swift:398-403 directly shows the
        // opposite for a JSON object: every one of HydraHeadInfo's own fields, index/task/kind/origin
        // included, is decoded through the same lenient container.value(default:) helper Library.swift
        // uses, so a JSON *object* missing some of them decodes successfully with each field's own default
        // rather than failing HydraHeadInfo's init(from:) as a whole. Only a "hydra" value that is not a
        // JSON object at all (proven above) fails to open HydraHeadInfo's keyed container and yields nil.
        // This test proves the pinned source's actual behaviour, not the record's prose; flagged for the
        // ledger to correct the record.
        var thread = Decode("\"hydra\":{\"index\":1}");

        Assert.NotNull(thread.Hydra);
        Assert.True(thread.IsHydraHead);
        Assert.Equal(1, thread.Hydra!.Index);
        Assert.Equal("", thread.Hydra.Task);
        Assert.Equal(HydraHeadInfo.Kind.Droppy, thread.Hydra.HydraHeadInfoKind);
        Assert.Equal(HydraHeadInfo.Origin.Delegated, thread.Hydra.HydraHeadInfoOrigin);
    }

    [Trait("Req", "PR-STORE-166")] // Core/Models/Library.swift:263, :192
    [Fact]
    public void A_well_formed_hydra_payload_decodes_and_isHydraHead_is_true()
    {
        var thread = Decode("\"hydra\":{\"index\":1,\"task\":\"scout\",\"kind\":\"droppy\",\"origin\":\"queued\"}");

        Assert.NotNull(thread.Hydra);
        Assert.True(thread.IsHydraHead);
        Assert.Equal(1, thread.Hydra!.Index);
        Assert.Equal("scout", thread.Hydra.Task);
    }

    // ---- PR-TERM-058: a project script's five persisted fields and their construction defaults ----

    [Trait("Req", "PR-TERM-058")] // Core/Models/Library.swift:96-109
    [Fact]
    public void A_new_script_gets_a_fresh_id_symbol_play_and_runOnWorktreeCreate_false()
    {
        var script = new ProjectScript("Build", "dotnet build");

        Assert.NotEqual(Guid.Empty, script.Id);
        Assert.Equal("Build", script.Name);
        Assert.Equal("dotnet build", script.Command);
        Assert.Equal("play", script.Symbol);
        Assert.False(script.RunOnWorktreeCreate);
    }

    [Trait("Req", "PR-TERM-058")] // Core/Models/Library.swift:103-108
    [Fact]
    public void A_new_script_honors_explicit_symbol_and_runOnWorktreeCreate()
    {
        var script = new ProjectScript("Lint", "dotnet format", "wand.and.stars", true);

        Assert.Equal("wand.and.stars", script.Symbol);
        Assert.True(script.RunOnWorktreeCreate);
    }

    [Trait("Req", "PR-TERM-058")] // Core/Models/Library.swift:63, :88
    [Fact]
    public void A_script_persists_under_the_project_scripts_key_with_its_five_field_names()
    {
        var script = new ProjectScript("Build", "dotnet build", "hammer", true);
        var project = new Project("droppy-code", "/repo/droppy-code") with { Scripts = new[] { script } };

        var bytes = Coding.Encode(project);
        var json = JsonDocument.Parse(bytes).RootElement;
        var scripts = json.GetProperty("scripts");
        Assert.Equal(1, scripts.GetArrayLength());
        var scriptElement = scripts[0];

        Assert.Equal(Uuid(script.Id), scriptElement.GetProperty("id").GetString());
        Assert.Equal("Build", scriptElement.GetProperty("name").GetString());
        Assert.Equal("dotnet build", scriptElement.GetProperty("command").GetString());
        Assert.Equal("hammer", scriptElement.GetProperty("symbol").GetString());
        Assert.True(scriptElement.GetProperty("runOnWorktreeCreate").GetBoolean());
    }

    // ---- PR-TERM-059: decoding a stored script tolerates a missing id, symbol and worktree flag ----

    [Trait("Req", "PR-TERM-059")] // Core/Models/Library.swift:110-117
    [Fact]
    public void A_stored_script_missing_id_symbol_and_flag_gets_a_fresh_id_symbol_play_and_flag_false()
    {
        var element = JsonDocument.Parse("{\"name\":\"Build\",\"command\":\"dotnet build\"}").RootElement;

        var script = ProjectScriptConverter.Decode(element);

        Assert.NotEqual(Guid.Empty, script.Id);
        Assert.Equal("Build", script.Name);
        Assert.Equal("dotnet build", script.Command);
        Assert.Equal("play", script.Symbol);
        Assert.False(script.RunOnWorktreeCreate);
    }

    [Trait("Req", "PR-TERM-059")] // Core/Models/Library.swift:112-113
    [Fact]
    public void A_stored_script_with_an_id_keeps_it_instead_of_minting_a_fresh_one()
    {
        var storedId = Guid.NewGuid();
        var element = JsonDocument.Parse(
            "{\"id\":\"" + Uuid(storedId) + "\",\"name\":\"Build\",\"command\":\"dotnet build\"}").RootElement;

        var script = ProjectScriptConverter.Decode(element);

        Assert.Equal(storedId, script.Id);
    }

    [Trait("Req", "PR-TERM-059")] // Core/Models/Library.swift:114-115
    [Fact]
    public void A_stored_script_missing_name_or_command_throws_a_library_level_decode_error()
    {
        Assert.Throws<DecodingError>(() => ProjectScriptConverter.Decode(JsonDocument.Parse("{\"command\":\"x\"}").RootElement));
        Assert.Throws<DecodingError>(() => ProjectScriptConverter.Decode(JsonDocument.Parse("{\"name\":\"x\"}").RootElement));
        Assert.Throws<DecodingError>(() => ProjectScriptConverter.Decode(JsonDocument.Parse("{}").RootElement));
    }

    // ---- UI-STORE-003: ChatThread.untitled is "New thread" and drives the shown-when state ----

    [Trait("Req", "UI-STORE-003")] // Core/Models/Library.swift:122
    [Fact]
    public void Untitled_is_exactly_New_thread_capital_N_lowercase_t_one_space_no_trailing_punctuation()
    {
        Assert.Equal("New thread", ChatThread.Untitled);
        Assert.Equal(10, ChatThread.Untitled.Length);
    }

    [Trait("Req", "UI-STORE-003")] // Core/Models/Library.swift:207
    [Fact]
    public void A_freshly_created_thread_carries_the_untitled_title_and_hasCustomTitle_false()
    {
        using var runtime = new TestRuntime();
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

        Assert.Equal(ChatThread.Untitled, thread.Title);
        Assert.False(thread.HasCustomTitle);
    }

    [Trait("Req", "UI-STORE-003")] // Core/Models/Library.swift:227
    [Fact]
    public void A_stored_thread_with_no_title_decodes_to_the_untitled_placeholder()
    {
        var thread = Decode();

        Assert.Equal(ChatThread.Untitled, thread.Title);
    }

    // ---- UI-STORE-006: a project's icon; nil draws the folder mark, an emoji or symbol draws its own mark ----

    [Trait("Req", "UI-STORE-006")] // Core/Models/Library.swift:68-70
    [Fact]
    public void A_project_with_no_icon_chosen_draws_the_folder_mark()
    {
        var project = new Project("droppy-code", "/repo/droppy-code");

        Assert.Null(project.Icon);
    }

    [Trait("Req", "UI-STORE-006")] // Core/Models/Library.swift:68-70, :6-54
    [Fact]
    public void A_stored_emoji_icon_draws_the_emoji_and_a_stored_symbol_icon_draws_the_named_symbol()
    {
        var id = Guid.NewGuid();
        var emojiProject = ProjectConverter.Decode(ProjectElement(id, "/repo/a", "\"icon\":\"emoji:\\uD83D\\uDD28\""));
        Assert.IsType<ProjectIcon.Emoji>(emojiProject.Icon);
        Assert.Equal("\U0001F528", ((ProjectIcon.Emoji)emojiProject.Icon!).Value);

        var symbolProject = ProjectConverter.Decode(ProjectElement(id, "/repo/b", "\"icon\":\"symbol:hammer\""));
        Assert.IsType<ProjectIcon.Symbol>(symbolProject.Icon);
        Assert.Equal("hammer", ((ProjectIcon.Symbol)symbolProject.Icon!).Name);
    }

    [Trait("Req", "UI-STORE-006")] // Core/Models/Library.swift:90; Core/Support/Coding.swift:5-7
    [Fact]
    public void A_stored_empty_string_icon_decodes_as_nil_and_therefore_draws_the_folder_mark()
    {
        // ProjectIcon(storage:) returns nil for an empty string (Library.swift:33), so ProjectIcon's own
        // init(from:) throws (:45-47); that throw happens inside Project's container.value(.icon,
        // default: nil) (:90), which is Coding.swift's value(_:default:) try?, so it is swallowed and the
        // whole project's icon lands on nil rather than failing the project.
        var project = ProjectConverter.Decode(ProjectElement(Guid.NewGuid(), "/repo/a", "\"icon\":\"\""));

        Assert.Null(project.Icon);
    }

    // ---- UI-STORE-007: a project script's icon; absent or unrecognised falls back to "play" ----

    [Trait("Req", "UI-STORE-007")] // Core/Models/Library.swift:103, :116
    [Fact]
    public void A_script_with_no_symbol_set_falls_back_to_play()
    {
        var created = new ProjectScript("Build", "dotnet build");
        Assert.Equal("play", created.Symbol);

        var decoded = ProjectScriptConverter.Decode(JsonDocument.Parse("{\"name\":\"Build\",\"command\":\"x\"}").RootElement);
        Assert.Equal("play", decoded.Symbol);
    }

    [Theory]
    [InlineData("checkmark.seal")]
    [InlineData("wand.and.stars")]
    [InlineData("gearshape")]
    [InlineData("hammer")]
    [InlineData("ant")]
    [Trait("Req", "UI-STORE-007")] // Core/Models/Library.swift:100; App/AppModel.swift:1346-1355
    public void A_scripts_stored_symbol_is_the_SF_Symbol_name_itself_not_the_friendly_droppy_code_json_key(string symbolName)
    {
        // The persisted field is the SF Symbol name (e.g. "checkmark.seal" for the friendly key "test"), not
        // the friendly key; the port must keep the stored strings unchanged (packet notes, UI-STORE-007).
        var script = new ProjectScript("Test", "dotnet test", symbolName);

        var bytes = Coding.Encode(script);
        var json = JsonDocument.Parse(bytes).RootElement;
        Assert.Equal(symbolName, json.GetProperty("symbol").GetString());

        var reDecoded = ProjectScriptConverter.Decode(json);
        Assert.Equal(symbolName, reDecoded.Symbol);
    }

    // ---- UI-STORE-008: a settled thread's two persisted fields sort it to the bottom of its list ----

    [Trait("Req", "UI-STORE-008")] // Core/Models/Library.swift:141-147
    [Fact]
    public void IsSettled_true_with_settledAt_present_survives_a_round_trip()
    {
        var settledAt = DateTimeOffset.Parse("2024-05-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var thread = Decode("\"isSettled\":true,\"settledAt\":\"2024-05-01T12:00:00Z\"");

        Assert.True(thread.IsSettled);
        Assert.Equal(settledAt, thread.SettledAt);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.True(reEncoded.IsSettled);
        Assert.Equal(settledAt, reEncoded.SettledAt);
    }

    [Trait("Req", "UI-STORE-008")] // Core/Models/Library.swift:141-147
    [Fact]
    public void IsSettled_true_with_settledAt_absent_is_a_decodable_state()
    {
        var thread = Decode("\"isSettled\":true");

        Assert.True(thread.IsSettled);
        Assert.Null(thread.SettledAt);
    }

    [Trait("Req", "UI-STORE-008")] // Core/Models/Library.swift:143
    [Fact]
    public void A_thread_defaults_to_not_settled()
    {
        using var runtime = new TestRuntime();
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

        Assert.False(thread.IsSettled);
        Assert.Null(thread.SettledAt);
    }

    // ---- UI-STORE-009: only expandsHelpers is persisted; foldsHelpers is a computed inverse ----

    [Trait("Req", "UI-STORE-009")] // Core/Models/Library.swift:161-170
    [Fact]
    public void A_thread_saved_before_the_fold_existed_decodes_expandsHelpers_false_so_it_shows_the_folded_line()
    {
        var thread = Decode();

        Assert.False(thread.ExpandsHelpers);
        Assert.True(thread.FoldsHelpers);
    }

    [Trait("Req", "UI-STORE-009")] // Core/Models/Library.swift:162-166
    [Fact]
    public void FoldsHelpers_is_computed_and_never_written_under_its_own_key()
    {
        using var runtime = new TestRuntime();
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised)
            .WithFoldsHelpers(false);

        Assert.True(thread.ExpandsHelpers);

        var bytes = Coding.Encode(thread);
        var json = JsonDocument.Parse(bytes).RootElement;
        Assert.True(json.TryGetProperty("expandsHelpers", out var expandsHelpersProperty));
        Assert.True(expandsHelpersProperty.GetBoolean());
        Assert.False(json.TryGetProperty("foldsHelpers", out _));
    }

    [Trait("Req", "UI-STORE-009")] // Core/Models/Library.swift:161, :156-160
    [Fact]
    public void ExpandsHelpers_true_unfolds_the_helpers_and_round_trips()
    {
        var thread = Decode("\"expandsHelpers\":true");

        Assert.True(thread.ExpandsHelpers);
        Assert.False(thread.FoldsHelpers);

        var reEncoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;
        Assert.True(reEncoded.ExpandsHelpers);
    }

    [Trait("Req", "UI-STORE-009")] // Core/Models/Library.swift:159-161
    [Fact]
    public void A_helper_in_its_parents_panel_is_marked_isInPanel_and_kept_out_of_the_sidebar_count()
    {
        // Storage-side fact only: isInPanel is the persisted flag that, while true, keeps the helper out of
        // the sidebar, the palette, the recent picks and the unread count (Library.swift:159-161); the UI
        // consequences belong to the sidebar area.
        var thread = Decode("\"isInPanel\":true");

        Assert.True(thread.IsInPanel);
    }
}
