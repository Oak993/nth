using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class AsturOwnMemoryTests
{
    [Fact]
    public async Task Scoped_view_cannot_read_edit_keep_or_forget_another_assistants_note()
    {
        using var rig = new MemoryRig();
        var own = rig.Memory.Propose(MemoryRig.Clean("Invoices are checked on Mondays", NoteKind.Procedure, MemoryRig.Astur)).Note!;
        var other = rig.Memory.Propose(MemoryRig.Clean("Invoices stay in the main binder", NoteKind.Procedure, MemoryRig.Main)).Note!;
        using var model = new MemoryModel(rig.Memory, mainAssistant: () => MemoryRig.Astur, work: new InlineMemoryWork(), fixedScope: MemoryRig.Astur);
        Assert.Equal(1, model.Counts.Used);
        Assert.Equal(own.Id, Assert.Single(Assert.Single(Assert.Single(model.Groups).Topics).Notes).Id);
        model.BeginEdit(other.Id);
        model.ToggleSource(other.Id);
        await model.Delete(other.Id);
        await model.Keep(other.Id);
        Assert.Null(model.EditingId);
        Assert.Null(model.SourceOpenId);
        Assert.NotNull(rig.Memory.Find(other.Id));
        model.ForgetText = "Invoices";
        model.PreviewForget();
        Assert.Equal(own.Id, Assert.Single(model.ForgetPreview!).Id);
        await model.ConfirmForget();
        Assert.Null(rig.Memory.Find(own.Id));
        Assert.NotNull(rig.Memory.Find(other.Id));
    }
}
