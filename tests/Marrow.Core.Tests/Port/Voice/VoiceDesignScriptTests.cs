using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Light;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Voice;

[Trait("Feature", "CZ-7b")]
public sealed class VoiceDesignScriptTests
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/voice.json")));
    }

    [Fact]
    [Trait("Spec", "CZ-T75")]
    [Trait("Decision", "ED-292")] // CZ-T75: voice-composer.js SCRIPT/SYN rng(91), STREAM rng(7), synthAt.
    public void The_speaker_events_and_bands_equal_the_locked_design_arithmetic()
    {
        using var fixture = Fixture();
        var script = VoiceDesignScript.Default;
        Assert.Equal(fixture.RootElement.GetProperty("end").GetDouble(), script.End, 10);
        var events = fixture.RootElement.GetProperty("stream").EnumerateArray().ToArray();
        Assert.Equal(events.Length, script.Events.Count);
        for (var i = 0; i < events.Length; i++)
        {
            Assert.Equal(events[i].GetProperty("t").GetDouble(), script.Events[i].At, 10);
            Assert.Equal(events[i].GetProperty("i").GetInt32(), script.Events[i].Index);
            Assert.Equal(events[i].GetProperty("text").GetString(), script.Events[i].Text);
            Assert.Equal(events[i].GetProperty("kind").GetString() == "add", script.Events[i].Add);
        }
        foreach (var sample in fixture.RootElement.GetProperty("levels").EnumerateArray())
        {
            var bands = script.Level(sample.GetProperty("t").GetDouble());
            Assert.Equal(sample.GetProperty("all").GetDouble(), bands.All, 10);
            Assert.Equal(sample.GetProperty("low").GetDouble(), bands.Low, 10);
            Assert.Equal(sample.GetProperty("mid").GetDouble(), bands.Mid, 10);
            Assert.Equal(sample.GetProperty("high").GetDouble(), bands.High, 10);
        }
    }

    [Fact]
    [Trait("Spec", "CZ-T75")]
    [Trait("Decision", "ED-292")] // CZ-T75: events arrive at STREAM's times, the final pass fixes too -> too.
    public void The_real_session_replays_each_revision_and_finishes_after_the_speaker_plus_035()
    {
        using var runtime = new TestRuntime();
        var script = VoiceDesignScript.Default;
        var source = ScriptedVoiceSource.ForDesign(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en", new SpeechModel("preview", null)));
        var session = source.Session;
        var updates = new List<(double At, TranscriptUpdate Update)>();
        session.TranscriptChanged += update => updates.Add((runtime.Time.GetTimestamp() / (double)runtime.Time.TimestampFrequency, update));
        runtime.OnMain(session.Activate);
        foreach (var item in script.Events.Where(e => e.At <= script.End + .35))
        {
            runtime.Advance(TimeSpan.FromSeconds(item.At) - TimeSpan.FromSeconds(runtime.Time.GetTimestamp() / (double)runtime.Time.TimestampFrequency));
            Assert.Equal(item.Text, session.Words[item.Index].Text);
            Assert.Equal(item.At, updates[^1].At, 6);
        }
        runtime.Advance(TimeSpan.FromSeconds(script.End + .35) - TimeSpan.FromSeconds(runtime.Time.GetTimestamp() / (double)runtime.Time.TimestampFrequency));
        Assert.Equal(VoiceSessionState.Processing, session.State);
        Assert.Equal(VoiceLabelState.Finishing, session.Label);
        using var fixture = Fixture();
        var final = updates[^1].Update;
        Assert.Equal(TranscriptUpdateKind.Final, final.Kind);
        var planned = fixture.RootElement.GetProperty("final").EnumerateArray().ToArray();
        Assert.Equal(planned.Length, final.Ops.Count);
        for (var i = 0; i < planned.Length; i++)
        {
            Assert.Equal(planned[i].GetProperty("at").GetDouble(), final.Ops[i].At.TotalSeconds, 6);
            Assert.Equal(planned[i].GetProperty("text").GetString(), final.Ops[i].Op.Text);
            Assert.Equal(planned[i].GetProperty("i").GetInt32(), final.Ops[i].Op.Index);
        }
        runtime.Advance(TimeSpan.FromSeconds(fixture.RootElement.GetProperty("processing").GetDouble()));
        Assert.Equal(VoiceSessionState.Landing, session.State);
        Assert.Equal("Add a shortcut for the theme switch, and show it in the tooltip too.", string.Join(' ', session.Words.Select(w => w.Text)));
        runtime.AdvanceMilliseconds(500);
        Assert.Equal(VoiceSessionState.Rest, session.State);
        Assert.Equal(TimeSpan.Zero, session.BufferedAudio);
    }

    [Fact]
    [Trait("Spec", "CZ-T95")]
    [Trait("Decision", "ED-292")] // CZ-T95: the real render follower against voice-options.js follow(vis,target,dt,.2,.17).
    public void The_shared_light_follower_matches_the_design_down_and_up_samples()
    {
        using var fixture = Fixture();
        foreach (var option in fixture.RootElement.GetProperty("light").EnumerateArray())
        {
            var follower = new VoiceLightFollower();
            var target = option.GetProperty("target").GetDouble();
            double previous = 0;
            foreach (var sample in option.GetProperty("down").EnumerateArray())
            {
                var t = sample.GetProperty("t").GetDouble();
                Assert.Equal(sample.GetProperty("value").GetDouble(), follower.Step(target, t - previous, false), 10);
                previous = t;
            }
            follower.Jump(target);
            previous = 0;
            foreach (var sample in option.GetProperty("up").EnumerateArray())
            {
                var t = sample.GetProperty("t").GetDouble();
                Assert.Equal(sample.GetProperty("value").GetDouble(), follower.Step(1, t - previous, false), 10);
                previous = t;
            }
        }
    }
}
