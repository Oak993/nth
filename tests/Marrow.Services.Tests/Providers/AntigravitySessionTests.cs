using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/AntigravitySession.swift (B2 1fbf81e). Part 1: PR-PROVCLI-318
// and 320 to 342 (the argument vector, normalize, the mode flags, resume, start, send, interrupt, compact, stop, the
// stream handlers, the close path, the catalog pipeline, auth status and plan limits). Wire behaviour is proven
// against the compiled offline stand-in tests/fixtures/antigravity-cli (R-TEST-8, R-TEST-10), which records every
// argument vector and stdin line; the stream handlers and the pure helpers the Swift keeps private are exercised
// directly by reflection, without a process.
public sealed class AntigravitySessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string MiddleDot = "\U000000B7"; // U+00B7 MIDDLE DOT

    // A non-"file" scheme keeps the Windows path unchanged through SwiftURL.Path (the ClaudeSessionTests note).
    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;

    private static SessionConfiguration Config(
        AntigravityScript? script,
        string? resumeID = null,
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        string? workingDirectory = null) => new()
        {
            Provider = ProviderKind.Antigravity,
            Executable = script is null ? null : FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(workingDirectory ?? (script is null ? Scratch() : Path.GetDirectoryName(script.ExecutablePath)!)),
            Environment = script is null ? new ValueMap<string, string>([]) : new ValueMap<string, string>([script.EnvironmentEntry]),
            ResumeID = resumeID,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
        };

    /// <summary>A fresh folder under MARROW_PORT_DATA_DIR (R-TEST-9).</summary>
    private static string Scratch()
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "AntigravitySessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static (AntigravitySession Session, List<ProviderEvent> Events) Make(SessionConfiguration configuration)
    {
        var session = new AntigravitySession(configuration);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    private const string Init = "{\"event\":\"init\",\"conversation_id\":\"conv-1\"}";

    private static (AntigravitySession Session, List<ProviderEvent> Events) Started(TestRuntime runtime, SessionConfiguration configuration)
    {
        var (session, events) = Make(configuration);
        runtime.Loop.Run(() => session.Start(), Bound);
        return (session, events);
    }

    private static TurnInput Turn(string text, RuntimeMode runtimeMode = RuntimeMode.Supervised, InteractionMode interactionMode = InteractionMode.Build,
        string? model = null, string? effort = null, ValueList<Attachment>? images = null) => new()
        {
            Text = text,
            Images = images ?? ValueList<Attachment>.Empty,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
        };

    private static T Field<T>(AntigravitySession session, string name) => (T)typeof(AntigravitySession).GetField(name, Instance)!.GetValue(session)!;

    private static void SetField(AntigravitySession session, string name, object? value) =>
        typeof(AntigravitySession).GetField(name, Instance)!.SetValue(session, value);

    private static object? InvokeStatic(string method, params object?[] args)
    {
        try
        {
            return typeof(AntigravitySession).GetMethod(method, Static)!.Invoke(null, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static object? Invoke(AntigravitySession session, string method, params object?[] args)
    {
        try
        {
            return typeof(AntigravitySession).GetMethod(method, Instance)!.Invoke(session, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static JSONValue Json(string text) => JSONValue.Parse(text) ?? throw new ArgumentException("Not JSON: " + text);

    /// <summary>The private stream dispatcher (AntigravitySession.swift:194-211), run on main as the read task does.</summary>
    private static void Handle(TestRuntime runtime, AntigravitySession session, string line) =>
        runtime.OnMain(() => Invoke(session, "Handle", Json(line)));

    private static void DidClose(TestRuntime runtime, AntigravitySession session) =>
        runtime.OnMain(() => Invoke(session, "DidClose"));

    private static string LaunchID(AntigravitySession session) => Field<string>(session, "launchID");

    private static IReadOnlyList<string> Base(string workingDirectory) =>
        ["--input-format", "stream-json", "--output-format", "stream-json", "--print-timeout", "30m", "--add-dir", workingDirectory];

    private static IReadOnlyList<string> ModeArguments(RuntimeMode runtimeMode, InteractionMode interaction)
    {
        var (session, _) = Make(Config(null));
        return (IReadOnlyList<string>)Invoke(session, "ModeArguments", runtimeMode, interaction)!;
    }

    private static PlanLimits.Window W(string id, string title, double percent, DateTimeOffset? resetsAt = null) =>
        new() { Id = id, Title = title, Percent = percent, ResetsAt = resetsAt };

    private static ValueList<PlanLimits.Window> ParseUsage(JSONValue? data, string? fallback) =>
        (ValueList<PlanLimits.Window>)InvokeStatic("ParseUsage", data, fallback)!;

    private static PlanLimits.Window? ParseCredits(JSONValue? data, string? fallback) =>
        (PlanLimits.Window?)InvokeStatic("ParseCredits", data, fallback);

    // ---- PR-PROVCLI-318 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-318")] // Services/Providers/AntigravitySession.swift:72-81,96-101
    public void Start_launches_agy_with_the_eight_base_tokens_ending_in_the_working_directory()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("argv");
        script.SetInit(Init);
        var configuration = Config(script);
        Started(runtime, configuration);
        Assert.Equal(Base(configuration.WorkingDirectory.Path), script.Calls().Single());
        Assert.Equal("30m", script.Calls().Single()[5]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-318")] // Services/Providers/AntigravitySession.swift:66,80,99
    public void A_working_directory_with_spaces_reaches_agy_as_one_add_dir_argument()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("spaces");
        script.SetInit(Init);
        var folder = Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "work dir with spaces");
        Directory.CreateDirectory(folder);
        var configuration = Config(script, workingDirectory: folder);
        Started(runtime, configuration);
        var call = script.Calls().Single();
        Assert.Equal(8, call.Count);
        Assert.Equal("--add-dir", call[6]);
        Assert.Equal(configuration.WorkingDirectory.Path, call[7]);
    }

    // ---- PR-PROVCLI-320 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:52-53
    public void The_efforts_agy_accepts_are_low_medium_high()
    {
        Assert.Equal(["low", "medium", "high"], AntigravitySession.Efforts);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:419
    public void Normalize_returns_no_model_and_no_effort_for_a_nil_or_empty_model()
    {
        Assert.Equal((null, null), AntigravitySession.Normalize(null, "high"));
        Assert.Equal((null, null), AntigravitySession.Normalize("", "low"));
        Assert.Equal((null, null), AntigravitySession.Normalize(null, null));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:400-413
    public void ParseModelRow_strips_an_effort_suffix_tested_high_then_medium_then_low_and_its_display_suffix()
    {
        Assert.Equal(new AntigravitySession.ParsedModelRow("gemini-3.1-pro", "Gemini 3.1 Pro", "high"),
            AntigravitySession.ParseModelRow("gemini-3.1-pro-high", "Gemini 3.1 Pro (High)"));
        Assert.Equal(new AntigravitySession.ParsedModelRow("gemini-3-flash", "Gemini 3 Flash", "medium"),
            AntigravitySession.ParseModelRow("gemini-3-flash-medium", "Gemini 3 Flash (Medium)"));
        Assert.Equal(new AntigravitySession.ParsedModelRow("gemini-3-flash", "Gemini 3 Flash", "low"),
            AntigravitySession.ParseModelRow("gemini-3-flash-low", "Gemini 3 Flash (Low)"));
        // The display suffix is stripped only when it matches the slug's effort.
        Assert.Equal(new AntigravitySession.ParsedModelRow("m", "M (High)", "low"), AntigravitySession.ParseModelRow("m-low", "M (High)"));
        // "-high" is tested before "-medium": only the last suffix goes.
        Assert.Equal(new AntigravitySession.ParsedModelRow("m-medium", "", "high"), AntigravitySession.ParseModelRow("m-medium-high", ""));
        Assert.Equal(new AntigravitySession.ParsedModelRow("plain", "Plain", null), AntigravitySession.ParseModelRow("plain", "Plain"));
        // A bare "high" has no "-high" suffix.
        Assert.Equal(new AntigravitySession.ParsedModelRow("high", "High", null), AntigravitySession.ParseModelRow("high", "High"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:423-426
    public void Normalize_passes_no_effort_for_a_base_slug_containing_claude()
    {
        Assert.Equal(("claude-sonnet-4.5", (string?)null), AntigravitySession.Normalize("claude-sonnet-4.5-high", "low"));
        Assert.Equal(("my-claude-model", (string?)null), AntigravitySession.Normalize("my-claude-model", "high"));
        // Ordinal: an upper-case "Claude" is not the substring "claude".
        Assert.Equal(("Claude-x", "high"), AntigravitySession.Normalize("Claude-x", null));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:428-434
    public void Normalize_resolves_the_effort_explicit_first_then_parsed_then_the_default()
    {
        Assert.Equal(("gemini-3-flash", "medium"), AntigravitySession.Normalize("gemini-3-flash-low", "medium"));
        Assert.Equal(("gemini-3-flash", "low"), AntigravitySession.Normalize("gemini-3-flash-low", null));
        Assert.Equal(("gemini-3-flash", "low"), AntigravitySession.Normalize("gemini-3-flash-low", ""));
        Assert.Equal(("gemini-3-flash", "high"), AntigravitySession.Normalize("gemini-3-flash", null));
        Assert.Equal(("gemini-3-flash", "high"), AntigravitySession.Normalize("gemini-3-flash", ""));
        // The explicit effort is not validated against the three efforts.
        Assert.Equal(("gemini-3-flash", "max"), AntigravitySession.Normalize("gemini-3-flash", "max"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:433,436-441
    public void Normalize_forces_gemini_3_1_pro_medium_to_high_and_gpt_oss_120b_always_to_medium()
    {
        Assert.Equal(("gemini-3.1-pro", "high"), AntigravitySession.Normalize("gemini-3.1-pro", "medium"));
        Assert.Equal(("gemini-3.1-pro", "high"), AntigravitySession.Normalize("gemini-3.1-pro-medium", null));
        Assert.Equal(("gemini-3.1-pro", "low"), AntigravitySession.Normalize("gemini-3.1-pro", "low"));
        Assert.Equal(("gpt-oss-120b", "medium"), AntigravitySession.Normalize("gpt-oss-120b", null));
        Assert.Equal(("gpt-oss-120b", "medium"), AntigravitySession.Normalize("gpt-oss-120b", "high"));
        Assert.Equal(("gpt-oss-120b", "medium"), AntigravitySession.Normalize("gpt-oss-120b-low", null));
        // Exact slugs only.
        Assert.Equal(("gemini-3.1-pro-preview", "medium"), AntigravitySession.Normalize("gemini-3.1-pro-preview", "medium"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:88-90
    public void Start_appends_the_model_then_the_effort_flag_from_normalize()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("model");
        script.SetInit(Init);
        var configuration = Config(script, model: "gemini-3.1-pro-medium");
        Started(runtime, configuration);
        Assert.Equal([.. Base(configuration.WorkingDirectory.Path), "--model", "gemini-3.1-pro", "--effort", "high"], script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-320")] // Services/Providers/AntigravitySession.swift:88-90,423-426
    public void Start_passes_a_claude_model_without_any_effort_flag()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("claude-model");
        script.SetInit(Init);
        var configuration = Config(script, model: "claude-opus-4-high", effort: "low");
        Started(runtime, configuration);
        Assert.Equal([.. Base(configuration.WorkingDirectory.Path), "--model", "claude-opus-4"], script.Calls().Single());
    }

    // ---- PR-PROVCLI-321 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-321")] // Services/Providers/AntigravitySession.swift:126-142
    public void Plan_interaction_gives_mode_plan_whatever_the_runtime_mode_plus_the_skip_flag_for_full_access()
    {
        Assert.Equal(["--mode", "plan"], ModeArguments(RuntimeMode.Supervised, InteractionMode.Plan));
        Assert.Equal(["--mode", "plan"], ModeArguments(RuntimeMode.AutoAcceptEdits, InteractionMode.Plan));
        Assert.Equal(["--mode", "plan"], ModeArguments(RuntimeMode.Auto, InteractionMode.Plan));
        Assert.Equal(["--mode", "plan", "--dangerously-skip-permissions"], ModeArguments(RuntimeMode.FullAccess, InteractionMode.Plan));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-321")] // Services/Providers/AntigravitySession.swift:130-140
    public void Build_interaction_omits_the_flag_for_supervised_and_accepts_edits_otherwise()
    {
        Assert.Empty(ModeArguments(RuntimeMode.Supervised, InteractionMode.Build));
        Assert.Equal(["--mode", "accept-edits"], ModeArguments(RuntimeMode.AutoAcceptEdits, InteractionMode.Build));
        Assert.Equal(["--mode", "accept-edits"], ModeArguments(RuntimeMode.Auto, InteractionMode.Build));
        Assert.Equal(["--mode", "accept-edits", "--dangerously-skip-permissions"], ModeArguments(RuntimeMode.FullAccess, InteractionMode.Build));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-321")] // Services/Providers/AntigravitySession.swift:88-91
    public void Start_places_the_mode_arguments_after_the_model_flags()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("modes");
        script.SetInit(Init);
        var configuration = Config(script, model: "gemini-3-flash", runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Plan);
        Started(runtime, configuration);
        Assert.Equal(
            [.. Base(configuration.WorkingDirectory.Path), "--model", "gemini-3-flash", "--effort", "high", "--mode", "plan", "--dangerously-skip-permissions"],
            script.Calls().Single());
    }

    // ---- PR-PROVCLI-322 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-322")] // Services/Providers/AntigravitySession.swift:91-94
    public void Start_appends_the_conversation_flag_and_the_resume_id_last()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("resume");
        script.SetInit(Init);
        var configuration = Config(script, resumeID: "conv-7", runtimeMode: RuntimeMode.AutoAcceptEdits);
        Started(runtime, configuration);
        Assert.Equal([.. Base(configuration.WorkingDirectory.Path), "--mode", "accept-edits", "--conversation", "conv-7"], script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-322")] // Services/Providers/AntigravitySession.swift:92
    public void Start_passes_no_conversation_flag_for_an_empty_resume_id()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("resume-empty");
        script.SetInit(Init);
        var configuration = Config(script, resumeID: "");
        Started(runtime, configuration);
        Assert.Equal(Base(configuration.WorkingDirectory.Path), script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-322")] // Services/Providers/AntigravitySession.swift:61
    public void The_spend_baseline_is_known_for_a_nil_or_empty_resume_id_and_unknown_for_a_real_one()
    {
        Assert.True(Field<bool>(Make(Config(null)).Session, "spendBaselineKnown"));
        Assert.True(Field<bool>(Make(Config(null, resumeID: "")).Session, "spendBaselineKnown"));
        Assert.False(Field<bool>(Make(Config(null, resumeID: "conv-1")).Session, "spendBaselineKnown"));
    }

    // ---- PR-PROVCLI-323 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-323")] // Services/Providers/AntigravitySession.swift:55-60,153-156
    public void Init_and_send_store_the_modes_the_model_and_an_effort_that_is_not_empty()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("store");
        script.SetInit(Init);
        var (session, _) = Started(runtime, Config(script, model: "gemini-3-flash", effort: "low"));
        Assert.Equal(RuntimeMode.Supervised, Field<RuntimeMode>(session, "runtimeMode"));
        Assert.Equal(InteractionMode.Build, Field<InteractionMode>(session, "interactionMode"));

        runtime.Loop.Run(() => session.Send(Turn("one", RuntimeMode.FullAccess, InteractionMode.Plan, model: "gemini-3.1-pro", effort: "high")), Bound);
        Assert.Equal(RuntimeMode.FullAccess, Field<RuntimeMode>(session, "runtimeMode"));
        Assert.Equal(InteractionMode.Plan, Field<InteractionMode>(session, "interactionMode"));
        Assert.Equal("gemini-3.1-pro", Field<string>(session, "model"));
        Assert.Equal("high", Field<string>(session, "effort"));

        // An empty model or effort leaves the stored one.
        runtime.Loop.Run(() => session.Send(Turn("two", RuntimeMode.Auto, InteractionMode.Build, model: "", effort: null)), Bound);
        Assert.Equal(RuntimeMode.Auto, Field<RuntimeMode>(session, "runtimeMode"));
        Assert.Equal("gemini-3.1-pro", Field<string>(session, "model"));
        Assert.Equal("high", Field<string>(session, "effort"));
        session.Stop();
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-323")] // Services/Providers/AntigravitySession.swift:88-91,153-156
    public void A_relaunch_uses_the_launch_modes_but_the_updated_model_and_effort()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("relaunch");
        script.SetInit(Init);
        var configuration = Config(script, model: "gemini-3-flash");
        var (session, _) = Started(runtime, configuration);
        runtime.Loop.Run(() => session.Send(Turn("go", RuntimeMode.FullAccess, InteractionMode.Plan, model: "gemini-3.1-pro", effort: "low")), Bound);
        session.Stop();
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
        runtime.Loop.RunUntil(() => Field<TaskHandle>(session, "readTask").Completion.IsCompleted, Bound);

        runtime.Loop.Run(() => session.Start(), Bound);
        Assert.False(Field<bool>(session, "isStopping"));
        var calls = script.Calls();
        Assert.Equal(2, calls.Count);
        Assert.Equal([.. Base(configuration.WorkingDirectory.Path), "--model", "gemini-3-flash", "--effort", "high"], calls[0]);
        // Supervised and Build from the configuration, no --mode; the model and effort from the turn.
        Assert.Equal([.. Base(configuration.WorkingDirectory.Path), "--model", "gemini-3.1-pro", "--effort", "low"], calls[1]);
        session.Stop();
    }

    // ---- PR-PROVCLI-324 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-324")] // Services/Providers/AntigravitySession.swift:71
    public void Start_without_an_executable_throws_notInstalled_antigravity()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));
        var notInstalled = Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(ProviderKind.Antigravity, notInstalled.Provider);
        Assert.False(session.IsRunning);
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-324")] // Services/Providers/AntigravitySession.swift:104,194-203
    public void Start_writes_nothing_and_resolves_on_the_first_init_with_a_non_empty_conversation_id()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("init");
        script.SetInit(
            "{\"event\":\"init\"}",
            "{\"event\":\"init\",\"conversation_id\":\"\"}",
            "{\"event\":\"init\",\"conversation_id\":7}",
            "{\"event\":\"init\",\"conversation_id\":\"conv-9\"}");
        var (session, events) = Make(Config(script));
        var id = runtime.Loop.Run(() => session.Start(), Bound);
        Assert.Equal("conv-9", id);
        Assert.Equal("conv-9", Field<string>(session, "conversationID"));
        var ready = Assert.Single(events);
        Assert.Equal(new ProviderEvent.SessionReady("conv-9"), ready);
        Assert.True(session.IsRunning);
        Assert.Equal(0, script.MessageCount(0));
        session.Stop();
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-324")] // Services/Providers/AntigravitySession.swift:196-203
    public void A_later_init_replaces_the_conversation_id_and_emits_sessionReady_again()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, "{\"event\":\"init\",\"conversation_id\":\"c-2\"}");
        Handle(runtime, session, "{\"event\":\"init\",\"conversation_id\":\"\"}");
        Assert.Equal("c-2", Field<string>(session, "conversationID"));
        Assert.Equal([new ProviderEvent.SessionReady("c-2")], events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-324")] // Services/Providers/AntigravitySession.swift:104
    public void Start_propagates_a_launch_failure()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var configuration = Config(null) with { Executable = FileUrl(Path.Combine(folder, "missing", "agy.exe")), WorkingDirectory = FileUrl(folder) };
        var (session, events) = Make(configuration);
        Assert.ThrowsAny<Exception>(() => runtime.Loop.Run(() => session.Start(), Bound));
        Assert.DoesNotContain(events, e => e is ProviderEvent.SessionReady);
    }

    // ---- PR-PROVCLI-325 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-325")] // Services/Providers/AntigravitySession.swift:113-119,144-149
    public void A_start_with_no_init_fails_after_thirty_seconds_with_the_sign_in_question_and_terminates_the_process()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("timeout");
        var (session, _) = Make(Config(script));
        Task<string>? start = null;
        runtime.OnMain(() => start = session.Start());
        runtime.Loop.RunUntil(() => script.Calls().Count == 1, Bound);
        runtime.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(29.9));
        Assert.False(start!.IsCompleted);
        runtime.Advance(TimeSpan.FromSeconds(0.1));
        runtime.Loop.RunUntil(() => start.IsCompleted, Bound);
        var error = Assert.IsType<ProviderError>(start.Exception!.InnerException);
        var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("Antigravity did not answer. Is `agy` signed in? Run `agy` once in Terminal.", failed.Message);
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-325")] // Services/Providers/AntigravitySession.swift:144-145
    public void The_timer_finds_the_continuation_gone_after_a_successful_start()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("timer-after");
        script.SetInit(Init);
        var (session, events) = Started(runtime, Config(script));
        runtime.Advance(TimeSpan.FromSeconds(31));
        Assert.True(session.IsRunning);
        Assert.Equal([new ProviderEvent.SessionReady("conv-1")], events);
        session.Stop();
    }

    // ---- PR-PROVCLI-326 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-326")] // Services/Providers/AntigravitySession.swift:152
    public void Send_without_a_running_process_throws_notRunning()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Send(Turn("hi")), Bound));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Empty(events);
        Assert.False(Field<bool>(session, "turnActive"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-326")] // Services/Providers/AntigravitySession.swift:157-163
    public void Send_writes_one_user_event_line_with_the_text_and_no_image_and_emits_turnStarted_without_an_id()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("send");
        script.SetInit(Init);
        var (session, events) = Started(runtime, Config(script));
        events.Clear();
        SetField(session, "interruptRequested", true);
        SetField(session, "streamedText", true);
        var image = new Attachment { Name = "a.png", Path = Path.Combine(Scratch(), "a.png"), MimeType = "image/png" };
        runtime.Loop.Run(() => session.Send(Turn("hello agy", images: new ValueList<Attachment>([image]))), Bound);

        Assert.Equal([new ProviderEvent.TurnStarted(null)], events);
        Assert.True(Field<bool>(session, "turnActive"));
        Assert.False(Field<bool>(session, "interruptRequested"));
        Assert.False(Field<bool>(session, "streamedText"));

        var line = Json(script.CapturedLine(0, 0));
        // Swift encodes the dictionary without sorted keys, so only the members are asserted (R-COLL-2).
        Assert.Equal(["event", "message"], line.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("user", line["event"]!.JSONValueString);
        Assert.Equal(["content"], line["message"]!.JSONValueObject!.Keys);
        Assert.Equal(new JSONValue.String("hello agy"), line["message"]!["content"]);
        runtime.Loop.RunUntil(() => script.MessageCount(0) >= 1, Bound);
        Assert.Equal(1, script.MessageCount(0));
        session.Stop();
    }

    // ---- PR-PROVCLI-327 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-327")] // Services/Providers/AntigravitySession.swift:167
    public void Interrupt_without_an_active_turn_does_nothing()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        runtime.Loop.Run(() => session.Interrupt(), Bound);
        Assert.False(Field<bool>(session, "interruptRequested"));
        Assert.False(Field<bool>(session, "isStopping"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-327")] // Services/Providers/AntigravitySession.swift:166-178,371-381
    public void Interrupting_a_turn_kills_agy_and_ends_the_turn_interrupted_without_an_exited_event()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("interrupt");
        script.SetInit(Init);
        var (session, events) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("long job")), Bound);
        events.Clear();
        runtime.Loop.Run(() => session.Interrupt(), Bound);
        Assert.True(Field<bool>(session, "interruptRequested"));
        Assert.True(Field<bool>(session, "isStopping"));
        runtime.Loop.RunUntil(() => events.OfType<ProviderEvent.TurnCompleted>().Any(), Bound);
        runtime.Loop.RunUntil(() => Field<TaskHandle>(session, "readTask").Completion.IsCompleted, Bound);
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null)], events);
        Assert.False(Field<bool>(session, "interruptRequested"));
        Assert.False(session.IsRunning);
    }

    // ---- PR-PROVCLI-328 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-328")] // Services/Providers/AntigravitySession.swift:180-182
    public void Compact_always_throws_that_Antigravity_manages_its_own_context()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Compact(), Bound));
        var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("Antigravity manages its own context.", failed.Message);
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-328")] // Services/Providers/AntigravitySession.swift:184-185
    public void ResolveApproval_and_answerQuestion_do_nothing()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        session.ResolveApproval("req-1", "allow");
        session.AnswerQuestion("req-2", new ValueMap<string, ValueList<string>>([new("q", new ValueList<string>(["a"]))]));
        Assert.Empty(events);
        Assert.False(Field<bool>(session, "turnActive"));
        Assert.False(Field<bool>(session, "isStopping"));
    }

    // ---- PR-PROVCLI-329 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-329")] // Services/Providers/AntigravitySession.swift:187-190
    public void Stop_without_a_process_only_marks_the_session_stopping()
    {
        var (session, events) = Make(Config(null));
        session.Stop();
        Assert.True(Field<bool>(session, "isStopping"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-329")] // Services/Providers/AntigravitySession.swift:187-190,371-381
    public void Stopping_during_a_turn_still_completes_the_turn_as_failed_but_reports_no_exit()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("stop");
        script.SetInit(Init);
        var (session, events) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("work")), Bound);
        events.Clear();
        session.Stop();
        Assert.True(Field<bool>(session, "isStopping"));
        runtime.Loop.RunUntil(() => Field<TaskHandle>(session, "readTask").Completion.IsCompleted, Bound);
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Antigravity exited.")], events);
    }

    // ---- PR-PROVCLI-330 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-330")] // Services/Providers/AntigravitySession.swift:194-211,249-252
    public void Unknown_events_and_the_user_input_and_checkpoint_steps_are_ignored()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, "{\"event\":\"heartbeat\",\"conversation_id\":\"c\"}");
        Handle(runtime, session, "{\"conversation_id\":\"c\"}");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"user_input\",\"step_index\":0,\"text_delta\":\"x\"}}");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"checkpoint\",\"step_index\":1,\"tool_name\":\"run_command\"}}");
        Handle(runtime, session, "{\"event\":\"step_update\"}");
        Assert.Empty(events);
        Assert.Null(typeof(AntigravitySession).GetField("conversationID", Instance)!.GetValue(session));
    }

    // ---- PR-PROVCLI-331 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-331")] // Services/Providers/AntigravitySession.swift:40,213-220,286-291
    public void An_agent_response_emits_its_context_usage_first_then_the_delta_under_a_launch_scoped_id()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session,
            "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"step_index\":3,\"text_delta\":\"Hi\"," +
            "\"usage\":{\"input_tokens\":100,\"cache_read_tokens\":20,\"output_tokens\":5,\"total_tokens\":9999}}}");
        var id = LaunchID(session);
        Assert.Equal(8, id.Length);
        Assert.Matches("^[0-9A-F-]{8}$", id);
        Assert.Equal(
            [new ProviderEvent.Usage(new ContextUsage { UsedTokens = 125, WindowTokens = null }), new ProviderEvent.MessageDelta($"agy-{id}-message-3", "Hi")],
            events);
        Assert.True(Field<bool>(session, "streamedText"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-331")] // Services/Providers/AntigravitySession.swift:214,217-218,287-289
    public void An_agent_response_without_usage_or_text_emits_nothing_and_a_missing_index_is_zero()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"step_index\":1,\"usage\":null,\"text_delta\":\"\"}}");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"usage\":{\"input_tokens\":0,\"output_tokens\":0}}}");
        Assert.Empty(events);
        Assert.False(Field<bool>(session, "streamedText"));
        // Usage is emitted even when the delta is empty; a missing step index reads as zero.
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"usage\":{\"output_tokens\":7}}}");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"text_delta\":\"x\"}}");
        Assert.Equal(
            [new ProviderEvent.Usage(new ContextUsage { UsedTokens = 7 }), new ProviderEvent.MessageDelta($"agy-{LaunchID(session)}-message-0", "x")],
            events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-331")] // Services/Providers/AntigravitySession.swift:40
    public void Every_session_gets_its_own_launch_id()
    {
        Assert.NotEqual(LaunchID(Make(Config(null)).Session), LaunchID(Make(Config(null)).Session));
    }

    // ---- PR-PROVCLI-332 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-332")] // Services/Providers/AntigravitySession.swift:221-236
    public void A_tool_step_is_named_by_tool_name_else_tool_info_name_and_ignored_without_either()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var (session, events) = Make(Config(null, workingDirectory: folder));
        var file = Path.Combine(folder, "a.txt");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":4,\"state\":\"ACTIVE\"," +
            "\"tool_info\":{\"parameters\":{\"AbsolutePath\":" + JsonString(file) + "}}}}");
        Assert.Empty(events);
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":4,\"state\":\"ACTIVE\"," +
            "\"tool_info\":{\"name\":\"view_file\",\"parameters\":{\"AbsolutePath\":" + JsonString(file) + "}}}}");
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":5,\"state\":\"ACTIVE\"," +
            "\"tool_name\":\"view_file\",\"tool_info\":{\"name\":\"write_to_file\",\"parameters\":{\"AbsolutePath\":" + JsonString(file) + "}}}}");
        var id = LaunchID(session);
        Assert.Equal(2, events.Count);
        var first = Assert.IsType<ProviderEvent.ToolStarted>(events[0]);
        Assert.Equal($"agy-{id}-tool-4", first.Id);
        Assert.Equal(ToolCall.Kind.Read, first.Call.ToolCallKind);
        Assert.Equal("a.txt", first.Call.Title);
        var second = Assert.IsType<ProviderEvent.ToolStarted>(events[1]);
        Assert.Equal($"agy-{id}-tool-5", second.Id);
        Assert.Equal(ToolCall.Kind.Read, second.Call.ToolCallKind); // tool_name wins over tool_info.name
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-332")] // Services/Providers/AntigravitySession.swift:228-235,269-273
    public void An_active_edit_step_snapshots_its_file_opens_the_row_once_and_returns()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var file = Path.Combine(folder, "b.txt");
        File.WriteAllText(file, "before\n");
        var (session, events) = Make(Config(null, workingDirectory: folder));
        var step = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":2,\"state\":\"ACTIVE\"," +
            "\"tool_name\":\"replace_file_content\",\"tool_info\":{\"parameters\":{\"TargetFile\":" + JsonString(file) + "}}}}";
        Handle(runtime, session, step);
        Handle(runtime, session, step);
        var id = $"agy-{LaunchID(session)}-tool-2";
        var started = Assert.IsType<ProviderEvent.ToolStarted>(Assert.Single(events));
        Assert.Equal(id, started.Id);
        Assert.Equal(ToolCall.Kind.Edit, started.Call.ToolCallKind);
        Assert.Equal("b.txt", started.Call.Title);
        Assert.Equal([new FileEdit("b.txt")], started.Call.Edits);
        Assert.Equal("before\n", Field<Dictionary<string, string>>(session, "editSnapshots")[id]);
        Assert.Contains(id, Field<HashSet<string>>(session, "startedTools"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-332")] // Services/Providers/AntigravitySession.swift:231-232,269-273
    public void A_snapshot_of_a_missing_or_oversized_file_is_the_empty_string_and_a_non_active_edit_takes_none()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var big = Path.Combine(folder, "big.txt");
        File.WriteAllBytes(big, Enumerable.Repeat((byte)'a', 1_000_001).ToArray());
        var exact = Path.Combine(folder, "exact.txt");
        File.WriteAllBytes(exact, Enumerable.Repeat((byte)'b', 1_000_000).ToArray());
        var (session, _) = Make(Config(null, workingDirectory: folder));
        string Step(int index, string state, string path) =>
            "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":" + index + ",\"state\":\"" + state + "\"," +
            "\"tool_name\":\"write_to_file\",\"tool_info\":{\"parameters\":{\"TargetFile\":" + JsonString(path) + "}}}}";
        Handle(runtime, session, Step(1, "ACTIVE", Path.Combine(folder, "missing.txt")));
        Handle(runtime, session, Step(2, "ACTIVE", big));
        Handle(runtime, session, Step(3, "ACTIVE", exact));
        Handle(runtime, session, Step(4, "PENDING", exact));
        var launch = LaunchID(session);
        var snapshots = Field<Dictionary<string, string>>(session, "editSnapshots");
        Assert.Equal("", snapshots[$"agy-{launch}-tool-1"]);
        Assert.Equal("", snapshots[$"agy-{launch}-tool-2"]);
        Assert.Equal(new string('b', 1_000_000), snapshots[$"agy-{launch}-tool-3"]);
        Assert.False(snapshots.ContainsKey($"agy-{launch}-tool-4"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-332")] // Services/Providers/AntigravitySession.swift:228-236,243-248
    public void A_step_that_first_surfaces_as_done_still_opens_its_row_before_completing_it()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var (session, events) = Make(Config(null, workingDirectory: folder));
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":6,\"state\":\"DONE\"," +
            "\"tool_name\":\"run_command\",\"tool_info\":{\"parameters\":{\"CommandLine\":\"ls -la\"},\"output\":\"total 0\"}}}");
        var id = $"agy-{LaunchID(session)}-tool-6";
        Assert.Equal(2, events.Count);
        var started = Assert.IsType<ProviderEvent.ToolStarted>(events[0]);
        Assert.Equal(id, started.Id);
        Assert.Equal(ToolCall.Kind.Command, started.Call.ToolCallKind);
        Assert.Equal(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Output = "total 0", Status = ToolCall.Status.Completed, Edits = null }), events[1]);
    }

    // ---- PR-PROVCLI-333 ----

    private static string ToolStep(int index, string state, string name, string parameters, string? output = null, string? error = null) =>
        "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"step_index\":" + index + ",\"state\":\"" + state + "\"," +
        "\"tool_name\":\"" + name + "\",\"tool_info\":{\"parameters\":" + parameters +
        (output is null ? "" : ",\"output\":" + JsonString(output)) +
        (error is null ? "" : ",\"error\":{\"message\":" + JsonString(error) + "}") + "}}}";

    private static string JsonString(string text) => new JSONValue.String(text).CompactString;

    [Fact]
    [Trait("Req", "PR-PROVCLI-333")] // Services/Providers/AntigravitySession.swift:239-242,275-281
    public void An_error_state_or_a_done_step_with_an_error_fails_the_row_or_declines_it_on_permission_wording()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        const string parameters = "{\"CommandLine\":\"rm x\"}";
        Handle(runtime, session, ToolStep(1, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(1, "ERROR", "run_command", parameters));
        Handle(runtime, session, ToolStep(2, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(2, "DONE", "run_command", parameters, output: "ignored", error: "Permission Check Failed: rule"));
        Handle(runtime, session, ToolStep(3, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(3, "ERROR", "run_command", parameters, error: "User DENIED PERMISSION"));
        Handle(runtime, session, ToolStep(4, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(4, "ERROR", "run_command", parameters, error: "open: permission denied"));
        Handle(runtime, session, ToolStep(5, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(5, "DONE", "run_command", parameters, error: "exit status 1"));
        Handle(runtime, session, ToolStep(6, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(6, "ERROR", "run_command", parameters, error: "permission was denied"));
        var launch = LaunchID(session);
        var updates = events.OfType<ProviderEvent.ToolUpdated>().ToList();
        Assert.Equal(
            [
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-1", new ToolUpdate { Output = "The tool failed.", Status = ToolCall.Status.Failed }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-2", new ToolUpdate { Output = "Permission Check Failed: rule", Status = ToolCall.Status.Declined }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-3", new ToolUpdate { Output = "User DENIED PERMISSION", Status = ToolCall.Status.Declined }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-4", new ToolUpdate { Output = "open: permission denied", Status = ToolCall.Status.Declined }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-5", new ToolUpdate { Output = "exit status 1", Status = ToolCall.Status.Failed }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-6", new ToolUpdate { Output = "permission was denied", Status = ToolCall.Status.Failed }),
            ],
            updates);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-333")] // Services/Providers/AntigravitySession.swift:243-248
    public void A_done_step_completes_with_its_non_empty_output_and_any_other_state_emits_nothing()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        const string parameters = "{\"CommandLine\":\"ls\"}";
        Handle(runtime, session, ToolStep(1, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(1, "RUNNING", "run_command", parameters));
        Handle(runtime, session, ToolStep(1, "DONE", "run_command", parameters, output: ""));
        Handle(runtime, session, ToolStep(2, "ACTIVE", "run_command", parameters));
        Handle(runtime, session, ToolStep(2, "DONE", "run_command", parameters, output: "a\nb"));
        var launch = LaunchID(session);
        Assert.Equal(
            [
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-1", new ToolUpdate { Status = ToolCall.Status.Completed }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-2", new ToolUpdate { Output = "a\nb", Status = ToolCall.Status.Completed }),
            ],
            events.OfType<ProviderEvent.ToolUpdated>().ToList());
        Assert.Equal(2, events.OfType<ProviderEvent.ToolStarted>().Count());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-333")] // Services/Providers/AntigravitySession.swift:246,259-265
    public void A_finished_edit_carries_the_before_and_after_diff_or_a_path_only_edit_when_nothing_changed()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var changed = Path.Combine(folder, "c.txt");
        var same = Path.Combine(folder, "s.txt");
        File.WriteAllText(changed, "one\n");
        File.WriteAllText(same, "same\n");
        var (session, events) = Make(Config(null, workingDirectory: folder));
        var changedParameters = "{\"TargetFile\":" + JsonString(changed) + "}";
        var sameParameters = "{\"TargetFile\":" + JsonString(same) + "}";
        Handle(runtime, session, ToolStep(1, "ACTIVE", "write_to_file", changedParameters));
        Handle(runtime, session, ToolStep(2, "ACTIVE", "write_to_file", sameParameters));
        File.WriteAllText(changed, "two\n");
        Handle(runtime, session, ToolStep(1, "DONE", "write_to_file", changedParameters));
        Handle(runtime, session, ToolStep(2, "ERROR", "write_to_file", sameParameters, error: "boom"));
        var launch = LaunchID(session);
        var updates = events.OfType<ProviderEvent.ToolUpdated>().ToList();
        Assert.Equal(
            [
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-1", new ToolUpdate { Status = ToolCall.Status.Completed, Edits = new ValueList<FileEdit>([new FileEdit("c.txt", "one\n", "two\n")]) }),
                new ProviderEvent.ToolUpdated($"agy-{launch}-tool-2", new ToolUpdate { Output = "boom", Status = ToolCall.Status.Failed, Edits = new ValueList<FileEdit>([new FileEdit("s.txt")]) }),
            ],
            updates);
        // The snapshot is consumed: a second close carries no edits.
        Handle(runtime, session, ToolStep(1, "DONE", "write_to_file", changedParameters));
        Assert.Equal(new ProviderEvent.ToolUpdated($"agy-{launch}-tool-1", new ToolUpdate { Status = ToolCall.Status.Completed }), events[^1]);
    }

    // ---- PR-PROVCLI-334 ----

    private static string Result(string members) => "{\"event\":\"result\",\"result\":{" + members + "}}";

    [Fact]
    [Trait("Req", "PR-PROVCLI-334")] // Services/Providers/AntigravitySession.swift:293-322
    public void A_known_baseline_turns_cumulative_totals_into_per_turn_spend()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var (session, events) = Make(Config(null, model: "gemini-3-flash"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":100,\"input_tokens\":1,\"output_tokens\":1}"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"input_tokens\":120,\"output_tokens\":30}"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":150}"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":0}"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":null"));
        Assert.Equal(
            [
                new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Antigravity, 100, model: "gemini-3-flash")),
                new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Antigravity, 50, model: "gemini-3-flash")),
            ],
            events.OfType<ProviderEvent.TokenSpend>().ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-334")] // Services/Providers/AntigravitySession.swift:32-36,317-320
    public void A_resumed_conversation_seeds_its_baseline_from_the_first_total_without_charging_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var (session, events) = Make(Config(null, resumeID: "conv-1"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":0}"));
        Assert.False(Field<bool>(session, "spendBaselineKnown"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":1000}"));
        Assert.True(Field<bool>(session, "spendBaselineKnown"));
        Assert.Empty(events.OfType<ProviderEvent.TokenSpend>());
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"usage\":{\"total_tokens\":1200}"));
        Assert.Equal([new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Antigravity, 200, model: null))], events.OfType<ProviderEvent.TokenSpend>().ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-334")] // Services/Providers/AntigravitySession.swift:294-296
    public void A_result_ends_the_turn_and_clears_the_snapshots_and_the_started_tools_first()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var file = Path.Combine(folder, "d.txt");
        File.WriteAllText(file, "x");
        var (session, events) = Make(Config(null, workingDirectory: folder));
        SetField(session, "turnActive", true);
        Handle(runtime, session, ToolStep(1, "ACTIVE", "write_to_file", "{\"TargetFile\":" + JsonString(file) + "}"));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\""));
        Assert.False(Field<bool>(session, "turnActive"));
        Assert.Empty(Field<Dictionary<string, string>>(session, "editSnapshots"));
        Assert.Empty(Field<HashSet<string>>(session, "startedTools"));
    }

    // ---- PR-PROVCLI-335 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-335")] // Services/Providers/AntigravitySession.swift:325-329
    public void A_reply_that_never_streamed_is_shown_from_the_result_response()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"response\":\"Done here.\""));
        var completed = Assert.Single(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.Matches("^agy-" + LaunchID(session) + "-result-[0-9A-F-]{8}$", completed.Id);
        Assert.Equal("Done here.", completed.Text);
        Assert.IsType<ProviderEvent.MessageCompleted>(events[0]);

        events.Clear();
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"response\":\" \\n\\t \""));
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"response\":5"));
        Assert.Empty(events.OfType<ProviderEvent.MessageCompleted>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-335")] // Services/Providers/AntigravitySession.swift:325-329
    public void A_streamed_reply_is_not_repeated_from_the_result_and_the_flag_resets()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"step_index\":0,\"text_delta\":\"Hi\"}}");
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"response\":\"Hi\""));
        Assert.Empty(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.False(Field<bool>(session, "streamedText"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-335")] // Services/Providers/AntigravitySession.swift:330-341
    public void Denied_actions_raise_one_warning_and_a_conversation_id_is_adopted()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        Handle(runtime, session, Result(
            "\"status\":\"SUCCESS\",\"conversation_id\":\"conv-5\"," +
            "\"denied_actions\":[{\"display_name\":\"Edit a.txt\",\"action\":\"edit\"},{\"action\":\"run_command\"},{\"other\":1},\"x\"]"));
        Assert.Equal(
            [
                new ProviderEvent.Notice(new Notice
                {
                    NoticeLevel = Notice.Level.Warning,
                    Message = "Antigravity held back Edit a.txt, run_command without prompting. Use Full access or add an allow rule in the CLI's settings.",
                }),
                new ProviderEvent.SessionReady("conv-5"),
                new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            ],
            events);
        Assert.Equal("conv-5", Field<string>(session, "conversationID"));

        events.Clear();
        Handle(runtime, session, Result("\"status\":\"SUCCESS\",\"conversation_id\":\"\",\"denied_actions\":[]"));
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)], events);
        Assert.Equal("conv-5", Field<string>(session, "conversationID"));
    }

    // ---- PR-PROVCLI-336 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-336")] // Services/Providers/AntigravitySession.swift:342-362
    public void The_result_status_picks_one_of_five_turn_endings()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        ProviderEvent.TurnCompleted Ending(string members)
        {
            events.Clear();
            Handle(runtime, session, Result(members));
            return Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        }

        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                "Antigravity held back Edit a.txt, run_command waiting for a permission it could not ask for. Use Full access for this chat."),
            Ending("\"status\":\"WAITING\",\"denied_actions\":[{\"display_name\":\"Edit a.txt\"},{\"action\":\"run_command\"}]"));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), Ending("\"status\":\"WAITING\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), Ending("\"status\":\"SUCCESS\",\"error\":\"ignored\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            Ending("\"status\":\"SUCCESS\",\"denied_actions\":[{\"action\":\"x\"}]"));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null), Ending("\"status\":\"CANCELED\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null), Ending("\"status\":\"INTERRUPTED\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "quota exhausted"), Ending("\"status\":\"ERROR\",\"error\":\"quota exhausted\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Antigravity stopped before finishing."), Ending("\"error\":\"\""));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Antigravity stopped before finishing."), Ending("\"status\":\"success\""));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-336")] // Services/Providers/AntigravitySession.swift:342-343,362
    public void An_interrupt_request_ends_the_turn_interrupted_whatever_the_status_and_is_reset()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        SetField(session, "interruptRequested", true);
        Handle(runtime, session, Result("\"status\":\"WAITING\",\"denied_actions\":[{\"action\":\"x\"}]"));
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null), events[^1]);
        Assert.False(Field<bool>(session, "interruptRequested"));
    }

    // ---- PR-PROVCLI-337 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-337")] // Services/Providers/AntigravitySession.swift:365-383
    public void The_close_path_fails_an_active_turn_then_reports_the_exit_when_not_stopping()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        SetField(session, "turnActive", true);
        DidClose(runtime, session);
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Antigravity exited."), new ProviderEvent.Exited(null)], events);
        Assert.False(Field<bool>(session, "turnActive"));

        events.Clear();
        SetField(session, "turnActive", true);
        SetField(session, "interruptRequested", true);
        SetField(session, "isStopping", true);
        DidClose(runtime, session);
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null)], events);
        Assert.False(Field<bool>(session, "interruptRequested"));

        events.Clear();
        DidClose(runtime, session);
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-337")] // Services/Providers/AntigravitySession.swift:365-370,385-390
    public void A_start_that_closes_first_fails_with_the_auth_hint_when_stderr_says_so()
    {
        using var runtime = new TestRuntime();
        foreach (var stderr in new[] { "Error: Authentication Required\n", "you are NOT SIGNED IN\n", "401 unauthenticated\n", "run `agy` to Login\n" })
        {
            var script = new AntigravityScript("auth-hint");
            script.Set("session.stderr", stderr);
            script.Set("exit-at-start", "1");
            var (session, _) = Make(Config(script));
            var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));
            Assert.Equal(new ProviderError.Case.Failed("Antigravity is not signed in. Run `agy` once in Terminal to sign in."), error.ErrorCase);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-337")] // Services/Providers/AntigravitySession.swift:365-370
    public void A_start_that_closes_first_fails_with_the_stderr_tail_or_that_Antigravity_exited()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("tail");
        script.Set("session.stderr", "please login\nfatal: bad flag\n");
        script.Set("exit-at-start", "2");
        var (session, _) = Make(Config(script));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));
        Assert.Equal(new ProviderError.Case.Failed("please login\nfatal: bad flag"), error.ErrorCase);

        var quiet = new AntigravityScript("quiet");
        quiet.Set("exit-at-start", "0");
        var (silent, events) = Make(Config(quiet));
        var silentError = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => silent.Start(), Bound));
        Assert.Equal(new ProviderError.Case.Failed("Antigravity exited."), silentError.ErrorCase);
        Assert.Equal([new ProviderEvent.Exited(null)], events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-337")] // Services/Providers/AntigravitySession.swift:371-382
    public void A_turn_cut_off_by_an_exit_fails_with_the_auth_hint_then_reports_the_exit_with_the_tail()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("turn-exit");
        script.SetInit(Init);
        script.Set("turn-0.stderr", "Unauthenticated: token expired\n");
        script.Set("exit-after-turn-0", "1");
        var (session, events) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("go")), Bound);
        runtime.Loop.RunUntil(() => events.OfType<ProviderEvent.Exited>().Any(), Bound);
        Assert.Equal(
            [
                new ProviderEvent.SessionReady("conv-1"),
                new ProviderEvent.TurnStarted(null),
                new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Antigravity is not signed in. Run `agy` once in Terminal to sign in."),
                new ProviderEvent.Exited("Unauthenticated: token expired"),
            ],
            events);
    }

    // ---- PR-PROVCLI-338 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-338")] // Services/Providers/AntigravitySession.swift:452-517
    public void ListModels_collapses_effort_variants_into_one_row_per_base_slug_in_first_seen_order()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("models");
        script.Set("models.stdout",
            "\u001b[1mMODEL\u001b[0m\tNAME\n" +
            "Fetching models...\n" +
            "gemini-3.1-pro-low\tGemini 3.1 Pro (Low)\n" +
            "gemini-3.1-pro-high\tGemini 3.1 Pro (High)\n" +
            "\u001b[32mgemini-3-flash\u001b[0m\tGemini 3 Flash\n" +
            "claude-sonnet-4.5\t\n" +
            "gemini-3.1-pro-low\tRenamed (Low)\n");
        script.Set("model.stdout", "{\"command\":{\"data\":{\"id\":\"gemini-3-flash\"}}}");
        var models = runtime.Loop.Run(() => AntigravitySession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.Equal(
            [
                new ModelOption("gemini-3.1-pro", "Gemini 3.1 Pro", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "high", isDefault: false),
                new ModelOption("gemini-3-flash", "Gemini 3 Flash", efforts: ValueList<string>.Empty, defaultEffort: null, isDefault: true),
                new ModelOption("claude-sonnet-4.5", "claude-sonnet-4.5", efforts: ValueList<string>.Empty, defaultEffort: null, isDefault: false),
            ],
            models);
        var calls = script.Calls();
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, call => call.SequenceEqual(["models"]));
        Assert.Contains(calls, call => call.SequenceEqual(["-p", "/model", "--output-format", "json"]));
    }

    // ---- PR-PROVCLI-339 ----

    private static ProviderStatus.Auth AuthStatus(TestRuntime runtime, AntigravityScript script) =>
        runtime.Loop.Run(() => AntigravitySession.AuthStatus(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);

    [Fact]
    [Trait("Req", "PR-PROVCLI-339")] // Services/Providers/AntigravitySession.swift:527-533
    public void AuthStatus_runs_agy_models_and_is_signed_in_without_an_account_when_it_lists_a_row()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("auth-in");
        script.Set("models.stdout", "\u001b[2mgemini-3-flash\u001b[0m\tGemini 3 Flash\n");
        script.Set("models.stderr", "login required\n");
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), AuthStatus(runtime, script));
        Assert.Equal(["models"], script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-339")] // Services/Providers/AntigravitySession.swift:531-539
    public void AuthStatus_reads_five_sign_in_phrases_before_falling_back_to_the_exit_status()
    {
        using var runtime = new TestRuntime();
        ProviderStatus.Auth Status(string? stdout, string? stderr, int exit)
        {
            var script = new AntigravityScript("auth");
            if (stdout is not null) script.Set("models.stdout", stdout);
            if (stderr is not null) script.Set("models.stderr", stderr);
            script.Set("models.exit", exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return AuthStatus(runtime, script);
        }

        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status(null, "Authentication required.\n", 1));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status("You are Not Signed In\n", null, 0));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status(null, "UNAUTHENTICATED\n", 1));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status("Please sign in first.\n", null, 0));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status(null, "Login\n", 1));
        // A row with a failed exit is not enough, and the phrases decide first.
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status("gemini-3-flash\tGemini\n", "login\n", 1));
        Assert.Equal(new ProviderStatus.Auth.Unknown(), Status("gemini-3-flash\tGemini\n", null, 1));
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), Status("no models here\n", null, 0));
        Assert.Equal(new ProviderStatus.Auth.Unknown(), Status("boom\n", "crash\n", 3));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-339")] // Services/Providers/AntigravitySession.swift:528-530
    public void AuthStatus_is_unknown_when_agy_cannot_run()
    {
        using var runtime = new TestRuntime();
        var missing = FileUrl(Path.Combine(Scratch(), "no-such-agy.exe"));
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            runtime.Loop.Run(() => AntigravitySession.AuthStatus(missing, new ValueMap<string, string>([])), Bound));
    }

    // ---- PR-PROVCLI-340 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-340")] // Services/Providers/AntigravitySession.swift:547-562
    public void ReadPlanLimits_runs_usage_and_credits_concurrently_and_appends_the_credit_window_last_with_no_plan_name()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("limits");
        script.Set("rendezvous", "usage\ncredits\n");
        script.Set("usage.stdout",
            "{\"command\":{\"data\":{\"groups\":[{\"name\":\"Gemini\",\"buckets\":[{\"id\":\"weekly\",\"name\":\"Weekly\",\"remaining_fraction\":0.25}]}]}}}");
        script.Set("credits.stdout", "{\"command\":{\"data\":{\"remaining_credits\":12}}}");
        var limits = runtime.Loop.Run(() => AntigravitySession.ReadPlanLimits(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.NotNull(limits);
        Assert.Null(limits!.PlanName);
        Assert.Equal(
            [W("weekly", "Gemini " + MiddleDot + " Weekly", 75), W("ai-credits", "AI credits " + MiddleDot + " 12 remaining", 0)],
            limits.Windows);
        var calls = script.Calls();
        Assert.Contains(calls, call => call.SequenceEqual(["-p", "/usage", "--output-format", "json"]));
        Assert.Contains(calls, call => call.SequenceEqual(["-p", "/credits", "--output-format", "json"]));
        Assert.Equal("ok", script.Read("rendezvous-usage.txt"));
        Assert.Equal("ok", script.Read("rendezvous-credits.txt"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-340")] // Services/Providers/AntigravitySession.swift:550-561
    public void ReadPlanLimits_is_nil_without_any_window_and_keeps_one_side_when_the_other_fails()
    {
        using var runtime = new TestRuntime();
        var none = new AntigravityScript("limits-none");
        none.Set("usage.stdout", "not json");
        none.Set("credits.stdout", "{\"command\":{\"data\":{}}}");
        Assert.Null(runtime.Loop.Run(() => AntigravitySession.ReadPlanLimits(FileUrl(none.ExecutablePath), new ValueMap<string, string>([none.EnvironmentEntry])), Bound));

        var credits = new AntigravityScript("limits-credits");
        credits.Set("credits.stdout", "{\"response\":\"AI Credits\\t0\"}");
        var limits = runtime.Loop.Run(() => AntigravitySession.ReadPlanLimits(FileUrl(credits.ExecutablePath), new ValueMap<string, string>([credits.EnvironmentEntry])), Bound);
        Assert.Equal([W("ai-credits", "AI credits " + MiddleDot + " 0 remaining", 100)], limits!.Windows);

        var missing = FileUrl(Path.Combine(Scratch(), "no-such-agy.exe"));
        Assert.Null(runtime.Loop.Run(() => AntigravitySession.ReadPlanLimits(missing, new ValueMap<string, string>([])), Bound));
    }

    // ---- PR-PROVCLI-341 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-341")] // Services/Providers/AntigravitySession.swift:567-583
    public void ParseUsage_titles_each_bucket_by_group_and_name_and_takes_the_complement_of_the_remaining_fraction()
    {
        var data = Json(
            "{\"groups\":[" +
            "{\"name\":\"Gemini Pro\",\"buckets\":[" +
            "{\"id\":\"pro-weekly\",\"name\":\"Weekly\",\"remaining_fraction\":0.25,\"reset_time\":\"2026-09-30T08:00:00Z\"}," +
            "{\"name\":\"5-hour\",\"remaining_fraction\":1,\"reset_time\":\"2026-09-24T13:30:00.500Z\"}," +
            "{\"id\":\"skip\",\"name\":\"No fraction\",\"remaining_fraction\":\"0.5\"}," +
            "{\"id\":\"over\",\"remaining_fraction\":1.5,\"reset_time\":\"soon\"}]}," +
            "{\"buckets\":[{\"remaining_fraction\":-0.5}]}]}");
        Assert.Equal(
            [
                W("pro-weekly", "Gemini Pro " + MiddleDot + " Weekly", 75, new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero)),
                W("Gemini Pro-1", "Gemini Pro " + MiddleDot + " 5-hour", 0, new DateTimeOffset(2026, 9, 24, 13, 30, 0, 500, TimeSpan.Zero)),
                W("over", "Gemini Pro " + MiddleDot + " Limit", 0),
                W("Models-3", "Models " + MiddleDot + " Limit", 100),
            ],
            ParseUsage(data, "Ignored\tWhen\t10%"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-341")] // Services/Providers/AntigravitySession.swift:581,587-603
    public void ParseUsage_falls_back_to_tab_separated_rows_where_the_percent_is_what_remains()
    {
        var text =
            "Gemini\tWeekly\t95%\t2026-10-01T00:00:00Z\n" +
            "too\tshort\n" +
            "Gemini\t5-hour\tlots\n" +
            " Claude \t Daily \t 40% \n" +
            "Other\tBucket\t-20\tnot a date\n";
        Assert.Equal(
            [
                W("usage-0", "Gemini " + MiddleDot + " Weekly", 5, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
                W("usage-1", "Claude " + MiddleDot + " Daily", 60),
                W("usage-2", "Other " + MiddleDot + " Bucket", 100),
            ],
            ParseUsage(Json("{\"groups\":[]}"), text));
        Assert.Empty(ParseUsage(null, null));
        Assert.Empty(ParseUsage(Json("{\"groups\":[{\"buckets\":[{\"name\":\"x\"}]}]}"), null));
    }

    // ---- PR-PROVCLI-342 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-342")] // Services/Providers/AntigravitySession.swift:605-613
    public void ParseCredits_is_binary_zero_percent_while_credits_remain_and_the_structured_value_wins()
    {
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 5 remaining", 0), ParseCredits(Json("{\"remaining_credits\":5}"), "AI credits\t9"));
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 0 remaining", 100), ParseCredits(Json("{\"remaining_credits\":0}"), null));
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " -3 remaining", 100), ParseCredits(Json("{\"remaining_credits\":-3}"), null));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-342")] // Services/Providers/AntigravitySession.swift:614-626
    public void ParseCredits_falls_back_to_the_first_tab_separated_credit_line_with_an_integer()
    {
        const string text = "Plan\t3\nAI Credits\tmany\nremaining CREDITS\t 42 \nCredits\t7\n";
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 42 remaining", 0), ParseCredits(Json("{}"), text));
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 7 remaining", 0), ParseCredits(null, "Credits\t7"));
        Assert.Null(ParseCredits(Json("{}"), null));
        Assert.Null(ParseCredits(null, "Credits only\nPlan\t4"));
    }
}
