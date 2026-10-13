using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Ctrl+Alt+Enter anywhere in Windows (configurable) ... validated by Tactus's ChordValidator. It is registered while nth runs and
// released on exit, and a failed registration shows in Settings with Change"; 16 "done when": the chord is registered, validated and released): the talk chord on a
// fake chord thread. Nothing is registered with Windows.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-9")]
public sealed class VoiceChordTests
{
    private static readonly PcHotkey TalkDefault = new(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, PcHotkey.Return);

    [Fact]
    public void Ctrl_Alt_Enter_is_registered_by_default_and_a_press_of_it_is_told()
    {
        var host = new FakeStopChordHost();
        using var chord = new VoiceChord(host, () => null);
        var pressed = 0;
        chord.Pressed += () => pressed++;

        Assert.True(chord.Register());

        Assert.Equal(TalkDefault, host.Registered);
        Assert.Equal(["register Ctrl+Alt+Enter"], host.Calls);
        Assert.True(chord.Status.Works);
        Assert.Null(chord.Status.Line);
        Assert.Equal("Ctrl+Alt+Enter", chord.Status.Chord);
        host.Press(VoiceChord.ChordId);
        host.Press(GlobalStop.StopId); // the stop chord's id is not this chord
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void The_chord_is_released_when_the_voice_ends_and_its_thread_goes_with_it()
    {
        var host = new FakeStopChordHost();
        var chord = new VoiceChord(host, () => null);
        chord.Register();

        chord.Dispose();

        Assert.Equal(["register Ctrl+Alt+Enter", "unregister"], host.Calls);
        Assert.Null(host.Registered);
        chord.Dispose(); // again: nothing more
        Assert.Equal(2, host.Calls.Count);
        Assert.False(chord.Register()); // a released chord never registers again
    }

    [Fact]
    public void A_changed_setting_takes_the_new_chord_and_lets_the_old_one_go()
    {
        var host = new FakeStopChordHost();
        string? setting = null;
        using var chord = new VoiceChord(host, () => setting);
        chord.Register();

        setting = "Ctrl+Alt+Home";
        Assert.True(chord.Register());

        Assert.Equal(["register Ctrl+Alt+Enter", "unregister", "register Ctrl+Alt+Home"], host.Calls);
        Assert.Equal("Ctrl+Alt+Home", chord.Status.Chord);
    }

    [Fact]
    public void A_chord_another_app_holds_is_not_registered_and_the_status_says_so_with_the_way_out()
    {
        var host = new FakeStopChordHost();
        host.Taken.Add(TalkDefault);
        using var chord = new VoiceChord(host, () => null);
        var told = new List<VoiceChordStatus>();
        chord.StatusChanged += told.Add;

        Assert.False(chord.Register());

        Assert.True(chord.Status.Taken);
        Assert.False(chord.Status.Works);
        Assert.Equal("Ctrl+Alt+Enter is taken by another app. Change it.", chord.Status.Line);
        Assert.Single(told);
        Assert.Null(host.Registered);
    }

    [Theory]
    [InlineData("Enter", PcChordProblem.SingleKey)]
    [InlineData("Win+Enter", PcChordProblem.WinKey)]
    [InlineData("Ctrl+Alt+Esc", PcChordProblem.Clash)]
    [InlineData("Ctrl+Alt+Space", PcChordProblem.Clash)]
    public void A_chord_that_fails_the_validator_is_never_registered(string text, PcChordProblem problem)
    {
        var host = new FakeStopChordHost();
        using var chord = new VoiceChord(host, () => text, () => [PcHotkey.StopDefault, PcHotkey.PauseDefault]);

        Assert.False(chord.Register());

        Assert.Equal(problem, chord.Status.Problem);
        Assert.NotNull(chord.Status.Line);
        Assert.Contains("Change it.", chord.Status.Line, StringComparison.Ordinal);
        Assert.Null(host.Registered);
        Assert.DoesNotContain(host.Calls, call => call.StartsWith("register", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chord_that_types_a_character_on_an_installed_layout_is_refused()
    {
        var host = new FakeStopChordHost();
        host.Types.Add(TalkDefault);
        using var chord = new VoiceChord(host, () => null);

        Assert.False(chord.Register());

        Assert.Equal(PcChordProblem.TypesCharacter, chord.Status.Problem);
        Assert.Equal("Ctrl+Alt+Enter types a character on one of your keyboard layouts. Change it.", chord.Status.Line);
    }

    [Fact]
    public void Text_that_is_not_a_chord_means_the_default()
    {
        var host = new FakeStopChordHost();
        using var chord = new VoiceChord(host, () => "banana");

        Assert.True(chord.Register());

        Assert.Equal(TalkDefault, host.Registered);
    }
}
