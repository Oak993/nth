using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :437-455,942-943.
public sealed class AppModelHydraMergeUnknownHostTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-109")] // App/AppModel+HydraMerge.swift:438,454-455,942-943
    public void A_GitLab_remote_without_a_web_URL_uses_the_literal_unknown_host_in_both_places()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project", origin: false);
        f.Repos[project.Id].Git("remote", "add", "origin", "gitlab:group/repo.git");
        var lead = f.AddLead(project, "Lead", []);
        f.Start(lead);
        var forge = new ForgeScript(project.Path);
        forge.Reply(0, stderr: "raw detail", status: "1");

        var error = Assert.ThrowsAny<SwiftError>(() => f.H.Main.Loop.Run(async () =>
            await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model,
                new Git(project.Path), "Subject", "Body", "hydra/source", "main")!, AppModelHarness.Bound));

        Assert.Equal("GitLab refused the login (401 Unauthorized) for your GitLab host: the token glab signs in with is missing or expired.\n\nraw detail\n\n" +
            "Sign back in with `glab auth login --hostname your GitLab host` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `hydra/source` is already pushed, so the team's work is safe.",
            SwiftErrors.LocalizedDescription(error));
        Assert.Equal(1, forge.Count);
    }
}
