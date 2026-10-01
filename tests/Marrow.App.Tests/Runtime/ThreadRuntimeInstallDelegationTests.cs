using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// The tail of install(_:) (App/Runtime/ThreadRuntime.swift:622-630, B3): the document the runtime reads is installed, and
// what the Hydra delegation held is settled against whether a turn is current.
public sealed class ThreadRuntimeInstallDelegationTests
{
    private static void Install(ThreadRuntimeDelegationRig rig, ThreadDocument document) =>
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "Install", document));

    private static ValueSet<string> Touched(ThreadRuntimeDelegationRig rig) => rig.OnMain(() => rig.Runtime.HydraCurrentTurnTouchedPaths);

    [Fact]
    [Trait("Req", "PR-RUNTIME-254")] // :622-624 with no current turn the delegation stream and its parser end and the touched paths are emptied
    [Trait("Req", "PR-RUNTIME-261")] // :623 an install with no current turn clears hydraStream and delegationParser
    public void Installing_a_document_while_idle_ends_the_stream_and_empties_the_touched_paths()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        rig.SetStream(new DelegationStreamState(Guid.NewGuid(), "reply-1", Guid.NewGuid(), 2));
        rig.SetParser(new HydraDelegationStreamParser(Guid.NewGuid(), "reply-1"));
        rig.OnMain(() => typeof(ThreadRuntime).GetProperty("HydraCurrentTurnTouchedPaths")!
            .SetValue(rig.Runtime, new ValueSet<string>(["left/over.txt"], SwiftStringComparer.Instance)));
        Assert.NotEmpty(Touched(rig));

        Install(rig, new ThreadDocument(rig.Lead.Id));

        Assert.Null(rig.Stream);
        Assert.Null(rig.Parser);
        Assert.Empty(Touched(rig));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-254")] // :625-630 with a turn in flight the edits of its tool rows are recorded, relative to the root, and the set is not emptied
    public void Installing_a_document_during_a_turn_records_the_edits_of_that_turns_tool_rows()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.OnMain(() =>
        {
            rig.Runtime.RehearseTurn("Work.", touchedPaths: ["already/there.txt"]);
            return rig.Runtime.Turns.Last().Id;
        });
        var stream = new DelegationStreamState(turn, "reply-1", Guid.NewGuid(), 1);
        rig.SetStream(stream);
        var root = rig.Project.Path;
        var document = new ThreadDocument(rig.Lead.Id)
        {
            Items = new ValueList<TimelineItem>(
            [
                Tool("tool-1", turn, Path.Combine(root, "src", "a.cs")),
                Tool("tool-2", turn, Path.Combine(Path.GetTempPath(), "outside-the-root", "b.txt")), // outside the working root: dropped
                Tool("tool-3", Guid.NewGuid(), Path.Combine(root, "other", "c.cs")), // another turn's row: not read
            ]),
        };

        Install(rig, document);

        Assert.Equal(new[] { "already/there.txt", "src/a.cs" }.Order(), Touched(rig).Order());
        Assert.Equal(stream, rig.Stream); // with a turn current the stream is not ended
    }

    private static TimelineItem Tool(string id, Guid turn, string path) =>
        new(turn, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit,
            Title = "Edit",
            Edits = new ValueList<FileEdit>([new FileEdit(path, "a\n", "a\nb\n")]),
        }), id);
}
