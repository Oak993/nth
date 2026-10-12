// PORT-ONLY: Tactus, the step a pause waits for in the pill's words (ED-NEW-pcctl-37; DESIGN.md 10.1 "Pausing after this
// step: dragging in Paint"; canvas pc-control-ui unit labels). What the step does, and to which element when it has a name;
// a long name is cut with an ellipsis; the app is the pill's to add.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class PcBusyWordsTests
{
    private static readonly PcWindowInfo Paint = new(0x1a, "Untitled - Paint", "MSPaintApp", 101, "c:\\windows\\mspaint.exe");

    private static PcInputRequest Request(PcInputKind kind, string? element = null, string? keys = null) =>
        new(kind, Paint) { Element = element is null ? null : new PcUiNode("1.1a.3", "button", element), KeysText = keys };

    [Theory]
    [InlineData(PcInputKind.Click, null, null, "clicking")]
    [InlineData(PcInputKind.Click, "Save", null, "clicking Save")]
    [InlineData(PcInputKind.Type, null, null, "typing")]
    [InlineData(PcInputKind.Type, "Subject", null, "typing in Subject")]
    [InlineData(PcInputKind.Key, null, "Ctrl+S", "pressing Ctrl+S")]
    [InlineData(PcInputKind.Key, null, null, "pressing keys")]
    [InlineData(PcInputKind.Scroll, "Canvas", null, "scrolling")]
    [InlineData(PcInputKind.Drag, "Canvas", null, "dragging")]
    [InlineData(PcInputKind.Command, "Bold", null, "using Bold")]
    [InlineData(PcInputKind.Command, "  ", null, "working")]
    public void Each_kind_of_step_says_what_it_does_and_names_its_element(PcInputKind kind, string? element, string? keys, string words) =>
        Assert.Equal(words, PcBusyWords.Of(Request(kind, element, keys)));

    [Fact]
    public void A_long_name_is_cut_with_an_ellipsis_and_long_keys_read_as_keys()
    {
        var name = new string('a', 40);
        var words = PcBusyWords.Of(Request(PcInputKind.Click, name));
        Assert.Equal("clicking " + new string('a', PcBusyWords.MaxName - 1) + "\u2026", words);
        Assert.Equal("clicking Two lines", PcBusyWords.Of(Request(PcInputKind.Click, "Two\nlines")));
        Assert.Equal("pressing keys", PcBusyWords.Of(Request(PcInputKind.Key, keys: new string('x', PcBusyWords.MaxName + 1))));
    }
}
