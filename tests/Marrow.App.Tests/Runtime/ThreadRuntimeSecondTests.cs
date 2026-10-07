using System.Reflection;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeSecondTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-021")] // App/Runtime/ThreadRuntime.swift:510-526, :1446-1458
    public void Session_signature_has_nine_ordered_value_fields_and_each_field_changes_equality()
    {
        var signatureType = typeof(ThreadRuntime).GetNestedType("SessionSignature", BindingFlags.NonPublic);
        Assert.NotNull(signatureType);
        var names = signatureType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            ["Provider", "Directory", "LaunchRuntimeMode", "LaunchEffort", "LaunchFast", "LaunchModel", "LaunchInteraction", "Hydra", "AntiSlopEnabled"],
            names);

        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true, WorkerModel = "worker-a" };
        var baselineValues = new Dictionary<string, object?>
        {
            ["Provider"] = ProviderKind.Codex,
            ["Directory"] = @"C:\repo",
            ["LaunchRuntimeMode"] = RuntimeMode.Auto,
            ["LaunchEffort"] = "medium",
            ["LaunchFast"] = true,
            ["LaunchModel"] = "model-a",
            ["LaunchInteraction"] = InteractionMode.Plan,
            ["Hydra"] = launch,
            ["AntiSlopEnabled"] = true,
        };
        var baseline = MakeSignature(signatureType, baselineValues);
        Assert.Equal(baseline, MakeSignature(signatureType, baselineValues));

        var alternatives = new Dictionary<string, object?>
        {
            ["Provider"] = ProviderKind.Claude,
            ["Directory"] = @"C:\other",
            ["LaunchRuntimeMode"] = RuntimeMode.FullAccess,
            ["LaunchEffort"] = "high",
            ["LaunchFast"] = false,
            ["LaunchModel"] = "model-b",
            ["LaunchInteraction"] = InteractionMode.Build,
            ["Hydra"] = launch with { WorkerModel = "worker-b" },
            ["AntiSlopEnabled"] = false,
        };
        foreach (var (field, alternative) in alternatives)
        {
            var changed = new Dictionary<string, object?>(baselineValues) { [field] = alternative };
            Assert.NotEqual(baseline, MakeSignature(signatureType, changed));
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-244")] // ThreadRuntime.swift:1430-1450
    public void A_running_provider_session_signature_tracks_its_supported_hydra_launch()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("hydra session signature"));
        var thread = AppModelSamples.Thread(project) with
        {
            Provider = ProviderKind.Meta,
            HydraEnabled = true,
        };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var launch = h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            return model.HydraLaunch(thread);
        });
        Assert.NotNull(launch);
        var signature = InvokePrivateStatic("MakeSessionSignature", thread, project.Path,
            model.Settings.AntiSlopEnabled, launch);
        var existing = new TestProviderSession();
        var runtimeType = typeof(ThreadRuntime);
        var sessionField = runtimeType.GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var signatureField = runtimeType.GetField("sessionSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
        h.Main.OnMain(() =>
        {
            sessionField.SetValue(runtime, existing);
            signatureField.SetValue(runtime, signature);
        });

        var ensureSession = runtimeType.GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = h.Main.OnMain(() => (Task)ensureSession.Invoke(runtime, [project.Path, true])!);
        h.Main.Loop.Run(() => task, AppModelHarness.Bound);

        Assert.Same(existing, task.GetType().GetProperty("Result")!.GetValue(task));
    }
    [Fact]
    [Trait("Req", "PR-RUNTIME-022")] // App/Runtime/ThreadRuntime.swift:1388-1390
    public void Session_prompt_policy_includes_each_API_key_provider()
    {
        foreach (var provider in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai })
            Assert.True(InvokePolicyPredicate(provider));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-060")] // App/Runtime/ThreadRuntime.swift:1388-1390; App/AppModel+Hydra.swift:141-143; Core/Models/Provider.swift:101
    public void Session_prompt_policy_uses_the_exact_native_or_API_key_provider_union()
    {
        var expected = new HashSet<ProviderKind>
        {
            ProviderKind.Claude,
            ProviderKind.Codex,
            ProviderKind.Copilot,
            ProviderKind.Deepseek,
            ProviderKind.Meta,
            ProviderKind.Zai,
            ProviderKind.OpenRouter, // ED-234: an API-key provider like the three above
            ProviderKind.Local, // ED-410: keyless native session
        };

        foreach (var provider in Enum.GetValues<ProviderKind>())
            Assert.True(expected.Contains(provider) == InvokePolicyPredicate(provider), $"Provider: {provider}.");
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-036")] // App/Runtime/ThreadRuntime.swift:1622-1625
    public void Starts_over_only_when_a_resume_can_be_replaced_without_losing_its_anchor()
    {
        using var test = new TestRuntime();
        var plainError = new LocalizedTestException("temporary provider failure");
        var lostConversation = new LocalizedTestException("NO CONVERSATION FOUND with session ID");
        var noResume = Thread(ProviderSessionID: "session-1");
        var anchored = Thread(ProviderSessionID: "session-1", ProviderResumeAt: "message-8");

        Assert.False(InvokeStartsOver(plainError, Thread()));
        Assert.True(InvokeStartsOver(lostConversation, anchored));
        Assert.True(InvokeStartsOver(plainError, noResume));
        Assert.False(InvokeStartsOver(plainError, anchored));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-037")] // App/Runtime/ThreadRuntime.swift:1627-1633
    public void Lost_conversation_detection_matches_only_the_two_case_insensitive_phrases()
    {
        Assert.True(InvokeLostConversation(new LocalizedTestException("No Conversation Found with session ID")));
        Assert.True(InvokeLostConversation(new LocalizedTestException("The provider no longer has this conversation.")));
        Assert.False(InvokeLostConversation(new LocalizedTestException("conversation not found")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-145")] // App/Runtime/ThreadRuntime.swift:2013-2046
    public void Parsed_diff_uses_the_first_base_before_the_last_selected_end_checkpoint()
    {
        using var h = new AppModelHarness();
        var checkout = h.NewFolder("diff-span");
        var repo = AppModelGitRepo.Empty(checkout);
        var project = AppModelSamples.Project("P", checkout);
        var thread = AppModelSamples.Thread(project);

        File.WriteAllText(Path.Combine(checkout, "a.txt"), "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "base");
        var firstBase = repo.Git("rev-parse", "HEAD").Trim();

        File.WriteAllText(Path.Combine(checkout, "b.txt"), "b\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "end");
        var lastEnd = repo.Git("rev-parse", "HEAD").Trim();

        var earlier = Turn(index: 0, baseCheckpoint: firstBase);
        var selectedEnd = Turn(index: 1, baseCheckpoint: lastEnd, endCheckpoint: lastEnd, touchedPaths: ["b.txt"]);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([earlier, selectedEnd]),
        });

        var files = h.Main.Loop.Run(async () => await runtime.ParsedDiff(null), AppModelHarness.Bound);

        Assert.Equal(["b.txt"], files.Select(file => file.Path).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-GIT-145")] // App/Runtime/ThreadRuntime.swift:2042-2046
    public void Parsed_diff_falls_back_to_selected_provider_patch_and_skips_it_when_files_were_supplied()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("provider-diff"));
        var thread = AppModelSamples.Thread(project);
        const string patch = "diff --git a/src/new.txt b/src/new.txt\nnew file mode 100644\n--- /dev/null\n+++ b/src/new.txt\n@@ -0,0 +1 @@\n+new\n";
        var fromPatch = Turn(index: 0, providerDiff: patch);
        var skippedPatch = Turn(index: 1, providerDiff: patch);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([fromPatch, skippedPatch]),
        });

        var parsed = h.Main.Loop.Run(async () => await runtime.ParsedDiff(fromPatch.Id), AppModelHarness.Bound);
        var supplied = h.Main.Loop.Run(async () => await runtime.ParsedDiff(skippedPatch.Id, Array.Empty<DiffFile>()), AppModelHarness.Bound);

        var file = Assert.Single(parsed);
        Assert.Equal("src/new.txt", file.Path);
        Assert.Equal(1, file.Additions);
        Assert.Empty(supplied);
    }

    [Fact]
    [Trait("Req", "PR-GIT-146")] // App/Runtime/ThreadRuntime.swift:2022-2030, :2052-2056
    public void Parsed_diff_keeps_edits_inside_any_sidebar_project_and_drops_unrelated_paths()
    {
        using var h = new AppModelHarness();
        var ownRoot = h.NewFolder("own-project");
        var sidebarRoot = h.NewFolder("sidebar-project");
        var project = AppModelSamples.Project("Own", ownRoot);
        var otherProject = AppModelSamples.Project("Sidebar", sidebarRoot);
        var thread = AppModelSamples.Thread(project);
        var turn = Turn(index: 0);
        var call = new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit,
            Title = "Edit files",
            ToolCallStatus = ToolCall.Status.Completed,
            Edits = new ValueList<FileEdit>(
            [
                new(Path.Combine(ownRoot, "own.cs"), Additions: 2, Deletions: 1),
                new(Path.Combine(sidebarRoot, "sidebar.cs"), Additions: 3, Deletions: 0),
                new(Path.Combine(h.Scratch, "agent-note.md"), Additions: 1, Deletions: 0),
            ]),
        };
        var item = new TimelineItem(turn.Id, new TimelineItem.Content.Tool(call));
        var runtime = LoadRuntime(h, [project, otherProject], thread, new ThreadDocument(thread.Id)
        {
            Items = new ValueList<TimelineItem>([item]),
            Turns = new ValueList<TurnRecord>([turn]),
        });

        var files = h.Main.Loop.Run(async () => await runtime.ParsedDiff(turn.Id, Array.Empty<DiffFile>()), AppModelHarness.Bound);

        var names = files.Select(file => Path.GetFileName(file.Path)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(["own.cs", "sidebar.cs"], names);
    }

    [Fact]
    [Trait("Req", "PR-GIT-147")] // App/Runtime/ThreadRuntime.swift:3700-3723
    public void Rehearsed_turn_replaces_the_current_touched_paths_and_keeps_provider_patch_separate()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("rehearsal"));
        var thread = AppModelSamples.Thread(project);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id));

        h.Main.OnMain(() => runtime.RehearseTurn("done", ["src/a.cs", "src/b.cs"], "repository-wide provider patch"));

        var turn = Assert.Single(runtime.Turns);
        Assert.Equal(["src/a.cs", "src/b.cs"], turn.TouchedPaths!.ToArray());
        Assert.Equal("repository-wide provider patch", turn.ProviderDiff);
        Assert.Equal(new[] { "src/a.cs", "src/b.cs" }.ToHashSet(StringComparer.Ordinal), runtime.HydraCurrentTurnTouchedPaths.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-079")] // App/Runtime/ThreadRuntime.swift:2494-2504; Core/Models/Timeline.swift:304-306
    public void Mark_hydra_merged_persists_only_new_marks_and_does_not_write_for_a_no_op()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("merge-marks"));
        var thread = AppModelSamples.Thread(project);
        var unmerged = Turn(index: 0);
        var alreadyMerged = Turn(index: 1, hydraMerged: true);
        var untouched = Turn(index: 2);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>([unmerged, alreadyMerged, untouched]),
        });

        h.Main.OnMain(() => runtime.MarkHydraMerged([unmerged.Id, alreadyMerged.Id, unmerged.Id, Guid.NewGuid()]));

        Assert.All(runtime.Turns.Take(2), turn => Assert.True(turn.HydraMerged));
        Assert.False(runtime.Turns[2].HydraMerged);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.All(Storage.DecodeDocument(thread.Id)!.Turns.Take(2), turn => Assert.True(turn.HydraMerged));

        var sentinel = System.Text.Encoding.UTF8.GetBytes("unchanged on a no-op");
        File.WriteAllBytes(Storage.ThreadURL(thread.Id), sentinel);
        h.Main.OnMain(() => runtime.MarkHydraMerged([unmerged.Id, alreadyMerged.Id]));

        Assert.Equal(sentinel, File.ReadAllBytes(Storage.ThreadURL(thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-080")] // App/Runtime/ThreadRuntime.swift:2512-2517; Core/Models/Timeline.swift:347, :363
    public void Recording_merges_keeps_and_persists_only_the_newest_twenty()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("merge-history"));
        var thread = AppModelSamples.Thread(project);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id));
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        h.Main.OnMain(() =>
        {
            for (var index = 0; index < 21; index++)
            {
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = at.AddMinutes(index),
                    HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged,
                    Files = index,
                    Detail = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
        });

        Assert.Equal(Enumerable.Range(1, 20).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            runtime.HydraMerges.Select(record => record.Detail));
        var saved = Assert.IsType<ThreadDocument>(Storage.DecodeDocument(thread.Id));
        Assert.Equal(Enumerable.Range(1, 20).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            saved.HydraMerges.Select(record => record.Detail));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-082")] // App/Runtime/ThreadRuntime.swift:2507-2522
    public void Unmerged_file_count_is_distinct_and_excludes_running_or_already_merged_turns()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("unmerged-count"));
        var thread = AppModelSamples.Thread(project);
        var runtime = LoadRuntime(h, [project], thread, new ThreadDocument(thread.Id)
        {
            Turns = new ValueList<TurnRecord>(
            [
                Turn(index: 0, status: TurnStatus.Completed, touchedPaths: ["a.cs", "shared.cs"]),
                Turn(index: 1, status: TurnStatus.Failed, touchedPaths: ["b.cs", "shared.cs"]),
                Turn(index: 3, status: TurnStatus.Completed, touchedPaths: ["merged.cs"], hydraMerged: true),
            ]),
        });
        h.Main.OnMain(() => runtime.RehearseTurn(null, ["running.cs"]));

        Assert.Equal(
            new[] { TurnStatus.Completed, TurnStatus.Failed, TurnStatus.Completed, TurnStatus.Running },
            runtime.Turns.Select(turn => turn.Status));
        Assert.Equal(new[] { false, false, true, false }, runtime.Turns.Select(turn => turn.HydraMerged));
        Assert.Equal(3, runtime.HydraUnmergedFileCount);
    }

    private static object MakeSignature(Type signatureType, IReadOnlyDictionary<string, object?> values)
    {
        var signature = Activator.CreateInstance(signatureType, nonPublic: true)!;
        foreach (var (name, value) in values)
            signatureType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(signature, value);
        return signature;
    }

    private static bool InvokePolicyPredicate(ProviderKind provider) =>
        (bool)InvokePrivateStatic("KeepsHydraPolicyInSystemPrompt", provider)!;

    private static bool InvokeStartsOver(Exception error, ChatThread thread) =>
        (bool)InvokePrivateStatic("StartsOver", error, thread)!;

    private static bool InvokeLostConversation(Exception error) =>
        (bool)InvokePrivateStatic("LostConversation", error)!;

    private static object? InvokePrivateStatic(string name, params object?[] arguments)
    {
        var method = typeof(ThreadRuntime).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, arguments);
    }

    private static ChatThread Thread(string? ProviderSessionID = null, string? ProviderResumeAt = null)
    {
        var projectID = Guid.NewGuid();
        return new ChatThread(projectID, ProviderKind.Codex, model: null, effort: null, RuntimeMode.Supervised)
        {
            ProviderSessionID = ProviderSessionID,
            ProviderResumeAt = ProviderResumeAt,
        };
    }

    private static TurnRecord Turn(
        long index,
        string? baseCheckpoint = null,
        string? endCheckpoint = null,
        string? providerDiff = null,
        IReadOnlyList<string>? touchedPaths = null,
        TurnStatus status = TurnStatus.Completed,
        bool hydraMerged = false) => new()
        {
            Index = index,
            BaseCheckpoint = baseCheckpoint,
            EndCheckpoint = endCheckpoint,
            ProviderDiff = providerDiff,
            TouchedPaths = touchedPaths is null ? null : new ValueList<string>(touchedPaths),
            Status = status,
            HydraMerged = hydraMerged,
        };

    private static ThreadRuntime LoadRuntime(
        AppModelHarness h,
        IReadOnlyList<Project> projects,
        ChatThread thread,
        ThreadDocument document)
    {
        h.WriteLibrary(projects, [thread]);
        DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(thread.Id));
        var app = h.MakeModel();
        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return runtime;
    }
}
