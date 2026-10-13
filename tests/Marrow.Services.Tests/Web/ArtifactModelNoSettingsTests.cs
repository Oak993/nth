using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review 2026-10-13, finding M4 (ED-NEW-web-sec-artifacts-1; DESIGN.md 6.5 nth.complete is "a
// fresh, context-free call ... no memory, no tools"). An artifact's model call is a no-tools Claude launch, and it used to keep
// --setting-sources user,project,local: the user's CLAUDE.md, every CLAUDE.md above the call's folder, auto memory and the user's
// hooks loaded for each call (a hook even saw the artifact's text), and each call was saved as a Claude session on disk. A
// no-tools launch now loads no setting source, turns CLAUDE.md files and auto memory off, and is never saved; an ordinary chat
// launches exactly as before. Recorded arguments only: the offline transport starts nothing. The real CLI on a real folder with a
// marker CLAUDE.md and a hook is ArtifactModelSettingsNativeTests.
[Trait("Decision", "ED-NEW-web-sec-artifacts-1")]
public sealed class ArtifactModelNoSettingsTests
{
    [Fact]
    public void A_no_tools_launch_loads_no_settings_memory_or_hooks_and_is_never_saved_while_a_chat_launch_is_unchanged()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--plugin-dir --system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        var thread = Guid.NewGuid();
        RecordingClaudeProcess chat, artifact;
        using (PcControlBridge.Parts.Override(() => assembly.Shared))
        {
            chat = Launch(runtime, thread, noTools: false);
            artifact = Launch(runtime, thread, noTools: true);
        }

        // The artifact's call: no setting source at all (the CLI reads "" as none), so no user, project or local settings,
        // CLAUDE.md or hooks; CLAUDE.md files and auto memory off by name as well; nothing written to the session store.
        Assert.Equal("", Value(artifact.Arguments, "--setting-sources"));
        Assert.Single(artifact.Arguments, a => a == "--setting-sources");
        Assert.Contains("--no-session-persistence", artifact.Arguments);
        Assert.Equal("1", artifact.Environment.GetValueOrDefault("CLAUDE_CODE_DISABLE_CLAUDE_MDS"));
        Assert.Equal("1", artifact.Environment.GetValueOrDefault("CLAUDE_CODE_DISABLE_AUTO_MEMORY"));
        Assert.Equal("", Value(artifact.Arguments, "--tools"));

        // The same thread's ordinary chat keeps the user's settings, memory and sessions, as it always did.
        Assert.Equal("user,project,local", Value(chat.Arguments, "--setting-sources"));
        Assert.DoesNotContain("--no-session-persistence", chat.Arguments);
        Assert.False(chat.Environment.ContainsKey("CLAUDE_CODE_DISABLE_CLAUDE_MDS"));
        Assert.False(chat.Environment.ContainsKey("CLAUDE_CODE_DISABLE_AUTO_MEMORY"));
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public void A_no_tools_launch_overrides_a_memory_switch_the_account_environment_turned_back_on()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--plugin-dir"), [])));
        using var storage = new StorageScratchFolder();
        var environment = new ValueMap<string, string>(new Dictionary<string, string>
        {
            ["CLAUDE_CODE_DISABLE_CLAUDE_MDS"] = "0",
            ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "0",
        }, SwiftStringComparer.Instance);

        var artifact = Launch(runtime, Guid.NewGuid(), noTools: true, environment);

        Assert.Equal("1", artifact.Environment["CLAUDE_CODE_DISABLE_CLAUDE_MDS"]);
        Assert.Equal("1", artifact.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"]);
    }

    private static string? Value(string[] arguments, string flag)
    {
        var at = Array.IndexOf(arguments, flag);
        return at >= 0 && at + 1 < arguments.Length ? arguments[at + 1] : null;
    }

    private static RecordingClaudeProcess Launch(TestRuntime runtime, Guid thread, bool noTools, ValueMap<string, string>? environment = null)
    {
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/nth-artifact-model")!,
            Environment = environment ?? ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Plan,
            Model = "fixture-model",
            IgnisThreadID = noTools ? null : thread,
            NoTools = noTools,
        };
        Func<Generation?> acquire = () => null;
        var session = new ClaudeSession(configuration, acquire);
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        session.Stop();
        return call!;
    }
}
