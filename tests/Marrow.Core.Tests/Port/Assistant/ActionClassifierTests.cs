using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.1a, 12.1b; ED-NEW-dextracore-2): the classifier table of 12.1b with real payloads,
// each read with all three shell grammars (Auto), and the policy verdict each one gets.
public sealed class ActionClassifierTests
{
    private const string Worktree = @"C:\nth\worktrees\m1";

    internal static ClassifierContext Context() => new()
    {
        MissionWorktree = Worktree,
        AccessFolders = new ValueList<string>([@"C:\Projects\app"]),
        AssistantFolder = @"C:\nth\assistant",
        Allowlist = new ValueList<string>(["dotnet test", "python tools/gen.py", "npm run build"]),
        UnchangedScripts = new ValueList<string>([Worktree + @"\tools\gen.py"]),
        ProtectedRoots = new ValueList<string>([@"C:\nth\settings"]),
        Environment = new ValueMap<string, string>([
            new("USERPROFILE", @"C:\Users\owner"),
            new("TEMP", @"C:\Users\owner\AppData\Local\Temp"),
        ]),
        CommandShadowed = (_, _) => false,
    };

    private static PolicyContext Policy(bool tainted = false) =>
        new(new ValueList<AssistantProfile>([new AssistantProfile("dextra", "Dextra")]), new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero))
        {
            TaintContext = tainted ? TaintContext.Clean.Absorb(TaintedText.Read(ContentOrigin.Web, "https://news.example/a", "page")) : TaintContext.Clean,
        };

    private static Classification Shell(string command) =>
        ActionClassifier.Classify(new ActionRequest.ShellCommand(command), Context());

    private static readonly ValueList<ApprovalRequest.Option> NoOptions = ValueList<ApprovalRequest.Option>.Empty;

    private static string Encoded(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("dotnet test src/Marrow.Core.Tests", ActionClass.Run, Verdict.Allow)]
    [InlineData("git push origin main", ActionClass.Outward, Verdict.Ask)]
    [InlineData("npm publish", ActionClass.Outward, Verdict.Ask)]
    [InlineData("curl -X POST https://api.example.com/x -d @notes.txt", ActionClass.Outward, Verdict.Ask)]
    [InlineData(@"Remove-Item -Recurse C:\Users\owner\Documents\old", ActionClass.Destructive, Verdict.Ask)]
    [InlineData(@"Remove-Item -Recurse .\bin", ActionClass.Work, Verdict.Allow)]
    [InlineData("winget install foo", ActionClass.Install, Verdict.Ask)]
    [InlineData(@"reg add HKLM\Software\Foo /v Bar /d 1 /f", ActionClass.HardDeny, Verdict.Deny)]
    [InlineData("python tools/gen.py", ActionClass.Run, Verdict.Allow)]
    [InlineData("./deploy.sh", ActionClass.Unknown, Verdict.Ask)]
    [InlineData("frobnicate --all", ActionClass.Unknown, Verdict.Ask)]
    public void The_12_1b_shell_rows_get_their_class_and_verdict(string command, ActionClass expected, Verdict verdict)
    {
        var classification = Shell(command);
        Assert.Equal(expected, classification.Class);
        Assert.Equal(verdict, PolicyEngine.Decide(classification, Policy()).Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void An_encoded_PowerShell_post_is_decoded_and_is_Outward()
    {
        var command = "powershell -NoProfile -EncodedCommand " + Encoded("Invoke-WebRequest -Uri https://api.example.com/x -Method Post -Body 'hi'");
        var classification = Shell(command);
        Assert.Equal(ActionClass.Outward, classification.Class);
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(classification, Policy()).Verdict);
        // The same with the short -e and -enc spellings, and with pwsh.
        Assert.Equal(ActionClass.Outward, Shell("pwsh -e " + Encoded("iwr https://api.example.com/x -Method POST")).Class);
        Assert.Equal(ActionClass.Outward, Shell("powershell.exe -enc " + Encoded("Invoke-RestMethod -Method Put -Uri https://x.example")).Class);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void An_encoded_command_that_cannot_be_decoded_is_Unknown()
    {
        Assert.Equal(ActionClass.Unknown, Shell("powershell -EncodedCommand not-base64!").Class);
        Assert.Equal(ActionClass.Unknown, Shell("powershell -EncodedCommand $payload").Class);
        Assert.Equal(ActionClass.Unknown, Shell("powershell -EncodedArguments " + Encoded("x")).Class);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void An_allowlisted_script_asks_once_the_agent_is_tainted()
    {
        var classification = Shell("python tools/gen.py");
        Assert.Equal(ActionClass.Run, classification.Class);
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(classification, Policy()).Verdict);
        var tainted = PolicyEngine.Decide(classification, Policy(tainted: true));
        Assert.Equal(Verdict.Ask, tainted.Verdict);
        Assert.Equal(PolicyRule.Tainted, tainted.Rule);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void A_script_runs_alone_only_inside_the_worktree_unchanged_and_allowlisted()
    {
        // Changed (not in UnchangedScripts), outside the worktree, or not on the allowlist: Unknown.
        Assert.Equal(ActionClass.Unknown, Shell("python tools/other.py").Class);
        Assert.Equal(ActionClass.Unknown, Shell(@"python C:\Users\owner\gen.py").Class);
        var context = Context() with { Allowlist = ValueList<string>.Empty };
        Assert.Equal(ActionClass.Unknown, ActionClassifier.Classify(new ActionRequest.ShellCommand("python tools/gen.py"), context).Class);
        // Arguments that only exist when it runs never match the allowlist.
        Assert.Equal(ActionClass.Unknown, Shell("python tools/gen.py $(cat args.txt)").Class);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    [InlineData("send_message", null, ActionClass.Outward)]
    [InlineData("post_message", true, ActionClass.Outward)]
    [InlineData("do_thing", null, ActionClass.Unknown)]
    public void The_12_1b_MCP_rows_get_their_class(string tool, bool? readOnlyHint, ActionClass expected)
    {
        var server = tool == "send_message" ? "gmail" : tool == "post_message" ? "slack" : "acme";
        var annotations = readOnlyHint is null ? null : new McpToolAnnotations(ReadOnlyHint: readOnlyHint);
        var classification = ActionClassifier.Classify(new ActionRequest.McpTool(server, tool, Annotations: annotations), Context());
        Assert.Equal(expected, classification.Class);
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(classification, Policy()).Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-2")]
    public void A_provider_approval_maps_through_its_title()
    {
        var command = new ApprovalRequest("a1", ApprovalRequest.Kind.Command, "git push origin main", NoOptions);
        Assert.Equal(ActionClass.Outward, ActionClassifier.ClassifyApproval(command, Context()).Class);
        var mcp = new ApprovalRequest("a2", ApprovalRequest.Kind.Tool, "mcp__gmail__send_message", NoOptions);
        Assert.Equal(ActionClass.Outward, ActionClassifier.ClassifyApproval(mcp, Context()).Class);
        var edit = new ApprovalRequest("a3", ApprovalRequest.Kind.FileChange, @"C:\Users\owner\Desktop\notes.txt", NoOptions);
        Assert.Equal(ActionClass.WriteElsewhere, ActionClassifier.ClassifyApproval(edit, Context()).Class);
        var unnamed = new ApprovalRequest("a4", ApprovalRequest.Kind.FileChange, "Edit file", NoOptions);
        Assert.Equal(ActionClass.Unknown, ActionClassifier.ClassifyApproval(unnamed, Context()).Class);
        var access = new ApprovalRequest("a5", ApprovalRequest.Kind.Permissions, "Grant extra access?", NoOptions);
        Assert.Equal(ActionClass.Unknown, ActionClassifier.ClassifyApproval(access, Context()).Class);
    }
}
