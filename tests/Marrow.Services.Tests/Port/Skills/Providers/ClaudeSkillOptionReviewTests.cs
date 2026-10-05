using System.Text;
using Marrow.Testing;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class ClaudeSkillOptionReviewTests
{
    [Theory, Trait("Feature", "SK-11a"), Trait("Rule", "R-TEST-9")]
    [InlineData("--plugin-directory <path>")]
    [InlineData("--plugin-dir-old <path>")]
    [InlineData("--no-plugin-dir")]
    public void A_near_miss_option_does_not_acquire_skills_or_enable_the_missing_flag(string options)
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes(options), [])));
        RecordingClaudeProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => process = new(args, env));
        var count = 0;
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
            WorkingDirectory = SwiftURL.Create(scratch.Root)!, ResumeID = "fixture-chat",
            Environment = ValueMap<string,string>.Empty,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build
        };
        var session = new ClaudeSession(config, () => { count++; return null; });
        try
        {
            runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
            Assert.NotNull(process);
            Assert.Equal(0, count);
            Assert.DoesNotContain("--plugin-dir", process.Arguments);
        }
        finally { session.Stop(); runtime.Loop.RunUntilIdle(); }
    }
}
