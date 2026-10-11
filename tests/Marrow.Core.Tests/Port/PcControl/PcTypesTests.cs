using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.1 references; OD-0140 defaults (off, confirmations on); ED-NEW-pcctl-2
// (legacy Marrow pc-control.test.ts:207-250): screenshots are not kept in the history after the turn unless the user says so.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-2")]
public sealed class PcTypesTests
{
    [Fact]
    public void Window_refs_are_w_and_lower_hex_and_parse_back()
    {
        Assert.Equal("w:3a0f12", PcWindowRef.Format(0x3a0f12));
        Assert.True(PcWindowRef.TryParse("w:3A0F12", out var handle));
        Assert.Equal((nint)0x3a0f12, handle);
        foreach (var bad in new[] { null, "", "w:", "3a0f12", "w:zz", "w:0", "w:-1", "x:12", "w:12345678901234567" })
            Assert.False(PcWindowRef.TryParse(bad, out _), bad);
    }

    [Fact]
    public void Observation_ids_are_o_and_a_positive_number()
    {
        Assert.Equal("o:41", PcObsId.Format(41));
        Assert.True(PcObsId.TryParse("o:41", out var n));
        Assert.Equal(41, n);
        foreach (var bad in new[] { null, "o:", "o:0", "o:-3", "o:4a", "41", "o: 4" })
            Assert.False(PcObsId.TryParse(bad, out _), bad);
    }

    [Fact]
    public void Settings_default_to_off_with_confirmations_on_and_screenshots_not_kept()
    {
        var settings = PcSettings.Default;
        Assert.False(settings.Enabled);
        Assert.True(settings.ConfirmGuardedActions);
        Assert.True(settings.ShowClickRing);
        Assert.False(settings.KeepScreenshots);
        Assert.False(settings.LogTypedText);
        Assert.False(settings.ReadTextFromImages);
        Assert.Equal(1280, settings.LongEdge);
        Assert.Equal(1600, (settings with { Detail = PcDetail.High }).LongEdge);
    }

    [Fact]
    public void App_keys_prefer_the_aumid_and_lower_case_the_path()
    {
        Assert.Equal("exe:c:\\windows\\notepad.exe", new PcAppIdentity(@"C:\Windows\Notepad.exe").Key);
        Assert.Equal("aumid:microsoft.paint_8wekyb3d8bbwe!app", new PcAppIdentity(@"C:\x\p.exe", "Microsoft.Paint_8wekyb3d8bbwe!App").Key);
        Assert.Equal("notepad.exe", new PcAppIdentity(@"C:\Windows\Notepad.exe").ExeName);
    }

    [Fact]
    public void Rects_contain_their_top_left_but_not_their_far_edges()
    {
        var rect = new PcRect(100, 100, 800, 600);
        Assert.True(rect.Contains(new PcPoint(100, 100)));
        Assert.True(rect.Contains(new PcPoint(899, 699)));
        Assert.False(rect.Contains(new PcPoint(900, 300)));
        Assert.False(rect.Contains(new PcPoint(99, 300)));
        Assert.Equal(new PcPoint(500, 400), rect.Center);
        Assert.Equal(default, PcRect.Union([]));
    }
}
