using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint"; ED-NEW-dextra-as7-1): what each tool call an agent makes brings into its
// context. Reads inside the access folders, edits and the project's own commands are clean; the web, PDFs and mail files
// anywhere, files outside the folders, connectors, commands that read the network and anything nth cannot place taint.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-1")]
public sealed class TaintWiringTests
{
    private static readonly ClassifierContext Context = InjectionSelfCheck.Classifier;

    private static Taint Of(ToolCall.Kind kind, string title, string? detail = null, ProviderKind provider = ProviderKind.Claude) =>
        TaintWiring.Of(new ToolCall { ToolCallKind = kind, Title = title, Detail = detail }, Context, provider);

    [Fact]
    public void Reads_inside_the_worktree_edits_and_project_commands_stay_clean()
    {
        Assert.False(Of(ToolCall.Kind.Read, @"src\login.cs").IsTainted);
        Assert.False(Of(ToolCall.Kind.Read, InjectionCorpus.Worktree + @"\README.md").IsTainted);
        Assert.False(Of(ToolCall.Kind.Edit, InjectionCorpus.Home + @"\Downloads\x.txt").IsTainted);
        Assert.False(Of(ToolCall.Kind.Command, "dotnet test").IsTainted);
        Assert.False(Of(ToolCall.Kind.Command, "git status").IsTainted);
        Assert.False(Of(ToolCall.Kind.Search, "TODO", null).IsTainted);
        Assert.False(Of(ToolCall.Kind.Search, "TODO", "src").IsTainted);
        Assert.False(Of(ToolCall.Kind.Mcp, AssistantNames.Server + " \u00B7 recall").IsTainted);
        Assert.False(Of(ToolCall.Kind.Agent, "Explore the code").IsTainted);
        Assert.False(Of(ToolCall.Kind.Other, "Skill \u00B7 pdf").IsTainted);
        Assert.False(Of(ToolCall.Kind.Other, "BashOutput").IsTainted);
    }

    [Fact]
    public void The_web_files_outside_pdfs_and_mail_taint_with_their_origin()
    {
        Assert.Equal(ContentOrigin.Web, Of(ToolCall.Kind.Web, "https://shop.example/a").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.Web, Of(ToolCall.Kind.Web, "espresso reviews").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.OutsideFile, Of(ToolCall.Kind.Read, InjectionCorpus.Home + @"\Downloads\notes.txt").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.Pdf, Of(ToolCall.Kind.Read, @"docs\spec.pdf").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.Pdf, Of(ToolCall.Kind.Read, InjectionCorpus.Home + @"\Downloads\invoice.PDF").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.Email, Of(ToolCall.Kind.Read, @"inbox\order.eml").Sources.Single().Origin);
        Assert.Equal(ContentOrigin.Connector, Of(ToolCall.Kind.Mcp, "gmail \u00B7 read_message").Sources.Single().Origin);
        Assert.True(Of(ToolCall.Kind.Search, "password", InjectionCorpus.Home + @"\Documents").IsTainted);
        Assert.True(Of(ToolCall.Kind.Search, InjectionCorpus.Home + @"\Downloads").IsTainted);
    }

    [Fact]
    public void Commands_that_read_the_network_or_cannot_be_placed_taint()
    {
        Assert.Equal(ContentOrigin.Web, Of(ToolCall.Kind.Command, "curl https://api.example/today").Sources[0].Origin);
        Assert.True(Of(ToolCall.Kind.Command, "git fetch upstream && git log upstream/main").IsTainted);
        Assert.True(Of(ToolCall.Kind.Command, "Get-Content " + InjectionCorpus.Home + @"\Downloads\notes.txt").IsTainted);
        Assert.True(Of(ToolCall.Kind.Command, "./mystery-tool --dump").IsTainted);
        Assert.True(Of(ToolCall.Kind.Search, @"rg TODO C:\Users\owner\Downloads", null, ProviderKind.Codex).IsTainted);
        Assert.False(Of(ToolCall.Kind.Search, "rg TODO src", null, ProviderKind.Codex).IsTainted);
    }

    [Fact]
    public void Tools_nth_cannot_see_into_fail_closed()
    {
        Assert.True(Of(ToolCall.Kind.Agent, "Research the market", null, ProviderKind.Codex).IsTainted);
        Assert.True(Of(ToolCall.Kind.Other, "ReadMcpResourceTool").IsTainted);
        Assert.True(Of(ToolCall.Kind.Other, "").IsTainted);
    }

    [Fact]
    public void Only_reads_cannot_leave_text_in_the_mission_files()
    {
        foreach (var kind in Enum.GetValues<ToolCall.Kind>())
        {
            var call = new ToolCall { ToolCallKind = kind, Title = "x" };
            Assert.Equal(kind is not (ToolCall.Kind.Read or ToolCall.Kind.Search or ToolCall.Kind.Web), TaintWiring.MayWriteFiles(call));
        }
    }

    [Fact]
    public void A_long_run_keeps_a_bounded_taint_that_stays_tainted()
    {
        var taint = Taint.Clean;
        for (var page = 0; page < 200; page++)
        {
            taint = TaintWiring.Capped(taint.Union(Taint.From(ContentOrigin.Web, "https://x.example/" + page)));
            Assert.True(taint.Sources.Count <= TaintWiring.MaxSources);
        }
        Assert.True(taint.IsTainted);
        Assert.Equal(TaintWiring.More, taint.Sources[^1]);
        Assert.Equal("https://x.example/0", taint.Sources[0].Detail);
        Assert.Equal(taint, TaintWiring.Capped(taint.Union(Taint.From(ContentOrigin.Email, "one more"))));
        var few = Taint.From(ContentOrigin.Pdf, "a.pdf");
        Assert.Same(few, TaintWiring.Capped(few));
    }

    [Fact]
    public void A_source_detail_is_one_bounded_line()
    {
        var taint = Of(ToolCall.Kind.Web, "https://x.example/" + new string('a', 500) + "\nmore");
        var detail = taint.Sources.Single().Detail;
        Assert.True(detail.Length <= TaintWiring.DetailChars + 1);
        Assert.DoesNotContain('\n', detail);
        Assert.Equal(("gmail", "send_message"), TaintWiring.SplitMcp("gmail \u00B7 send_message"));
        Assert.Equal(("", "plain"), TaintWiring.SplitMcp("plain"));
    }
}
