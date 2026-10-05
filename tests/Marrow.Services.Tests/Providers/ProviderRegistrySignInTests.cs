using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), signingIn :283-284 and signIn :286-310.
//
// PR-PROVREG-234 and PR-PROVREG-235 are build records: the pinned checkout has no caller of signIn and no reader of
// signingIn, and neither is recorded as dead code, so the record leaves reachability open. The behaviour the Swift
// spells out is implemented and proved here so that it is there if a caller is found: the two guards, the login
// command run in process with a 300 second bound, the not-found and did-not-finish strings, the last non-empty
// ANSI-stripped output line, and the refresh / forced limits / forced catalog follow-up on success.
// The login CLI is the compiled provider-probe stand-in (R-TEST-8) reached only through a custom binary path inside
// this run's folders (R-TEST-10): no real CLI is started and nothing touches the network.
// Process-heavy (the login stand-in, then Codex's app-server twice): runs with no other class in parallel, like PlanLimitsTests
// (ED-190), so a loaded full run cannot starve its waits.
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class ProviderRegistrySignInTests
{
    private const ProviderKind Provider = ProviderKind.Cursor; // login command "cursor-agent login": arguments ["login"]

    private static string Missing()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-sign-in", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "does-not-exist.exe").Replace('\\', '/');
    }

    private static (ProviderRegistry Registry, ProviderProbeScript Script) Arrange(FakeProviderSettings settings, ProviderKind provider = Provider)
    {
        var script = ProviderProbeStandIns.CopyFor("sign-in");
        settings.Paths[provider] = script.ExecutablePath.Replace('\\', '/');
        return (new ProviderRegistry(settings), script);
    }

    private static Task<string?> SignIn(TestRuntime runtime, ProviderRegistry registry, ProviderKind provider) =>
        Task.Run(() => runtime.Loop.Run(() => registry.SignIn(provider), TimeSpan.FromSeconds(60)));

    // ---- PR-PROVREG-234 ------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-234")] // ProviderRegistry.swift:297-303 (login arguments, then refresh, forced limits read and forced catalog load; nil on success)
    public async Task a_successful_login_runs_the_login_arguments_then_rechecks_the_provider_and_reloads_its_catalog()
    {
        using var runtime = new TestRuntime();
        using var shell = ShellRecording.Start();
        // Codex: its login command is "codex login", its status check reads "login status", and both a plan-limits
        // read and a model listing launch its app-server, so each follow-up step leaves its own launch.
        var (registry, script) = Arrange(new FakeProviderSettings(), ProviderKind.Codex);
        script.SetStdout("Logged in using ChatGPT\n").SetExitCode(0).UseCodexRPC();
        // Both follow-ups are FORCED: with the catalog attempt closed and the limits just read, an unforced ask of
        // either would do nothing, so only a forced one leaves a launch.
        ProviderRegistryState.MarkCatalogAttempted(registry, ProviderKind.Codex);
        ProviderRegistryState.StampLimitsRead(registry, ProviderKind.Codex);

        var message = await SignIn(runtime, registry, ProviderKind.Codex);

        Assert.Null(message);
        var login = shell.Calls[0]; // the very first process run is the login command
        Assert.Equal(["login"], login.Arguments);
        Assert.Equal(300, login.Timeout); // bounded at exactly five minutes
        // Join the forced read's completion on the injected loop; elapsed real time says nothing
        // about whether its app-server launch and cleanup have happened.
        await Task.Run(() => runtime.Loop.RunUntil(() => !registry.LoadingLimits.Contains(ProviderKind.Codex) &&
            script.RecordedCalls().Count(call => call.SequenceEqual(["app-server"])) >= 2, TimeSpan.FromSeconds(30)));
        var calls = script.RecordedCalls();
        Assert.Equal(["login"], calls[0]); // the login command minus its own first word
        Assert.Contains(calls, call => call.SequenceEqual(["--version"])); // refresh(provider) ran after the login
        Assert.Contains(calls, call => call.SequenceEqual(["login", "status"]));
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), registry.Status(ProviderKind.Codex).ProviderStatusAuth); // the new login is seen
        Assert.Equal(2, calls.Count(call => call.SequenceEqual(["app-server"]))); // one forced limits read, one forced catalog load
        Assert.Contains(registry.Models(ProviderKind.Codex), model => model.Id == "offline-model");
        Assert.Null(registry.SigningIn);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-234")] // ProviderRegistry.swift:305-310 (a failure returns the last non-empty line of the ANSI-stripped combined output)
    public async Task a_failed_login_returns_the_last_non_empty_line_of_the_ansi_stripped_output_and_follows_up_with_nothing()
    {
        using var runtime = new TestRuntime();
        var (registry, script) = Arrange(new FakeProviderSettings());
        script.SetStdout("starting\n\u001b[31mfailing in red\u001b[0m\n").SetStderr("oops\n\u001b[1mlast words\u001b[0m\n\n").SetExitCode(1);

        var message = await SignIn(runtime, registry, Provider);

        Assert.Equal("last words", message); // stdout then stderr, escape codes removed, empty trailing lines ignored
        Assert.Single(script.RecordedCalls()); // no refresh, no limits read, no catalog probe
        Assert.Null(registry.SigningIn);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-234")] // ProviderRegistry.swift:306-309 (no non-empty output line: "<displayName> did not finish signing in.")
    public async Task a_failed_login_with_no_output_says_the_provider_did_not_finish_signing_in()
    {
        using var runtime = new TestRuntime();
        var (registry, script) = Arrange(new FakeProviderSettings());
        script.SetStdout("\n\n").SetExitCode(3);

        var message = await SignIn(runtime, registry, Provider);

        Assert.Equal("Cursor did not finish signing in.", message);
        Assert.Single(script.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-234")] // ProviderRegistry.swift:295 (an unresolved executable: "<displayName> was not found.")
    public async Task an_unresolved_executable_says_the_provider_was_not_found_and_launches_nothing()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        settings.Paths[Provider] = Missing(); // a custom path that does not resolve has no fallback
        var registry = new ProviderRegistry(settings);

        var message = await SignIn(runtime, registry, Provider);

        Assert.Equal("Cursor was not found.", message);
        Assert.Null(registry.SigningIn);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-234")] // ProviderRegistry.swift:291 (an API-key provider is refused with nil and nothing runs)
    public async Task an_api_key_provider_is_refused_without_running_anything()
    {
        using var runtime = new TestRuntime();
        var (registry, script) = Arrange(new FakeProviderSettings());

        foreach (var provider in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai })
        {
            Assert.Null(await SignIn(runtime, registry, provider));
            Assert.Null(registry.SigningIn);
        }
        Assert.Empty(script.RecordedCalls());
    }

    // ---- PR-PROVREG-235 ------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-235")] // ProviderRegistry.swift:283-284, :290 and :292-293 (signingIn names the provider while a sign-in runs and guards re-entry)
    public async Task signing_in_names_the_provider_while_the_login_runs_refuses_a_second_sign_in_and_clears_afterwards()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        var (registry, script) = Arrange(settings);
        var other = ProviderProbeStandIns.CopyFor("sign-in-other");
        settings.Paths[ProviderKind.Grok] = other.ExecutablePath.Replace('\\', '/');
        script.SetSleep(1500).SetExitCode(1).SetStdout("not signed in\n"); // the login runs a while, then fails (no follow-up work)
        Assert.Null(registry.SigningIn);

        TaskHandle<string?>? first = null;
        runtime.OnMain(() => first = MainActor.Spawn(() => registry.SignIn(Provider)));
        await Task.Run(() => runtime.Loop.RunUntil(() => script.RecordedCalls().Count >= 1, TimeSpan.FromSeconds(30)));
        Assert.Equal(Provider, registry.SigningIn); // the login is out

        var second = await SignIn(runtime, registry, ProviderKind.Grok); // refused at once, nothing launched
        Assert.Null(second);
        Assert.Empty(other.RecordedCalls());
        Assert.Equal(Provider, registry.SigningIn);

        await Task.Run(() => runtime.Loop.RunUntil(() => first!.Completion.IsCompleted, TimeSpan.FromSeconds(30)));
        Assert.Equal("not signed in", await first!.Value);
        Assert.Null(registry.SigningIn);
    }
}
