using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :354-482,934-947.
public sealed class AppModelHydraMergeRequestTests
{
    [Theory]
    [InlineData("existing", 2)]
    [InlineData("auth-refresh", 4)]
    [InlineData("false-auth", 4)]
    [InlineData("missing-existing", 4)]
    [Trait("Req", "PR-HYDRAMERGE-051")] // App/AppModel+HydraMerge.swift:462-472
    public void Existing_request_precedes_authentication_and_a_refreshed_token_retries_the_identical_vector_once(string scenario, int count)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", "https://user:pass@gitlab.example/group/repo.git");
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        forge.Reply(0); // :454 pre-check succeeds.
        if (scenario == "existing") forge.Reply(1, stderr: "401: merge request already exists !7", status: "1");
        else
        {
            forge.Reply(1, stderr: scenario == "false-auth" ? "project description 401" : scenario == "missing-existing" ? "already exists but no link; 401" : "401 Unauthorized", status: "1");
            forge.Reply(2);
            forge.Reply(3, stdout: "https://gitlab.example/group/repo/-/merge_requests/7");
        }
        var calls = new List<ShellInvocation>();
        var url = f.H.Main.Loop.Run(async () =>
        {
            using var observation = ShellObserver.Observe(calls.Add);
            return await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!;
        }, AppModelHarness.Bound);
        Assert.Equal("https://gitlab.example/group/repo/-/merge_requests/7", url!.AbsoluteString);
        var glab = forge.Calls();
        Assert.Equal(count, glab.Count);
        Assert.Equal(["auth", "status"], glab[0].Arguments);
        var expected = new[] { "mr", "create", "--title", "Subject", "--description", "Body", "--yes", "--squash-before-merge",
            "--repo", "https://gitlab.example/group/repo", "--source-branch", "hydra/source", "--target-branch", "main" };
        Assert.Equal(expected, glab[1].Arguments);
        if (scenario != "existing") { Assert.Equal(["auth", "status"], glab[2].Arguments); Assert.Equal(expected, glab[3].Arguments); }
        var tools = calls.Where(c => Path.GetFileNameWithoutExtension(c.Executable) == "glab").ToArray();
        Assert.Equal(count, tools.Length);
        Assert.Equal(30, tools[0].Timeout);
        foreach (var c in tools.Where(c => c.Arguments.Contains("create"))) Assert.Equal(300, c.Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-109")] // App/AppModel+HydraMerge.swift:454-455,471-472,942-943
    public void Both_GitLab_auth_throw_sites_render_the_exact_host_branch_and_raw_failure_paragraph(bool afterCreate)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", "https://gitlab.example/group/repo.git");
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        if (afterCreate)
        {
            forge.Reply(0);
            forge.Reply(1, stderr: "401 Unauthorized\nraw detail", status: "1");
            forge.Reply(2, stderr: "expired status", status: "1");
        }
        else forge.Reply(0, stderr: "raw detail\nsecond line", status: "1");
        var error = Assert.ThrowsAny<SwiftError>(() => f.H.Main.Loop.Run(async () =>
            await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!, AppModelHarness.Bound));
        var detail = afterCreate ? "401 Unauthorized\nraw detail" : "raw detail\nsecond line";
        Assert.Equal("GitLab refused the login (401 Unauthorized) for gitlab.example: the token glab signs in with is missing or expired.\n\n" + detail +
            "\n\nSign back in with `glab auth login --hostname gitlab.example` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `hydra/source` is already pushed, so the team's work is safe.", SwiftErrors.LocalizedDescription(error));
        Assert.Equal(afterCreate ? 3 : 1, forge.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("raw detail\n\nline two")]
    [Trait("Req", "PR-HYDRAMERGE-109")] // App/AppModel+HydraMerge.swift:942-943
    public void Auth_error_preserves_an_empty_detail_and_the_unknown_host_literal(string detail)
    {
        var type = typeof(AppModel).Assembly.GetType("Marrow.App.HydraMergeRequestError+GitLabAuth")!;
        var error = (SwiftError)Activator.CreateInstance(type, "your GitLab host", "source", detail)!;
        Assert.Equal("GitLab refused the login (401 Unauthorized) for your GitLab host: the token glab signs in with is missing or expired.\n\n" + detail +
            "\n\nSign back in with `glab auth login --hostname your GitLab host` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `source` is already pushed, so the team's work is safe.", SwiftErrors.LocalizedDescription(error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-110")] // App/AppModel+HydraMerge.swift:477-482,944-945
    public void Creation_failure_renders_the_last_attempts_message_bare_including_a_recovery_failure(bool recover)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", "https://gitlab.example/group/repo.git");
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        forge.Reply(0);
        forge.Reply(1, stdout: "unimportant stdout", stderr: recover ? "recover original" : "bare\nerror", status: "1");
        forge.Reply(2, stdout: "unimportant last stdout", stderr: "last\nrecovery error", status: "1");
        var error = Assert.ThrowsAny<SwiftError>(() => f.H.Main.Loop.Run(async () =>
            await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "source", "main")!, AppModelHarness.Bound));
        Assert.Equal(recover ? "last\nrecovery error" : "bare\nerror", SwiftErrors.LocalizedDescription(error));
        Assert.Equal(recover ? 3 : 2, forge.Count);
        if (recover) Assert.Equal(forge.Calls()[1].Arguments.Append("--recover"), forge.Calls()[2].Arguments);
        var type = typeof(AppModel).Assembly.GetType("Marrow.App.HydraMergeRequestError+CreationFailed")!;
        Assert.Equal("", SwiftErrors.LocalizedDescription((SwiftError)Activator.CreateInstance(type, "")!));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("create")]
    [InlineData("link")]
    [InlineData("merge")]
    [InlineData("generic")]
    [Trait("Req", "PR-HYDRAMERGE-055")] // App/AppModel+HydraMerge.swift:355-362
    [Trait("Req", "PR-HYDRAMERGE-056")] // App/AppModel+HydraMerge.swift:363-366
    [Trait("Req", "PR-HYDRAMERGE-058")] // App/AppModel+HydraMerge.swift:368-372
    [Trait("Req", "PR-HYDRAMERGE-066")] // App/AppModel+HydraMerge.swift:406-408
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:226-237
    public void Each_failure_contract_preserves_project_prefix_metadata_retry_and_note_threshold(string site)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var web = "https://gitlab.example/group/repo.git";
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team\n");
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        var branch = f.Branch(lead);
        const string url = "https://gitlab.example/group/repo/-/merge_requests/95";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var n = forge.Count;
            var errorText = "failure " + attempt;
            forge.Reply(n, stderr: site == "auth" ? errorText : "", status: site == "auth" ? "1" : "0");
            forge.Reply(n + 1, stdout: site == "link" ? "https://gitlab.example/group/repo/-/merge_requests/0" : site == "create" ? "" : url,
                stderr: site == "create" ? errorText : "", status: site == "create" ? "1" : "0");
            forge.Reply(n + 2, stderr: site == "merge" ? errorText : "", status: site == "merge" ? "1" : "0");
            var pushed = false;
            var result = f.Pass(lead, p, several: true, observer: call =>
            {
                if (HydraMergeReviewFixture.IsGit(call, "push")) pushed = true;
                // Fetch and push have finished against the LOCAL origin before the forge reads its identity.
                if (pushed && f.Runtime(lead).HydraMergeStep == HydraMergeStage.Opening && HydraMergeReviewFixture.IsGit(call, "remote"))
                {
                    using var suppress = ShellObserver.Observe(_ => { });
                    repo.Git("remote", "set-url", "origin", web);
                }
                if (site == "generic" && HydraMergeReviewFixture.IsGit(call, "commit-tree"))
                    throw (SwiftError)Activator.CreateInstance(typeof(AppModel).Assembly.GetType("Marrow.App.HydraMergeRequestError+CreationFailed")!, errorText)!;
            });
            repo.Git("remote", "set-url", "origin", f.Origins[p.Id]);
            Assert.Equal("Failed", result);
            var record = f.Runtime(lead).HydraMerges.Last();
            var title = site == "generic" ? "Project: Hydra could not merge the team's work." : site == "merge" ? "Project: Hydra opened !95 but could not merge it." : $"Project: Hydra pushed {branch} but could not open a merge request.";
            var body = site switch
            {
                "auth" => "GitLab refused the login (401 Unauthorized) for gitlab.example: the token glab signs in with is missing or expired.\n\n" + errorText + $"\n\nSign back in with `glab auth login --hostname gitlab.example` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `{branch}` is already pushed, so the team's work is safe.",
                "create" => errorText + $"\n\nThe work is in the checkout and on `{branch}`; the next try opens the request.",
                "link" => "The forge did not say where the request is. The next try opens it again.",
                "merge" => url + "\n\n" + errorText + "\n\nThe next try brings the request up to date with `main` and merges it.",
                _ => errorText + "\n\nThe work is in the checkout; the next try sends it again.",
            };
            Assert.Equal(title + ": " + body, record.Detail);
            Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
            Assert.Equal("Project", record.Project); Assert.Equal(0, record.Files);
            Assert.Equal(site == "auth" ? (bool?)null : true, record.Retryable);
            Assert.Equal(site == "merge" ? "!95" : null, record.Label);
            Assert.Equal(site == "merge" ? url : null, record.Url?.AbsoluteString);
            Assert.Equal(site != "auth", f.Runtime(lead).HasHydraMergeRetryPending);
            var shouldSpeak = site == "auth" || attempt == 2 && site != "link";
            Assert.Equal(site == "auth" ? attempt + 1 : shouldSpeak ? 1 : 0, f.Notes(lead).Length);
            if (shouldSpeak) Assert.Equal(title + "\n" + body, f.Notes(lead).Last());
        }
        // :230 identical LAST detail repeats neither the record nor the note.
        Assert.Equal(site == "link" ? 1 : 3, f.Runtime(lead).HydraMerges.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://gitlab.example/group/repo/-/merge_requests/0")]
    [InlineData("https://github.example/owner/repo/pull/0")]
    [InlineData("https://forge.example/owner/repo/pulls/0")]
    [InlineData("https://forge.example/nested/owner/repo/pulls/7")]
    [Trait("Req", "PR-HYDRAMERGE-056")] // App/AppModel+HydraMerge.swift:363-366; Models/MergeRequestLink.swift:16-47
    public void Missing_or_unparseable_request_links_speak_only_after_two_previous_retryable_failures(string url)
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        f.Forge.Reply(0, stdout: url);
        for (var i = 0; i < 2; i++) f.H.Main.OnMain(() => f.Runtime.RecordHydraMerge(new HydraMergeRecord
        { At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Files = 0, Detail = "earlier " + i, Retryable = true }));
        f.Run();
        var branch = (string)HydraMergeProofFixture.Call("HydraBranchName", null, f.Lead)!;
        var title = $"Hydra pushed {branch} but could not open a merge request.";
        const string body = "The forge did not say where the request is. The next try opens it again.";
        var r = f.Runtime.HydraMerges.Last();
        Assert.Equal(title + ": " + body, r.Detail);
        Assert.Equal(title + "\n" + body, Assert.Single(f.Notes));
        Assert.True(r.Retryable); Assert.Null(r.Label); Assert.Null(r.Url); Assert.Equal(0, r.Files);
        Assert.True(f.Runtime.HasHydraMergeRetryPending);
        Assert.Equal(3, f.Runtime.HydraMerges.Count);
        Assert.Equal(1, f.Forge.Count); // No merge command for a missing/unparseable request.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:228-235
    public void The_last_two_records_must_both_be_retryable_failures_and_an_identical_detail_still_schedules_a_new_retry(bool mixed)
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        f.Forge.Reply(0, stderr: "offline create", status: "1");
        f.Forge.Reply(1, stderr: "offline create", status: "1");
        for (var i = 0; i < 2; i++) f.H.Main.OnMain(() => f.Runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = i == 1 && mixed ? HydraMergeRecord.Outcome.Merged : HydraMergeRecord.Outcome.Failed,
            Files = 0, Detail = "prior " + i, Retryable = i == 1 && !mixed ? null : true,
        }));
        f.Run();
        Assert.Equal(3, f.Runtime.HydraMerges.Count);
        Assert.Empty(f.Notes);
        Assert.True(f.Runtime.HasHydraMergeRetryPending);
        f.H.Main.OnMain(() => f.Runtime.HasHydraMergeRetryPending = false); // The old scheduled attempt is no longer pending.
        f.Run();
        Assert.True(f.Runtime.HasHydraMergeRetryPending);
        Assert.Equal(3, f.Runtime.HydraMerges.Count);
        Assert.Empty(f.Notes);
    }
}
