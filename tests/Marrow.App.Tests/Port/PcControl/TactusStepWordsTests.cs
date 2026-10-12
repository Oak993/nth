// PORT-ONLY: Tactus (ED-NEW-pcctl-28, ED-NEW-pcctl-32; DESIGN.md 4.5, 4.6). The pill's line for a step a chat's audit log
// wrote: the timeline's words without the app (the pill's title names it), the typed text as a count, an unconfirmed step
// said so, a step that waits for the user and one that failed in the timeline's own words.
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class TactusStepWordsTests
{
    [Theory]
    [InlineData("pc_click", "Save", "ok", "Clicked Save")]
    [InlineData("pc_click", null, "ok", "Clicked")]
    [InlineData("pc_set_value", "Amount", "unverified", "Set Amount (not confirmed)")]
    [InlineData("pc_click", "Send", "waiting_for_user", "Waiting for you: click Send")]
    [InlineData("pc_click", "Save", "element_gone", "Could not click Save")]
    [InlineData("pc_list_apps", null, "ok", "Listed apps")]
    public void A_step_reads_in_the_timeline_s_words_without_its_app(string tool, string? element, string result, string expected) =>
        Assert.Equal(expected, TactusToolWords.Step(new PcAuditEntry(DateTimeOffset.UnixEpoch, tool, "notepad.exe", "Untitled", element, "button", result, 5)));

    [Fact]
    public void Typed_text_is_a_count_into_its_field()
    {
        var entry = new PcAuditEntry(DateTimeOffset.UnixEpoch, PcToolSchemas.Type, "outlook.exe", "Inbox", "Subject", "edit", "ok", 5) { TextLength = 1 };

        Assert.Equal("Typed 1 character into Subject", TactusToolWords.Step(entry));
        Assert.Equal("Typed 0 characters", TactusToolWords.Step(entry with { Element = null, TextLength = null }));
        Assert.Equal("Could not type 12 characters into Subject", TactusToolWords.Step(entry with { TextLength = 12, Result = "denied_app" }));
    }
}
