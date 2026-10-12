// PORT-ONLY: Tactus, a step's other forms in the pill's words (ED-NEW-pcctl-38; DESIGN.md 2.7, 4.5 "Hands off", "Done",
// "Borrow chip"): what Tactus asks to do ("Tactus needs the mouse for one step: click Save in Paint."), what it did
// ("Clicked Save in Paint" under "Done, all yours") and what a borrow was for ("Used the mouse for one click in Paint"),
// each naming its element by the same rule as the busy words; the app is the pill's to add.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class PcStepWordsTests
{
    private static readonly PcWindowInfo Paint = new(0x1a, "Untitled - Paint", "MSPaintApp", 101, "c:\\windows\\mspaint.exe");

    private static PcInputRequest Request(PcInputKind kind, string? element = null, string? keys = null, int count = 1) =>
        new(kind, Paint) { Element = element is null ? null : new PcUiNode("1.1a.3", "button", element), KeysText = keys, Count = count };

    [Theory]
    [InlineData(PcInputKind.Click, "Save", null, 1, "click Save", "clicked Save", "one click")]
    [InlineData(PcInputKind.Click, null, null, 1, "click", "clicked", "one click")]
    [InlineData(PcInputKind.Click, "Image", null, 2, "double-click Image", "double-clicked Image", "a double-click")]
    [InlineData(PcInputKind.Type, "Subject", null, 1, "type in Subject", "typed in Subject", "typing")]
    [InlineData(PcInputKind.Type, null, null, 1, "type", "typed", "typing")]
    [InlineData(PcInputKind.Key, null, "Ctrl+Shift+V", 1, "press Ctrl+Shift+V", "pressed Ctrl+Shift+V", "Ctrl+Shift+V")]
    [InlineData(PcInputKind.Key, null, null, 1, "press keys", "pressed keys", "keys")]
    [InlineData(PcInputKind.Scroll, "Canvas", null, 1, "scroll", "scrolled", "a scroll")]
    [InlineData(PcInputKind.Drag, "Canvas", null, 1, "drag", "dragged", "a drag")]
    [InlineData(PcInputKind.Command, "Bold", null, 1, "use Bold", "used Bold", "Bold")]
    [InlineData(PcInputKind.Command, null, null, 1, "do one step", "did one step", "one step")]
    public void Each_kind_of_step_says_what_Tactus_asks_to_do_what_it_did_and_what_it_borrowed_for(PcInputKind kind, string? element, string? keys, int count,
        string @do, string did, string chip) =>
        Assert.Equal(new PcStepWords(@do, did, chip), PcBusyWords.Words(Request(kind, element, keys, count)));

    [Fact]
    public void A_long_name_is_cut_as_in_the_busy_words_and_long_keys_read_as_keys()
    {
        var cut = new string('a', PcBusyWords.MaxName - 1) + "\u2026";
        Assert.Equal("click " + cut, PcBusyWords.Words(Request(PcInputKind.Click, new string('a', 40))).Do);
        Assert.Equal("press keys", PcBusyWords.Words(Request(PcInputKind.Key, keys: new string('x', PcBusyWords.MaxName + 1))).Do);
    }
}
