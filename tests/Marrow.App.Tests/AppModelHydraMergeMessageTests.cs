using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :822-841 (the commit message of a merge). The text
// engine is the offline forge stand-in standing in for the claude CLI; no model and no network are reached.
public sealed class AppModelHydraMergeMessageTests
{
    private const string Instructions = "Use Conventional Commits";

    [Theory]
    [InlineData("text")]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("fails")]
    [Trait("Req", "PR-HYDRAMERGE-036")] // AppModel+HydraMerge.swift:823-834
    [Trait("Req", "PR-HYDRAMERGE-037")] // AppModel+HydraMerge.swift:835-840 (an engine that answers nothing usable falls through)
    public void The_commit_message_comes_from_the_text_engine_and_an_answer_that_is_nothing_falls_through_to_the_fallback(string answer)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        HydraMergeSpec.Commit(repo, ("d.txt", "delete me\n"), ("r.txt", "rename me\nline two\nline three\nline four\n"));
        repo.Git("push", "origin", "main");
        var lead = f.AddLead(p, "Chat title", ["a.txt", "n.txt", "d.txt", "r.txt", "renamed.txt"]);
        f.AddHead(lead, ["a.txt"], index: 1, task: "Parser fix");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        File.WriteAllText(Path.Combine(p.Path, "n.txt"), "new file\n");
        File.Delete(Path.Combine(p.Path, "d.txt"));
        File.Move(Path.Combine(p.Path, "r.txt"), Path.Combine(p.Path, "renamed.txt"));
        f.Start(lead);
        f.H.Main.OnMain(() =>
        {
            f.Model.Settings.TextGeneration = TextGenerationChoice.Claude;
            f.Model.Settings.SetBinaryPath(ForgeStandIns.PathOf("glab"), ProviderKind.Claude);
            f.Model.Settings.CommitInstructions = Instructions;
        });
        var script = new ForgeScript(p.Path);
        switch (answer)
        {
            case "text": script.Reply(0, stdout: "Generated subject\n\n- Full body"); break;
            case "empty": script.Reply(0, stdout: ""); break;
            case "blank": script.Reply(0, stdout: "  \n\n"); break;
            default: script.Reply(0, stderr: "the model is unavailable", status: "1"); break;
        }
        script.Reply(1, stdout: HydraMergeSpec.RequestURL);
        var calls = new List<ShellInvocation>();

        f.Run(lead, calls.Add);

        var engine = Assert.Single(calls, call => Path.GetFileNameWithoutExtension(call.Executable) == "glab");
        Assert.Contains("-p", engine.Arguments);
        // The checkout is the engine's working directory.
        Assert.Equal(p.Path, engine.Directory);
        var prompt = Encoding.UTF8.GetString(engine.Input!);
        Assert.Contains("\nFollow these instructions too: " + Instructions + "\n", prompt, StringComparison.Ordinal);
        // One line per file: A for an added file, D for a deleted one, M for any other (a modified file, a renamed one).
        var summary = prompt[(prompt.IndexOf("Changed files:\n", StringComparison.Ordinal) + "Changed files:\n".Length)..prompt.IndexOf("\n\nPatch:\n", StringComparison.Ordinal)];
        Assert.Equal(["A n.txt", "D d.txt", "M a.txt", "M renamed.txt"], summary.Split('\n').Order(StringComparer.Ordinal));
        Assert.Contains("\n\nPatch:\ndiff --git ", prompt, StringComparison.Ordinal);
        var expected = answer == "text" ? "Generated subject\n\n- Full body" : "Chat title\n\n- Walter: Parser fix";
        var commit = calls.Single(call => HydraMergeReviewFixture.IsGit(call, "commit-tree"));
        Assert.Equal(expected, Encoding.UTF8.GetString(commit.Input!));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-037")] // AppModel+HydraMerge.swift:835-840
    public void Without_an_engine_the_commit_message_is_the_chat_title_over_the_heads_tasks()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var untitled = f.AddLead(p, "x", []);
        untitled = HydraMergeSpec.Change(f, untitled, t => t with { Title = ChatThread.Untitled });
        var padded = f.AddLead(p, "  Padded title  ", []);
        var bare = f.AddLead(p, "Bare title", []);
        var empty = f.AddLead(p, "Only silent heads", []);
        f.AddHead(untitled, [], index: 1, task: "Parser fix");
        f.AddHead(untitled, [], index: 2, task: "");
        f.AddHead(untitled, [], index: 3, task: new string('b', 130));
        f.AddHead(untitled, [], index: 4, task: "  Multi\nline  ");
        f.AddHead(empty, [], index: 1, task: "");
        f.Start(untitled, padded, bare, empty);
        Assert.Null(f.Model.TextEngine(ProviderKind.Codex));
        Assert.Null(f.Model.TextEngine(ProviderKind.Claude));

        string Message(ChatThread lead) => f.H.Main.Loop.Run(() => (Task<string>)HydraMergeProofFixture.Call(
            "CommitMessage", f.Model, lead, ValueList<DiffFile>.Empty, "", p.Path)!, AppModelHarness.Bound);

        // Line 1 is the title, or the fixed words for an untitled chat; line 2 is empty; then "- <name>: <task>" per head with a
        // task, one line each and at most 100 characters (99 and an ellipsis).
        Assert.Equal("Hydra: the team's work\n\n- Walter: Parser fix\n- Otto: " + new string('b', 99) + "…\n- Nova: Multi", Message(untitled));
        // The whole message is trimmed: a team with no tasks collapses to the title, and the title's own padding goes too.
        Assert.Equal("Padded title", Message(padded));
        Assert.Equal("Bare title", Message(bare));
        Assert.Equal("Only silent heads", Message(empty));
    }
}
