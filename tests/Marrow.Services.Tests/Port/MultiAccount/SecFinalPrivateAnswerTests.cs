using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

// PORT-ONLY: security gate S5 (ED-NEW-secfinal-2). Dextra's private Claude and Pi answers run in a new, empty folder of their own
// per call, removed after it, as a no-tools launch (no setting source, no MCP config, no CLAUDE.md, no saved session), never in
// the shared temp root whose project settings, .mcp.json or CLAUDE.md anything could have written. Pi's text-only head saves no session.
[Trait("Decision", "ED-NEW-secfinal-2")]
public sealed class SecFinalPrivateAnswerTests
{
    private static SessionConfiguration Source() => BuiltInServerFixtures.Config(ProviderKind.Claude);

    [Fact]
    public async Task Each_private_answer_gets_its_own_new_empty_folder_and_a_no_tools_launch_and_the_folder_goes_after()
    {
        var seen = new List<SessionConfiguration>();
        for (var call = 0; call < 2; call++)
        {
            var answer = await DextraTextRoutes.PrivateAnswerAsync(Source(), configuration =>
            {
                seen.Add(configuration);
                Assert.True(Directory.Exists(configuration.WorkingDirectory.Path));
                Assert.Empty(Directory.EnumerateFileSystemEntries(configuration.WorkingDirectory.Path));
                return Task.FromResult("answer " + call);
            });
            Assert.Equal("answer " + call, answer);
        }
        Assert.Equal(2, seen.Count);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        foreach (var configuration in seen)
        {
            Assert.True(configuration.NoTools);
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuration.WorkingDirectory.Path));
            Assert.NotEqual(root, folder, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(Source().WorkingDirectory.Path, configuration.WorkingDirectory.Path);
            Assert.False(Directory.Exists(folder));
        }
        Assert.NotEqual(seen[0].WorkingDirectory.Path, seen[1].WorkingDirectory.Path);
    }

    [Fact]
    public async Task A_failed_private_answer_still_removes_its_folder()
    {
        string? folder = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DextraTextRoutes.PrivateAnswerAsync<string>(Source(), configuration =>
        {
            folder = configuration.WorkingDirectory.Path;
            throw new InvalidOperationException("The account did not return an authorized text answer.");
        }));
        Assert.NotNull(folder);
        Assert.NotEqual(Source().WorkingDirectory.Path, folder); // its own folder, not the route's shared one
        Assert.NotEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparer.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Pis_text_only_head_saves_no_session()
    {
        var arguments = PiTextOnlyHeadPolicy.Arguments("text-only.mjs", "provider/model", null);
        Assert.Contains("--no-session", arguments);
        Assert.Contains("--no-tools", arguments);
        Assert.Contains("--no-context-files", arguments);
    }
}
