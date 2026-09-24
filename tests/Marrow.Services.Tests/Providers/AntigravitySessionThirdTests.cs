using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/AntigravitySession.swift (B2 1fbf81e). Part 3: PR-PROVCLI-435
// to 438 (the default effort, the empty catalog, the default-row rewrite, the MCP-free argument vector) and the two
// UI records whose data this file produces, UI-PROVCLI-021 (model picker rows) and UI-PROVCLI-025 (usage panel rows).
// Process calls run against the compiled offline stand-in tests/fixtures/antigravity-cli (R-TEST-8, R-TEST-10); the
// private usage parsers are exercised directly by reflection.
public sealed class AntigravitySessionThirdTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string MiddleDot = "\U000000B7"; // U+00B7 MIDDLE DOT

    // A non-"file" scheme keeps the Windows path unchanged through SwiftURL.Path (the ClaudeSessionTests note).
    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;

    private static object? CallStatic(string method, params object?[] args)
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

    private static JSONValue Json(string text) => JSONValue.Parse(text) ?? throw new ArgumentException("Not JSON: " + text);

    private static AntigravityScript Catalog(string label, string models, string? current = null)
    {
        var script = new AntigravityScript(label);
        script.Set("models.stdout", models);
        if (current is not null) script.Set("model.stdout", "{\"command\":{\"data\":{\"id\":\"" + current + "\"}}}");
        return script;
    }

    private static ValueList<ModelOption> ListModels(TestRuntime runtime, AntigravityScript script) =>
        runtime.Loop.Run(() => AntigravitySession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);

    private static PlanLimits.Window W(string id, string title, double percent) => new() { Id = id, Title = title, Percent = percent };

    // ---- PR-PROVCLI-435 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-435")] // Services/Providers/AntigravitySession.swift:499-506
    public void A_model_defaults_to_high_when_offered_else_to_the_highest_it_has_and_to_none_without_efforts()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("default-effort",
            "a-low\tA (Low)\na-medium\tA (Medium)\n" +
            "b-low\tB (Low)\n" +
            "c-high\tC (High)\nc-low\tC (Low)\n" +
            "d\tD\n", "none");
        var models = ListModels(runtime, script);
        Assert.Equal(["medium", "low", "high", null], models.Select(option => option.DefaultEffort));
        Assert.All(models, option => Assert.Null(option.Detail));
        Assert.All(models, option => Assert.Null(option.FastTier));
    }

    // ---- PR-PROVCLI-436 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-436")] // Services/Providers/AntigravitySession.swift:509
    public void A_catalog_with_no_slug_row_throws_that_agy_models_listed_no_models()
    {
        using var runtime = new TestRuntime();
        var headersOnly = Catalog("no-rows", "MODEL NAME\tDISPLAY\nFetching models...\n", "none");
        var error = Assert.Throws<ShellError>(() => ListModels(runtime, headersOnly));
        Assert.Equal("`agy models` listed no models.", error.ErrorDescription);

        var empty = Catalog("empty", "");
        var emptyError = Assert.Throws<ShellError>(() => ListModels(runtime, empty));
        Assert.Equal("`agy models` listed no models.", emptyError.ErrorDescription);
    }

    // ---- PR-PROVCLI-437 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-437")] // Services/Providers/AntigravitySession.swift:510-516
    public void Without_a_current_row_the_first_model_becomes_the_default_with_every_other_field_kept()
    {
        using var runtime = new TestRuntime();
        var models = ListModels(runtime, Catalog("rewrite", "p-low\tP (Low)\np-high\tP (High)\nq\tQ\n", "zzz"));
        Assert.Equal(
            [
                new ModelOption("p", "P", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "high", isDefault: true),
                new ModelOption("q", "Q", efforts: ValueList<string>.Empty, defaultEffort: null, isDefault: false),
            ],
            models);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-437")] // Services/Providers/AntigravitySession.swift:510
    public void With_a_current_row_the_first_model_is_left_alone()
    {
        using var runtime = new TestRuntime();
        var models = ListModels(runtime, Catalog("kept", "p\tP\nq\tQ\n", "q"));
        Assert.Equal([false, true], models.Select(option => option.IsDefault));
    }

    // ---- PR-PROVCLI-438 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-438")] // Services/Providers/AntigravitySession.swift:76-94
    public void The_argument_vector_carries_no_MCP_token_after_the_fixed_eight()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("argv-check");
        script.SetInit("{\"event\":\"init\",\"conversation_id\":\"conv-1\"}");
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Antigravity,
            Executable = FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
            ResumeID = "conv-0",
            Model = "gemini-3.1-pro-low",
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
        };
        var session = new AntigravitySession(configuration);
        runtime.Loop.Run(() => session.Start(), Bound);
        var argv = script.Calls().Single();
        Assert.Equal(
            [
                "--input-format", "stream-json", "--output-format", "stream-json", "--print-timeout", "30m", "--add-dir", configuration.WorkingDirectory.Path,
                "--model", "gemini-3.1-pro", "--effort", "low", "--mode", "accept-edits", "--dangerously-skip-permissions", "--conversation", "conv-0",
            ],
            argv);
        Assert.DoesNotContain(argv, argument => argument.Contains("mcp", StringComparison.OrdinalIgnoreCase));
        session.Stop();
    }

    // ---- UI-PROVCLI-021 ----

    [Fact]
    [Trait("Req", "UI-PROVCLI-021")] // Services/Providers/AntigravitySession.swift:400-412,465-516
    public void The_model_picker_gets_one_row_per_base_slug_with_its_efforts_on_the_slider()
    {
        using var runtime = new TestRuntime();
        var models = ListModels(runtime, Catalog("picker",
            "gemini-3.1-pro-high\tGemini 3.1 Pro (High)\n" +
            "gemini-3.1-pro-low\tGemini 3.1 Pro (Low)\n" +
            "gemini-3-flash-medium\tGemini 3 Flash (Medium)\n" +
            "gemini-3-flash-low\tGemini 3 Flash (Low)\n" +
            "claude-sonnet-4.5\tClaude Sonnet 4.5\n", "none"));
        Assert.Equal(["gemini-3.1-pro", "gemini-3-flash", "claude-sonnet-4.5"], models.Select(option => option.Id));
        Assert.Equal(["Gemini 3.1 Pro", "Gemini 3 Flash", "Claude Sonnet 4.5"], models.Select(option => option.Name));
        Assert.Equal(["low", "high"], models[0].Efforts);
        Assert.Equal(["low", "medium"], models[1].Efforts);
        Assert.Empty(models[2].Efforts);
        Assert.Equal(["high", "medium", null], models.Select(option => option.DefaultEffort));
        Assert.Equal([true, false, false], models.Select(option => option.IsDefault));
        Assert.All(models, option => Assert.Null(option.Detail));
    }

    // ---- UI-PROVCLI-025 ----

    [Fact]
    [Trait("Req", "UI-PROVCLI-025")] // Services/Providers/AntigravitySession.swift:567-583,605-627
    public void Usage_rows_are_titled_group_middle_dot_bucket_and_the_credits_row_is_binary()
    {
        var usage = (ValueList<PlanLimits.Window>)CallStatic("ParseUsage",
            Json("{\"groups\":[{\"name\":\"Gemini\",\"buckets\":[{\"id\":\"w\",\"name\":\"Weekly\",\"remaining_fraction\":0.5}]},{\"buckets\":[{\"id\":\"l\",\"remaining_fraction\":0}]}]}"),
            null)!;
        Assert.Equal([W("w", "Gemini " + MiddleDot + " Weekly", 50), W("l", "Models " + MiddleDot + " Limit", 100)], usage);
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 1 remaining", 0), (PlanLimits.Window?)CallStatic("ParseCredits", Json("{\"remaining_credits\":1}"), null));
        Assert.Equal(W("ai-credits", "AI credits " + MiddleDot + " 0 remaining", 100), (PlanLimits.Window?)CallStatic("ParseCredits", Json("{\"remaining_credits\":0}"), null));
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-025")] // Services/Providers/AntigravitySession.swift:560-561
    public void The_usage_panel_never_gets_a_plan_name_for_Antigravity()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("plan-name");
        script.Set("usage.stdout", "{\"command\":{\"data\":{\"plan\":\"Pro\",\"groups\":[{\"name\":\"G\",\"buckets\":[{\"id\":\"b\",\"remaining_fraction\":1}]}]}}}");
        script.Set("credits.stdout", "{\"command\":{\"data\":{\"plan_name\":\"Ultra\",\"remaining_credits\":3}}}");
        var limits = runtime.Loop.Run(() => AntigravitySession.ReadPlanLimits(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.NotNull(limits);
        Assert.Null(limits!.PlanName);
        Assert.Equal([W("b", "G " + MiddleDot + " Limit", 0), W("ai-credits", "AI credits " + MiddleDot + " 3 remaining", 0)], limits.Windows);
    }
}
