using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :458-474.
public sealed class AppModelHydraMergeRequestRetryTests
{
    [Theory]
    [InlineData("auth", "401 retry refused")]
    [InlineData("other", "server retry failed")]
    [InlineData("existing", "already exists !9")]
    [Trait("Req", "PR-HYDRAMERGE-051")]
    public void A_second_creation_failure_is_final_without_another_retry_or_existing_request_lookup(string scenario, string retryDetail)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project", origin: false);
        f.Repos[project.Id].Git("remote", "add", "origin", "https://gitlab.example/group/repo.git");
        var lead = f.AddLead(project, "Lead", []);
        f.Start(lead);
        var forge = new ForgeScript(project.Path);
        forge.Reply(0);
        forge.Reply(1, stderr: "401 Unauthorized", status: "1");
        forge.Reply(2);
        forge.Reply(3, stderr: retryDetail, status: "1");

        var error = Assert.ThrowsAny<SwiftError>(() => f.H.Main.Loop.Run(async () =>
            await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model,
                new Git(project.Path), "Subject", "Body", "hydra/source", "main")!, AppModelHarness.Bound));

        Assert.Equal(4, forge.Count);
        var calls = forge.Calls();
        Assert.Equal(calls[1].Arguments, calls[3].Arguments);
        if (scenario == "auth")
        {
            Assert.Equal("GitLabAuth", error.GetType().Name);
            Assert.Contains("\n\n" + retryDetail + "\n\n", SwiftErrors.LocalizedDescription(error), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("CreationFailed", error.GetType().Name);
            Assert.Equal(retryDetail, SwiftErrors.LocalizedDescription(error));
        }
    }
}
