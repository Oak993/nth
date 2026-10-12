// PORT-ONLY: Tactus (ED-NEW-pcctl-29, ED-NEW-pcctl-32; DESIGN.md 4.7, 10.10). The keys of the pill, the chat mirror and the
// Settings page in nth's settings store: their defaults when nothing is saved (centre, the latest step's monitor, answers
// in the pill, resume continues the chat, no sound, the workspace on "auto"), every value read back as written, a monitor
// name removed when none is chosen, and an unknown anchor or workspace choice read as its default.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class PcSettingsStorePillKeysTests
{
    [Fact]
    public void Nothing_saved_reads_as_the_defaults()
    {
        var settings = PcSettingsStore.Read(new InMemorySettingsStore());

        Assert.Equal("centre", settings.PillAnchor);
        Assert.Equal("latest", settings.PillMonitor);
        Assert.Null(settings.PillMonitorName);
        Assert.True(settings.AnswerInPill);
        Assert.True(settings.ResumeContinuesChat);
        Assert.False(settings.Sound);
        Assert.Equal("auto", settings.WorkspaceUse);
    }

    [Fact]
    public void Every_key_reads_back_as_written_under_its_design_name()
    {
        var store = new InMemorySettingsStore();
        var written = PcSettings.Default with
        {
            PillAnchor = "left",
            PillMonitor = "\\\\?\\DISPLAY#DEL4321#5&1a2b",
            PillMonitorName = "DELL U2723QE",
            AnswerInPill = false,
            ResumeContinuesChat = false,
            Sound = true,
            WorkspaceUse = "never",
        };

        PcSettingsStore.Write(store, written);
        var read = PcSettingsStore.Read(store);

        Assert.Equal("left", store.Object("pcControl.pill.anchor"));
        Assert.Equal(written.PillMonitor, store.Object("pcControl.pill.monitor"));
        Assert.Equal("DELL U2723QE", store.Object("pcControl.pill.monitorName"));
        Assert.False(Assert.IsType<bool>(store.Object("pcControl.pill.answerInPill")));
        Assert.False(Assert.IsType<bool>(store.Object("pcControl.resumeContinuesChat")));
        Assert.True(Assert.IsType<bool>(store.Object("pcControl.sound")));
        Assert.Equal("never", store.Object("pcControl.workspace.use"));
        Assert.Equal((written.PillAnchor, written.PillMonitor, written.PillMonitorName, written.AnswerInPill, written.ResumeContinuesChat, written.Sound, written.WorkspaceUse),
            (read.PillAnchor, read.PillMonitor, read.PillMonitorName, read.AnswerInPill, read.ResumeContinuesChat, read.Sound, read.WorkspaceUse));
    }

    [Fact]
    public void No_chosen_monitor_removes_its_name_and_unknown_choices_read_as_the_defaults()
    {
        var store = new InMemorySettingsStore();
        PcSettingsStore.Write(store, PcSettings.Default with { PillMonitor = "\\\\?\\DISPLAY#A", PillMonitorName = "A" });
        PcSettingsStore.Write(store, PcSettings.Default);
        Assert.Null(store.Object("pcControl.pill.monitorName"));

        store.Set("pcControl.pill.anchor", "middle");
        store.Set("pcControl.workspace.use", 3);
        store.Set("pcControl.pill.monitor", "");
        var read = PcSettingsStore.Read(store);

        Assert.Equal("centre", read.PillAnchor);
        Assert.Equal("auto", read.WorkspaceUse);
        Assert.Equal("latest", read.PillMonitor);
    }
}
