using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// T28 real initialize reply, 2026-10-07, codex-cli 0.159.3 on owner's Windows PC.
// This reproduces that wire shape with the existing offline fake; never launches the real CLI.
[Trait("Rule", "R-TEST-4")]
public sealed class CodexDesktopStandbyGateTests
{
    [Theory]
    [InlineData("Codex Desktop/0.159.3 (Windows 10.0.26300; x86_64) dumb (marrow; 1.7.0)", true)]
    [InlineData("Codex Desktop/0.159.2 (Windows 10.0.26300; x86_64) dumb (marrow; 1.7.0)", false)]
    [InlineData("Codex Desktop/no-version (marrow; 1.7.0)", false)]
    public void The_desktop_originator_with_a_space_uses_the_CLI_version_not_the_client_version(string userAgent, bool expected) =>
        Assert.Equal(expected, CodexHistoryInjection.VersionSupportsInjection(userAgent));

    [Fact]
    public async Task Desktop_user_agent_versions_do_not_share_a_schema_capability_cache_entry()
    {
        using var storage = new StorageScratchFolder();
        var executable = SwiftURL.Create("marrow-native-path:" + Path.Combine(storage.Root, "codex.exe").Replace('\\', '/'))!;
        var probes = 0;
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(++probes == 1));
        Assert.True(await CodexHistoryInjection.SupportedAsync(executable, new Dictionary<string, string>(), "Codex Desktop/0.159.3 (Windows)"));
        Assert.False(await CodexHistoryInjection.SupportedAsync(executable, new Dictionary<string, string>(), "Codex Desktop/0.160.0 (Windows)"));
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task The_real_desktop_initialize_shape_can_pass_the_offline_standby_capability_probe()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = new CodexScript("t28-desktop-initialize");
        script.SetResponse("initialize", "{\"userAgent\":\"Codex Desktop/0.159.3 (Windows 10.0.26300; x86_64) dumb (marrow; 1.7.0)\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}");
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
            Executable = SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + Path.GetDirectoryName(script.ExecutablePath)!.Replace('\\', '/'))!,
            Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        };
        var check = new CodexSession(configuration).SupportsStandbyInjection(CancellationToken.None);
        for (var i = 0; i < 200 && !check.IsCompleted; i++)
        {
            runtime.RunUntilIdle();
            SpinWait.SpinUntil(() => check.IsCompleted, TimeSpan.FromMilliseconds(25));
        }
        Assert.True(await check);
    }
}
