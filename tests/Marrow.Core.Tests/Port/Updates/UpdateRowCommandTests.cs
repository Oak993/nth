using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-4 (OD-0100 U1 failure line): which items have a command to run by hand.
public sealed class UpdateRowCommandTests
{
    private static UpdateRow Row(string id, UpdateItemKind kind) => new(id, "T", kind, UpdateRowState.Failed, "1", "2", null, null, false);

    [Fact]
    [Trait("Feature", "UE-4")]
    [Trait("Rule", "R-TEST-4")]
    public void Claude_and_Codex_have_their_update_command_and_nothing_else_does()
    {
        Assert.Equal("claude update", UpdateRowCommand.For(Row("cli:claude", UpdateItemKind.ProviderCli)));
        Assert.Equal("codex update", UpdateRowCommand.For(Row("cli:codex", UpdateItemKind.ProviderCli)));
        Assert.Null(UpdateRowCommand.For(Row("cli:opencode", UpdateItemKind.ProviderCli)));
        Assert.Null(UpdateRowCommand.For(Row("cli:claude", UpdateItemKind.DevTool)));
        Assert.Null(UpdateRowCommand.For(Row("marrow", UpdateItemKind.Marrow)));
        Assert.Null(UpdateRowCommand.For(Row("dev:git", UpdateItemKind.DevTool)));
    }

    [Fact]
    [Trait("Feature", "UE-4")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pills_failure_banner_names_the_command_to_run_by_hand_when_the_item_has_one()
    {
        var failed = new UpdateRow("cli:codex", "Codex", UpdateItemKind.ProviderCli, UpdateRowState.Failed, "0.157.1", "0.160.0", null, null, false);
        var state = new UpdatePillProjector().Project([failed], busy: false);
        Assert.Equal("Codex is still 0.157.1 after its updater ran. Retry, or run codex update in a terminal.", state.BannerText);
        var other = failed with { Id = "dev:git", Title = "Git", Kind = UpdateItemKind.DevTool };
        Assert.Equal("Git is still 0.157.1 after its updater ran. Retry.", new UpdatePillProjector().Project([other], busy: false).BannerText);
    }
}
