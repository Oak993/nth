using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.11 "corrupted JSON: the thread opens with the default layout, the bad file is kept ..." (web surfaces W3c;
// ED-NEW-web-w3c-1). A saved workspace is read in full or reported corrupted; reading never throws, whatever the text.
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class StoredWorkspaceDamageTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"v\":1")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"layout\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void Text_that_is_not_a_workspace_object_is_corrupted(string text)
    {
        var read = PaneWorkspaceJson.Read(text);
        Assert.Equal(WorkspaceReadOutcome.Corrupted, read.Outcome);
        Assert.Null(read.Stored);
        Assert.False(string.IsNullOrEmpty(read.Reason));
    }

    [Theory]
    [InlineData("{\"v\":\"one\"}")]
    [InlineData("{\"v\":0}")]
    [InlineData("{\"v\":1.5}")]
    [InlineData("{\"v\":-3}")]
    [InlineData("{\"v\":99999999999}")]
    [InlineData("{\"order\":5}")]
    [InlineData("{\"order\":{\"a\":1}}")]
    [InlineData("{\"order\":[5]}")]
    [InlineData("{\"order\":[[5]]}")]
    [InlineData("{\"order\":[[\"tasks\",null]]}")]
    [InlineData("{\"order\":[{\"tasks\":1}]}")]
    [InlineData("{\"side\":\"yes\"}")]
    [InlineData("{\"drawer\":1}")]
    [InlineData("{\"termGrid\":null}")]
    [InlineData("{\"ratios\":[0.5]}")]
    [InlineData("{\"ratios\":{\"chat\":\"half\"}}")]
    [InlineData("{\"ratios\":{\"chat\":null}}")]
    [InlineData("{\"ratios\":{\"chat\":1e999}}")]
    [InlineData("{\"pre\":{\"side\":true}}")]
    [InlineData("{\"collapsed\":\"side\"}")]
    [InlineData("{\"collapsed\":[1]}")]
    [InlineData("{\"expanded\":3}")]
    [InlineData("{\"expanded\":[\"browser\"]}")]
    [InlineData("{\"recent\":\"tasks\"}")]
    [InlineData("{\"recent\":[7]}")]
    [InlineData("{\"narrow\":false}")]
    [InlineData("{\"panes\":[]}")]
    [InlineData("{\"panes\":\"browser\"}")]
    public void A_known_field_of_the_wrong_kind_makes_the_whole_workspace_corrupted(string text)
    {
        var read = PaneWorkspaceJson.Read(text);
        Assert.Equal(WorkspaceReadOutcome.Corrupted, read.Outcome);
        Assert.Null(read.Stored);
    }

    [Theory]
    [InlineData("{\"expanded\":null}")]
    [InlineData("{\"narrow\":null}")]
    [InlineData("{\"order\":[]}")]
    [InlineData("{\"ratios\":{}}")]
    [InlineData("{\"panes\":{}}")]
    [InlineData("{\"panes\":{\"browser\":null,\"x\":[1,{\"a\":2}]}}")]
    [InlineData("{\"unknown\":[1,2,3],\"v\":1}")]
    [InlineData("{\"ratios\":{\"somethingNew\":0.5},\"collapsed\":[\"somethingNew\"]}")]
    [InlineData("{\"expanded\":\"notAPane\",\"narrow\":\"notAPane\"}")]
    public void Odd_but_readable_text_is_not_corrupted(string text)
    {
        var read = PaneWorkspaceJson.Read(text);
        Assert.NotEqual(WorkspaceReadOutcome.Corrupted, read.Outcome);
        Assert.NotNull(read.Stored);
    }

    [Fact]
    public void Reading_a_workspace_cut_off_anywhere_never_throws()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.Browser).Open(PaneKind.BackgroundTasks).Open(PaneKind.SideChat).WithRatio("chat", 0.4);
        var text = PaneWorkspaceJson.Write(new StoredWorkspace { State = state }.WithPaneData("browser", "{\"tabs\":[{\"url\":\"https://a.test/\"}]}"));
        var corrupted = 0;
        for (var length = 0; length <= text.Length; length++)
        {
            var read = PaneWorkspaceJson.Read(text[..length]);
            if (read.Outcome == WorkspaceReadOutcome.Corrupted) corrupted++;
            else Assert.NotNull(read.Stored);
        }
        // Only the whole text (and nothing shorter that ends a complete object) reads.
        Assert.Equal(text.Length, corrupted);
        Assert.NotEqual(WorkspaceReadOutcome.Corrupted, PaneWorkspaceJson.Read(text).Outcome);
    }

    [Fact]
    public void Reading_a_workspace_with_any_one_byte_changed_never_throws()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.Browser).Open(PaneKind.Artifacts).WithRatio("cols", 0.55).Collapse("side", 0.3);
        var text = PaneWorkspaceJson.Write(new StoredWorkspace { State = state }.WithPaneData("artifact", "{\"id\":\"a1\",\"version\":3}"));
        var random = new Random(3_012);
        var glyphs = "{}[]\",:0-.eE9xnulltrue\\ é".ToCharArray();
        for (var i = 0; i < 4000; i++)
        {
            var chars = text.ToCharArray();
            for (var edits = random.Next(1, 4); edits > 0; edits--) chars[random.Next(chars.Length)] = glyphs[random.Next(glyphs.Length)];
            var read = PaneWorkspaceJson.Read(new string(chars));
            if (read.Outcome != WorkspaceReadOutcome.Corrupted)
            {
                // Whatever read is writable again, and reads the same: nothing half-read is saved over a good file.
                var stored = read.Stored!;
                var again = PaneWorkspaceJson.Read(PaneWorkspaceJson.Write(stored));
                Assert.NotEqual(WorkspaceReadOutcome.Corrupted, again.Outcome);
                Assert.Equal(stored.State, again.Stored!.State);
            }
        }
    }

    [Fact]
    public void Reading_an_element_is_reading_the_text()
    {
        const string text = "{\"v\":1,\"order\":[[\"tasks\"]],\"side\":true}";
        using var document = JsonDocument.Parse(text);
        Assert.Equal(PaneWorkspaceJson.Read(text), PaneWorkspaceJson.Read(document.RootElement));
        using var bad = JsonDocument.Parse("[1]");
        Assert.Equal(WorkspaceReadOutcome.Corrupted, PaneWorkspaceJson.Read(bad.RootElement).Outcome);
    }
}
