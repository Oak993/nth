// PORT-ONLY: SK-11b / WU-7 offline acceptance. All text is Marrow-made fixture text; no process, network or real CLI.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexSkillRootsTests
{
    private static string Root(string name) => Path.Combine(Path.GetTempPath(), "marrow-sk11b", name);

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task Register_sends_skills_list_then_extraRoots_with_the_absolute_generation_path()
    {
        var wire = new CodexRecordingWire();
        var folder = Root("chat-1");
        await CodexSkillRoots.Register(wire.Request, "/work/project", folder, listFirst: true);
        Assert.Equal(["skills/list", "skills/extraRoots/set"], wire.Sent.Select(s => s.Method));
        Assert.Equal(CodexWiringFixture.Bytes(CodexWiringFixture.Json("{\"cwds\":[\"/work/project\"],\"forceReload\":true}")), CodexWiringFixture.Bytes(CodexWiringFixture.Json(wire.Sent[0].Params)));
        var roots = CodexWiringFixture.Json(wire.Sent[1].Params)["extraRoots"]!.JSONValueArray!;
        Assert.Equal([folder], roots.Select(r => r.JSONValueString));
        Assert.True(Path.IsPathFullyQualified(roots[0].JSONValueString!));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_listing_probe_sends_only_extraRoots_because_ListCommands_sends_its_own_skills_list()
    {
        var wire = new CodexRecordingWire();
        await CodexSkillRoots.Register(wire.Request, "/work/project", Root("list-key"), listFirst: false);
        Assert.Equal(["skills/extraRoots/set"], wire.Sent.Select(s => s.Method));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task An_error_reply_from_an_old_codex_is_tolerated_and_the_session_carries_on()
    {
        var wire = new CodexRecordingWire { Fail = method => method == "skills/extraRoots/set" ? new RPCError(-32601, "Method not found") : null };
        await CodexSkillRoots.Register(wire.Request, "/work/project", Root("chat-1"), listFirst: true);
        Assert.Equal(["skills/list", "skills/extraRoots/set"], wire.Sent.Select(s => s.Method));

        var listFails = new CodexRecordingWire { Fail = method => method == "skills/list" ? new RPCError(-32600, "bad") : null };
        await CodexSkillRoots.Register(listFails.Request, "/work/project", Root("chat-1"), listFirst: true);
        Assert.Equal(["skills/list", "skills/extraRoots/set"], listFails.Sent.Select(s => s.Method));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_transport_failure_is_not_swallowed()
    {
        var wire = new CodexRecordingWire { Fail = _ => new InvalidOperationException("the connection is gone") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CodexSkillRoots.Register(wire.Request, "/work/project", Root("chat-1"), listFirst: true));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_relative_folder_is_refused_before_anything_is_sent()
    {
        var wire = new CodexRecordingWire();
        await Assert.ThrowsAsync<ArgumentException>(() => CodexSkillRoots.Register(wire.Request, "/work/project", "views/codex/chat-1", listFirst: true));
        await Assert.ThrowsAsync<ArgumentException>(() => CodexSkillRoots.Register(wire.Request, "/work/project", "", listFirst: false));
        Assert.Empty(wire.Sent);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public async Task Skills_config_write_is_never_sent_whatever_the_replies()
    {
        var wire = new CodexRecordingWire { Fail = _ => new RPCError(-32601, "nope") };
        await CodexSkillRoots.Register(wire.Request, "/work/project", Root("chat-1"), listFirst: true);
        Assert.DoesNotContain(wire.Sent, s => s.Method.Contains("config", StringComparison.Ordinal));
        Assert.DoesNotContain(wire.Sent, s => s.Params.Contains("config", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Every_connection_sends_it_again_and_two_sessions_send_two_different_roots()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        using var one = service.Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        using var two = service.Start("codex", "chat-b", [item], f.Switches((item, true)), f.Budget);
        var wire = new CodexRecordingWire();
        await CodexSkillRoots.Register(wire.Request, "/work/project", one.Folder, listFirst: true);
        await CodexSkillRoots.Register(wire.Request, "/work/project", one.Folder, listFirst: true); // a reconnect of the same chat
        await CodexSkillRoots.Register(wire.Request, "/work/project", two.Folder, listFirst: true);
        var roots = wire.Sent.Where(s => s.Method == "skills/extraRoots/set").Select(s => CodexWiringFixture.Json(s.Params)["extraRoots"]![0]!.JSONValueString!).ToArray();
        Assert.Equal([one.Folder, one.Folder, two.Folder], roots);
        Assert.NotEqual(one.Folder, two.Folder);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_generation_folder_is_the_flat_list_of_skill_folders_Codex_expects()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        using var g = f.Open().Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        var skill = Assert.Single(g.Skills);
        Assert.Equal(g.Folder, Path.GetDirectoryName(skill.Folder));
        Assert.Equal("SKILL.md", Path.GetFileName(skill.File));
    }
}
