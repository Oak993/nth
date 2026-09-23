using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Library.swift (B2 1fbf81e), unit Core.Models.Library, part 1
// of 3. Every expected value is transcribed from the cited Swift lines or the record itself, never from the C#
// under test (R-TEST-4).
//
// NOTE on PR-STORE-084: the record's failure_cases text claims a malformed script record "drops the whole
// project". Reading Library.swift:88 and Coding.swift:5-7 directly (both cited by the record's own
// source_evidence) shows the opposite: `scripts = container.value(.scripts, default: [])` runs the array
// decode inside the same `try? decodeIfPresent` that every other lenient field uses, so a script element that
// throws collapses the WHOLE "scripts" array to the try?'s nil, which then falls back to `[]`; the surrounding
// Project.init(from:) never throws because of it and id/path/name/addedAt/etc. all decode normally. The
// "project-level blast radius" framing does not match the pinned Coding.swift value(_:default:) helper, which
// this test file also exercises directly on Project. The test below proves the behaviour the pinned source
// actually has, not the record's prose; flagged for the ledger to correct the record.
public sealed class LibraryTests
{
    private static string Uuid(Guid id) => SwiftUUID.UuidString(id);

    private static JsonElement ThreadElement(Guid id, Guid projectID, string extraJson = "")
    {
        var json = "{\"id\":\"" + Uuid(id) + "\",\"projectID\":\"" + Uuid(projectID) + "\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement ProjectElement(Guid id, string path, string extraJson = "")
    {
        var json = "{\"id\":\"" + Uuid(id) + "\",\"path\":\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    // ---- PR-GIT-149: worktreePath and branch are optional strings persisted with the library ----

    [Trait("Req", "PR-GIT-149")] // Core/Models/Library.swift:135-136
    [Fact]
    public void ChatThread_worktreePath_and_branch_are_optional_strings_that_round_trip()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised) with
        {
            WorktreePath = "C:\\work\\repo",
            Branch = "feature/x",
        };

        var bytes = Coding.Encode(thread);
        var decoded = Coding.Decode<ChatThread>(bytes)!;

        Assert.Equal("C:\\work\\repo", decoded.WorktreePath);
        Assert.Equal("feature/x", decoded.Branch);

        var withoutEither = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);
        Assert.Null(withoutEither.WorktreePath);
        Assert.Null(withoutEither.Branch);
    }

    // ---- PR-HYDRA-030 / PR-STORE-093: the per-chat Hydra migration ----

    [Trait("Req", "PR-HYDRA-030")] // Core/Models/Library.swift:256-260
    [Trait("Req", "PR-STORE-093")] // Core/Models/Library.swift:172-179, :256-260
    [Fact]
    public void A_thread_saved_before_the_switch_carries_an_ignored_false_and_decodes_hydra_enabled()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"hydraEnabled\":false");

        var thread = ChatThreadConverter.Decode(element);

        Assert.True(thread.HydraEnabled);
        Assert.True(thread.HydraEnabledIsExplicit);
    }

    [Trait("Req", "PR-HYDRA-030")] // Core/Models/Library.swift:256-260
    [Trait("Req", "PR-STORE-093")] // Core/Models/Library.swift:172-179, :256-260
    [Fact]
    public void A_thread_with_the_explicit_flag_true_honors_its_stored_hydraEnabled_value()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"hydraEnabledIsExplicit\":true,\"hydraEnabled\":false");

        var thread = ChatThreadConverter.Decode(element);

        Assert.False(thread.HydraEnabled);
        Assert.True(thread.HydraEnabledIsExplicit);
    }

    [Trait("Req", "PR-STORE-093")] // Core/Models/Library.swift:256-260
    [Fact]
    public void A_hand_edited_thread_with_explicit_true_and_no_stored_hydraEnabled_reads_as_on()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"hydraEnabledIsExplicit\":true");

        var thread = ChatThreadConverter.Decode(element);

        Assert.True(thread.HydraEnabled);
    }

    [Trait("Req", "PR-HYDRA-030")] // Core/Models/Library.swift:192, :188-192
    [Trait("Req", "PR-STORE-097")] // Core/Models/Library.swift:188-192
    [Fact]
    public void IsHelper_and_IsHydraHead_are_computed_never_persisted()
    {
        using var runtime = new TestRuntime();

        var noParentNoHydra = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);
        Assert.False(noParentNoHydra.IsHelper);
        Assert.False(noParentNoHydra.IsHydraHead);

        var helper = noParentNoHydra with { ParentThreadID = Guid.NewGuid() };
        Assert.True(helper.IsHelper);

        var head = noParentNoHydra with { Hydra = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) };
        Assert.True(head.IsHydraHead);

        // Neither computed property is a JSON key.
        var bytes = Coding.Encode(head);
        var document = JsonDocument.Parse(bytes);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("isHelper", keys);
        Assert.DoesNotContain("isHydraHead", keys);
    }

    // ---- PR-SIDEBAR-149 / PR-STORE-079 / PR-STORE-080: the project icon's storage format ----

    [Trait("Req", "PR-SIDEBAR-149")] // Core/Models/Library.swift:22-28
    [Trait("Req", "PR-STORE-079")] // Core/Models/Library.swift:3-5, :22-28, :51-54
    [Fact]
    public void ProjectIcon_storage_is_the_prefixed_string_for_each_case()
    {
        Assert.Equal("emoji:\U0001F528", new ProjectIcon.Emoji("\U0001F528").Storage);
        Assert.Equal("symbol:hammer", new ProjectIcon.Symbol("hammer").Storage);
    }

    [Trait("Req", "PR-SIDEBAR-149")] // Core/Models/Library.swift:30-41
    [Trait("Req", "PR-STORE-080")] // Core/Models/Library.swift:30-41
    [Fact]
    public void ProjectIcon_Create_parses_the_two_prefixes_and_falls_back_to_symbol()
    {
        var emoji = ProjectIcon.Create("emoji:x");
        Assert.IsType<ProjectIcon.Emoji>(emoji);
        Assert.Equal("x", emoji!.ProjectIconEmoji);

        var symbol = ProjectIcon.Create("symbol:hammer");
        Assert.IsType<ProjectIcon.Symbol>(symbol);
        Assert.Equal("hammer", symbol!.SymbolName);

        // A bare value with no recognised prefix is a symbol's name.
        var bare = ProjectIcon.Create("hammer");
        Assert.IsType<ProjectIcon.Symbol>(bare);
        Assert.Equal("hammer", bare!.SymbolName);
    }

    // ---- PR-STORE-081: an empty icon string ----

    [Trait("Req", "PR-STORE-081")] // Core/Models/Library.swift:32, :43-49
    [Fact]
    public void ProjectIcon_Create_returns_null_for_an_empty_string()
    {
        Assert.Null(ProjectIcon.Create(""));
    }

    [Trait("Req", "PR-STORE-081")] // Core/Models/Library.swift:43-49
    [Fact]
    public void An_empty_icon_string_throws_the_exact_data_corrupted_message()
    {
        var element = JsonDocument.Parse("\"\"").RootElement;

        var error = Assert.Throws<DecodingError>(() => ProjectIconConverter.Decode(element));

        Assert.Equal("An empty project icon", error.Message);
        Assert.Equal("dataCorrupted", error.Kind);
    }

    [Trait("Req", "PR-STORE-081")] // Core/Models/Library.swift:90 container.value(.icon, default: nil)
    [Fact]
    public void A_project_with_an_empty_icon_string_loads_with_no_icon()
    {
        var element = ProjectElement(Guid.NewGuid(), "/repo", "\"icon\":\"\"");

        var project = ProjectConverter.Decode(element);

        Assert.Null(project.Icon);
    }

    // ---- PR-SIDEBAR-156 / PR-STORE-087: the "New thread" constant, in all three roles ----

    [Trait("Req", "PR-SIDEBAR-156")] // Core/Models/Library.swift:122
    [Trait("Req", "PR-STORE-087")] // Core/Models/Library.swift:122, :207, :227
    [Fact]
    public void Untitled_is_exactly_New_thread()
    {
        Assert.Equal("New thread", ChatThread.Untitled);
    }

    [Trait("Req", "PR-SIDEBAR-156")] // Core/Models/Library.swift:207
    [Trait("Req", "PR-STORE-087")] // Core/Models/Library.swift:207
    [Fact]
    public void A_new_thread_starts_with_the_Untitled_title()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);
        Assert.Equal("New thread", thread.Title);
    }

    [Trait("Req", "PR-SIDEBAR-156")] // Core/Models/Library.swift:227
    [Trait("Req", "PR-STORE-087")] // Core/Models/Library.swift:227
    [Fact]
    public void A_thread_record_with_no_title_decodes_as_Untitled()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid());
        var thread = ChatThreadConverter.Decode(element);
        Assert.Equal("New thread", thread.Title);
    }

    [Trait("Req", "PR-SIDEBAR-156")] // Core/Models/Library.swift:227, :230
    [Fact]
    public void A_thread_record_with_a_missing_provider_decodes_as_codex()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid());
        var thread = ChatThreadConverter.Decode(element);
        Assert.Equal(ProviderKind.Codex, thread.Provider);
    }

    [Trait("Req", "PR-SIDEBAR-156")] // Core/Models/Library.swift:232-237
    [Fact]
    public void A_thread_record_with_a_missing_runtime_mode_decodes_as_supervised()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid());
        var thread = ChatThreadConverter.Decode(element);
        Assert.Equal(RuntimeMode.Supervised, thread.RuntimeMode);
    }

    // ---- PR-SIDEBAR-157 / PR-STORE-014 / PR-STORE-086: lenient library decoding ----

    [Trait("Req", "PR-SIDEBAR-157")] // Core/Models/Library.swift:266-277
    [Trait("Req", "PR-STORE-014")] // Core/Models/Support/Coding.swift:10-17
    [Fact]
    public void Library_decode_drops_a_corrupt_project_and_thread_but_keeps_the_rest_in_order()
    {
        var goodProject1 = Guid.NewGuid();
        var goodProject2 = Guid.NewGuid();
        var goodThread1 = Guid.NewGuid();
        var goodThread2 = Guid.NewGuid();
        var projectID = Guid.NewGuid();

        var json = $$"""
        {
            "projects": [
                {"id":"{{Uuid(goodProject1)}}","path":"/a"},
                {"path":"/missing-id"},
                {"id":"{{Uuid(goodProject2)}}","path":"/b"}
            ],
            "threads": [
                {"id":"{{Uuid(goodThread1)}}","projectID":"{{Uuid(projectID)}}"},
                {"id":"{{Uuid(goodThread2)}}"},
                {"projectID":"{{Uuid(projectID)}}"}
            ]
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        var library = LibraryConverter.Decode(element);

        Assert.Equal([goodProject1, goodProject2], library.Projects.Select(p => p.Id));
        Assert.Single(library.Threads);
        Assert.Equal(goodThread1, library.Threads[0].Id);
    }

    [Trait("Req", "PR-STORE-014")] // Core/Models/Support/Coding.swift:5-7
    [Fact]
    public void Library_decode_with_no_projects_or_threads_key_gives_empty_arrays()
    {
        var element = JsonDocument.Parse("{}").RootElement;

        var library = LibraryConverter.Decode(element);

        Assert.Empty(library.Projects);
        Assert.Empty(library.Threads);
    }

    [Trait("Req", "PR-SIDEBAR-157")] // Core/Models/Library.swift:81-84
    [Trait("Req", "PR-STORE-082")] // Core/Models/Library.swift:81-91
    [Fact]
    public void A_project_missing_its_id_or_path_fails_to_decode()
    {
        Assert.Throws<DecodingError>(() => ProjectConverter.Decode(JsonDocument.Parse("{\"path\":\"/a\"}").RootElement));
        Assert.Throws<DecodingError>(() => ProjectConverter.Decode(JsonDocument.Parse("{\"id\":\"" + Uuid(Guid.NewGuid()) + "\"}").RootElement));
    }

    [Trait("Req", "PR-SIDEBAR-157")] // Core/Models/Library.swift:85
    [Trait("Req", "PR-STORE-082")] // Core/Models/Library.swift:85
    [Fact]
    public void A_project_missing_its_name_falls_back_to_the_last_path_component()
    {
        var element = ProjectElement(Guid.NewGuid(), "C:\\repos\\droppy-code");
        var project = ProjectConverter.Decode(element);
        Assert.Equal(SwiftPath.LastPathComponent("C:\\repos\\droppy-code"), project.Name);
    }

    [Trait("Req", "PR-SIDEBAR-157")] // Core/Models/Library.swift:86
    [Trait("Req", "PR-STORE-082")] // Core/Models/Library.swift:86
    [Fact]
    public void A_project_missing_its_addedAt_falls_back_to_load_time()
    {
        using var runtime = new TestRuntime();
        var before = runtime.Time.GetUtcNow();

        var element = ProjectElement(Guid.NewGuid(), "/a");
        var project = ProjectConverter.Decode(element);

        Assert.Equal(before, project.AddedAt);
    }

    [Trait("Req", "PR-SIDEBAR-157")] // Core/Models/Library.swift:87
    [Trait("Req", "PR-STORE-082")] // Core/Models/Library.swift:87
    [Fact]
    public void A_project_missing_isExpanded_defaults_to_true()
    {
        var element = ProjectElement(Guid.NewGuid(), "/a");
        var project = ProjectConverter.Decode(element);
        Assert.True(project.IsExpanded);
    }

    [Trait("Req", "PR-STORE-082")] // Core/Models/Library.swift:88-90
    [Fact]
    public void A_project_missing_scripts_hydraPairID_and_icon_gets_their_declared_defaults()
    {
        var element = ProjectElement(Guid.NewGuid(), "/a");
        var project = ProjectConverter.Decode(element);
        Assert.Empty(project.Scripts);
        Assert.Null(project.HydraPairID);
        Assert.Null(project.Icon);
    }

    [Trait("Req", "PR-STORE-086")] // Core/Models/Library.swift:225-226
    [Fact]
    public void A_thread_missing_its_id_or_projectID_fails_to_decode()
    {
        Assert.Throws<DecodingError>(() => ChatThreadConverter.Decode(JsonDocument.Parse("{\"projectID\":\"" + Uuid(Guid.NewGuid()) + "\"}").RootElement));
        Assert.Throws<DecodingError>(() => ChatThreadConverter.Decode(JsonDocument.Parse("{\"id\":\"" + Uuid(Guid.NewGuid()) + "\"}").RootElement));
    }

    // ---- PR-STORE-083: a freshly added project ----

    [Trait("Req", "PR-STORE-083")] // Core/Models/Library.swift:72-79
    [Fact]
    public void A_new_project_starts_expanded_with_no_scripts_and_no_hydraPairID_or_icon()
    {
        using var runtime = new TestRuntime();
        var before = runtime.Time.GetUtcNow();

        var project = new Project("droppy-code", "/repo/droppy-code");

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("droppy-code", project.Name);
        Assert.Equal("/repo/droppy-code", project.Path);
        Assert.Equal(before, project.AddedAt);
        Assert.True(project.IsExpanded);
        Assert.Empty(project.Scripts);
        Assert.Null(project.HydraPairID);
        Assert.Null(project.Icon);
    }

    // ---- PR-STORE-084: project scripts ----

    [Trait("Req", "PR-STORE-084")] // Core/Models/Library.swift:111-118
    [Fact]
    public void A_script_missing_its_id_gets_a_fresh_uuid_symbol_and_worktree_flag_default()
    {
        var element = JsonDocument.Parse("{\"name\":\"Build\",\"command\":\"dotnet build\"}").RootElement;

        var script = ProjectScriptConverter.Decode(element);

        Assert.NotEqual(Guid.Empty, script.Id);
        Assert.Equal("Build", script.Name);
        Assert.Equal("dotnet build", script.Command);
        Assert.Equal("play", script.Symbol);
        Assert.False(script.RunOnWorktreeCreate);
    }

    [Trait("Req", "PR-STORE-084")] // Core/Models/Library.swift:113-117
    [Fact]
    public void A_script_missing_its_name_or_command_fails_to_decode()
    {
        Assert.Throws<DecodingError>(() => ProjectScriptConverter.Decode(JsonDocument.Parse("{\"command\":\"x\"}").RootElement));
        Assert.Throws<DecodingError>(() => ProjectScriptConverter.Decode(JsonDocument.Parse("{\"name\":\"x\"}").RootElement));
    }

    [Trait("Req", "PR-STORE-084")] // Core/Models/Library.swift:88; Support/Coding.swift:5-7
    [Fact]
    public void A_malformed_script_degrades_scripts_to_empty_but_does_not_drop_the_project()
    {
        // See the file header note: this proves the pinned source's actual behaviour (Coding.swift:5-7's
        // try?-and-fallback wraps the whole "scripts" array read, not each element), which differs from
        // PR-STORE-084's failure_cases prose.
        var element = ProjectElement(Guid.NewGuid(), "/a", "\"scripts\":[{\"command\":\"missing name\"}]");

        var project = ProjectConverter.Decode(element);

        Assert.Empty(project.Scripts);
        Assert.Equal("/a", project.Path);
    }

    // ---- PR-STORE-088: updatedAt inherits createdAt ----

    [Trait("Req", "PR-STORE-088")] // Core/Models/Library.swift:228-229
    [Fact]
    public void A_thread_missing_updatedAt_inherits_the_already_decoded_createdAt()
    {
        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"createdAt\":\"2020-01-01T00:00:00Z\"");

        var thread = ChatThreadConverter.Decode(element);

        Assert.Equal(thread.CreatedAt, thread.UpdatedAt);
        Assert.Equal(DateTimeOffset.Parse("2020-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), thread.UpdatedAt);
    }

    [Trait("Req", "PR-STORE-088")] // Core/Models/Library.swift:228-229
    [Fact]
    public void A_thread_missing_both_dates_gets_load_time_for_both()
    {
        using var runtime = new TestRuntime();
        var before = runtime.Time.GetUtcNow();

        var element = ThreadElement(Guid.NewGuid(), Guid.NewGuid());
        var thread = ChatThreadConverter.Decode(element);

        Assert.Equal(before, thread.CreatedAt);
        Assert.Equal(before, thread.UpdatedAt);
    }

    // ---- PR-STORE-089: a corrupt runtimeMode lands on Supervised ----

    [Trait("Req", "PR-STORE-089")] // Core/Models/Library.swift:234-237
    [Fact]
    public void RuntimeMode_absent_null_or_wrong_type_reads_as_supervised()
    {
        var absent = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(RuntimeMode.Supervised, absent.RuntimeMode);

        var nullValue = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"runtimeMode\":null"));
        Assert.Equal(RuntimeMode.Supervised, nullValue.RuntimeMode);

        var wrongType = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"runtimeMode\":42"));
        Assert.Equal(RuntimeMode.Supervised, wrongType.RuntimeMode);

        // A valid value is honoured, distinguishing this from an unconditional Supervised default.
        var full = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"runtimeMode\":\"fullAccess\""));
        Assert.Equal(RuntimeMode.FullAccess, full.RuntimeMode);
    }

    // ---- PR-STORE-090: a corrupt provider lands on codex ----

    [Trait("Req", "PR-STORE-090")] // Core/Models/Library.swift:230
    [Fact]
    public void Provider_absent_or_unrecognized_reads_as_codex()
    {
        var absent = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(ProviderKind.Codex, absent.Provider);

        var unrecognized = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"provider\":\"not-a-provider\""));
        Assert.Equal(ProviderKind.Codex, unrecognized.Provider);
    }

    // ---- PR-STORE-091: model decodes to nil, distinct from "" ----

    [Trait("Req", "PR-STORE-091")] // Core/Models/Library.swift:231
    [Fact]
    public void Model_absent_null_or_wrong_type_decodes_to_nil_never_empty_string()
    {
        var absent = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Null(absent.Model);

        var nullValue = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"model\":null"));
        Assert.Null(nullValue.Model);

        var wrongType = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"model\":7"));
        Assert.Null(wrongType.Model);

        var empty = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid(), "\"model\":\"\""));
        Assert.Equal("", empty.Model);
        Assert.NotNull(empty.Model);
    }

    // ---- PR-STORE-092: the three manual orderings ----

    [Trait("Req", "PR-STORE-092")] // Core/Models/Library.swift:148-153, :249-251
    [Fact]
    public void SortOrder_activityOrder_and_activityOrderDay_round_trip_and_default_to_nil()
    {
        using var runtime = new TestRuntime();

        var withoutThem = ChatThreadConverter.Decode(ThreadElement(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Null(withoutThem.SortOrder);
        Assert.Null(withoutThem.ActivityOrder);
        Assert.Null(withoutThem.ActivityOrderDay);

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised) with
        {
            SortOrder = 1.5,
            ActivityOrder = 2.5,
            ActivityOrderDay = DateTimeOffset.Parse("2024-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };

        var decoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;

        Assert.Equal(1.5, decoded.SortOrder);
        Assert.Equal(2.5, decoded.ActivityOrder);
        Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), decoded.ActivityOrderDay);
    }

    // ---- PR-STORE-094: the per-chat Hydra default preference ----

    [Trait("Req", "PR-STORE-094")] // Core/Models/Library.swift:194-202
    [Fact]
    public void HydraDefaultEnabled_reads_the_exact_key_absent_or_non_bool_is_true()
    {
        using var runtime = new TestRuntime();

        Assert.True(ChatThread.HydraDefaultEnabled());

        runtime.Defaults.Set("hydraDefaultEnabled", "not-a-bool");
        Assert.True(ChatThread.HydraDefaultEnabled());

        runtime.Defaults.Set(ChatThread.HydraDefaultKey, false);
        Assert.False(ChatThread.HydraDefaultEnabled());

        runtime.Defaults.Set(ChatThread.HydraDefaultKey, true);
        Assert.True(ChatThread.HydraDefaultEnabled());
    }

    [Trait("Req", "PR-STORE-094")] // Core/Models/Library.swift:220
    [Fact]
    public void A_new_chat_starts_with_the_saved_per_chat_hydra_default()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set(ChatThread.HydraDefaultKey, false);

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

        Assert.False(thread.HydraEnabled);
    }

    // ---- PR-STORE-095: a new chat's exact initial values ----

    [Trait("Req", "PR-STORE-095")] // Core/Models/Library.swift:204-221
    [Fact]
    public void A_new_chat_is_created_unpinned_unarchived_read_with_no_custom_title_and_in_build_mode()
    {
        using var runtime = new TestRuntime();
        var projectID = Guid.NewGuid();

        var thread = new ChatThread(projectID, ProviderKind.Claude, "claude-opus", "high", RuntimeMode.FullAccess);

        Assert.NotEqual(Guid.Empty, thread.Id);
        Assert.Equal(projectID, thread.ProjectID);
        Assert.Equal("New thread", thread.Title);
        Assert.Equal(thread.CreatedAt, thread.UpdatedAt);
        Assert.Equal(ProviderKind.Claude, thread.Provider);
        Assert.Equal("claude-opus", thread.Model);
        Assert.Equal("high", thread.Effort);
        Assert.False(thread.FastMode);
        Assert.Equal(RuntimeMode.FullAccess, thread.RuntimeMode);
        Assert.Equal(InteractionMode.Build, thread.InteractionMode);
        Assert.False(thread.IsPinned);
        Assert.False(thread.IsArchived);
        Assert.False(thread.HasUnread);
        Assert.False(thread.HasCustomTitle);
        Assert.False(thread.IsSettled);
        Assert.False(thread.IsInPanel);
        Assert.False(thread.ExpandsHelpers);
        Assert.True(thread.HydraEnabledIsExplicit);
        Assert.Equal(0, thread.HydraSpawnCount);
    }

    [Trait("Req", "PR-STORE-095")] // Core/Models/Library.swift:204-221
    [Fact]
    public void FastMode_defaults_to_false_when_the_caller_omits_it()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised);
        Assert.False(thread.FastMode);
    }

    // ---- PR-STORE-096: expandsHelpers persists, foldsHelpers is its inverse ----

    [Trait("Req", "PR-STORE-096")] // Core/Models/Library.swift:161-170
    [Fact]
    public void ExpandsHelpers_defaults_to_false_and_foldsHelpers_is_its_inverse_in_both_directions()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);
        Assert.False(thread.ExpandsHelpers);
        Assert.True(thread.FoldsHelpers);

        var expanded = thread.WithFoldsHelpers(false);
        Assert.True(expanded.ExpandsHelpers);
        Assert.False(expanded.FoldsHelpers);

        var folded = expanded.WithFoldsHelpers(true);
        Assert.False(folded.ExpandsHelpers);
    }

    [Trait("Req", "PR-STORE-096")] // Core/Models/Library.swift:161-170
    [Fact]
    public void FoldsHelpers_is_never_a_persisted_key()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

        var bytes = Coding.Encode(thread);
        var document = JsonDocument.Parse(bytes);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Contains("expandsHelpers", keys);
        Assert.DoesNotContain("foldsHelpers", keys);
    }

    // ---- PR-STORE-097: isInPanel is persisted; isHelper/isHydraHead are not ----

    [Trait("Req", "PR-STORE-097")] // Core/Models/Library.swift:162-166
    [Fact]
    public void IsInPanel_is_a_persisted_boolean_that_round_trips()
    {
        using var runtime = new TestRuntime();

        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised) with { IsInPanel = true };

        var decoded = Coding.Decode<ChatThread>(Coding.Encode(thread))!;

        Assert.True(decoded.IsInPanel);
    }
}
