using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 15.3; ED-NEW-dextra-as19-1): how a recording is written and read back. Event, narration and frame lines are
// compact JSON; the manifest and the draft are versioned documents read strictly. A field this build does not know survives a load and
// a save; a file from a newer build is read, flagged and never written back; a line or a document that does not fit is refused, not guessed.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-1")]
public sealed class TeachJsonTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private static TeachRecordingInfo Info() => new("t0123456789ab", "dextra", Started, TeachRecordingState.Recorded)
    {
        Elapsed = TimeSpan.FromSeconds(83.5), Events = 12, Frames = 40, FrameBytes = 4_000_000, Narration = 3, Changed = Started.AddMinutes(2),
        Ended = TeachStopReason.Owner, Keep = true, SkillName = "send-report", SkillId = "abc",
    };

    [Fact]
    public void An_event_with_everything_on_it_reads_back_the_same()
    {
        var e = new TeachEvent(7, TimeSpan.FromSeconds(12.345), TeachEventKind.Type)
        {
            Verb = "type", Element = new TeachElement("outlook", "New message", "edit", "To") { IsPassword = true }, Text = "[secret]", Length = 9, Secret = true,
            Url = "https://example.com/a", Frame = "frames/0007.jpg",
        };
        Assert.Equal(e, TeachJson.ReadEvent(TeachJson.EventLine(e)));
        var plain = new TeachEvent(1, TimeSpan.Zero, TeachEventKind.Shortcut) { Verb = "press", Text = "Ctrl+S" };
        Assert.Equal(plain, TeachJson.ReadEvent(TeachJson.EventLine(plain)));
    }

    [Fact]
    public void An_event_line_is_one_line_whatever_the_text_holds()
    {
        var e = new TeachEvent(1, TimeSpan.Zero, TeachEventKind.Type) { Verb = "type", Text = "a\nb\r\nc\u2028d\"e\\f", Length = 9 };
        var line = TeachJson.EventLine(e);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal(e.Text, TeachJson.ReadEvent(line)!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"seq\":1}")]
    [InlineData("{\"seq\":-1,\"at\":1,\"kind\":\"Click\"}")]
    [InlineData("{\"seq\":1,\"at\":-1,\"kind\":\"Click\"}")]
    [InlineData("{\"seq\":1,\"at\":1,\"kind\":\"Explode\"}")]
    [InlineData("{\"seq\":1,\"at\":1,\"kind\":\"Click\",\"kind\":\"Type\"}")]
    [InlineData("{\"seq\":\"1\",\"at\":1,\"kind\":\"Click\"}")]
    public void A_line_that_does_not_fit_is_refused(string line) => Assert.Null(TeachJson.ReadEvent(line));

    [Fact]
    public void A_frame_name_in_an_event_that_is_not_a_frame_name_is_dropped()
    {
        var line = "{\"seq\":1,\"at\":1,\"kind\":\"Click\",\"verb\":\"click\",\"frame\":\"..\\\\..\\\\win.ini\"}";
        Assert.Null(TeachJson.ReadEvent(line)!.Frame);
    }

    [Fact]
    public void A_narration_line_reads_back_and_a_bad_one_is_refused()
    {
        var line = new TeachNarrationLine(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(6), "First a new message.");
        Assert.Equal(line, TeachJson.ReadNarration(TeachJson.NarrationLine(line)));
        Assert.Null(TeachJson.ReadNarration("{\"start\":5,\"end\":2,\"text\":\"x\"}"));
        Assert.Null(TeachJson.ReadNarration("{\"start\":1,\"end\":2,\"text\":\"\"}"));
    }

    [Fact]
    public void The_manifest_reads_back_the_same()
    {
        var info = Info();
        var read = TeachJson.ReadInfo(TeachJson.InfoDocument(info));
        Assert.Equal(info, read);
        Assert.False(read!.Newer);
        Assert.Equal(Started.AddMinutes(2) + TeachLimits.Retention, TeachJson.ReadInfo(TeachJson.InfoDocument(info with { Keep = false, SkillName = null, SkillId = null }))!.ExpiresAt);
        Assert.Null((Info() with { State = TeachRecordingState.Saved }).ExpiresAt);
    }

    [Fact]
    public void A_field_this_build_does_not_know_survives_a_load_and_a_save()
    {
        var document = TeachJson.InfoDocument(Info());
        document["colour"] = "teal";
        document["nested"] = new JsonObject { ["a"] = new JsonArray(1, 2, 3) };
        var read = TeachJson.ReadInfo(JsonNode.Parse(document.ToJsonString()));
        Assert.Equal(["colour", "nested"], read!.Unknown.Keys.Order());
        var again = TeachJson.InfoDocument(read);
        Assert.Equal("teal", again["colour"]!.GetValue<string>());
        Assert.Equal(3, again["nested"]!["a"]!.AsArray().Count);
    }

    [Fact]
    public void A_manifest_from_a_newer_build_is_flagged()
    {
        var document = TeachJson.InfoDocument(Info());
        document["version"] = TeachRecordingInfo.CurrentVersion + 1;
        var read = TeachJson.ReadInfo(document);
        Assert.True(read!.Newer);
        Assert.Equal(TeachRecordingInfo.CurrentVersion + 1, TeachJson.InfoDocument(read)["version"]!.GetValue<int>());
    }

    [Fact]
    public void A_manifest_that_does_not_fit_is_refused()
    {
        foreach (var (name, value) in new (string, JsonNode?)[] { ("id", "../x"), ("assistant", "A B"), ("started", "yesterday"), ("state", "Exploded"), ("ended", "Never") })
        {
            var document = TeachJson.InfoDocument(Info());
            document[name] = value;
            Assert.Null(TeachJson.ReadInfo(document));
        }
        Assert.Null(TeachJson.ReadInfo(null));
        Assert.Null(TeachJson.ReadInfo(new JsonArray()));
    }

    private static TeachDraft Draft() => TeachDraftModel.Answer(
        TeachDraftBuilder.Skeleton(Info(), [
            new TeachEvent(1, TimeSpan.FromSeconds(2), TeachEventKind.Click) { Verb = "click", Element = new TeachElement("outlook", "Inbox", "button", "Send"), Frame = "frames/0001.jpg" },
            new TeachEvent(2, TimeSpan.FromSeconds(5), TeachEventKind.Type) { Verb = "type", Element = new TeachElement("outlook", "New", "edit", "To"), Text = "sam@example.com", Length = 15 },
        ], [new TeachNarrationLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Always check it first.")]),
        "typed-2", "It changes: make it an input");

    [Fact]
    public void A_draft_with_steps_inputs_decisions_obstacles_and_questions_reads_back_the_same()
    {
        var draft = Draft() with { Obstacles = new ValueList<TeachObstacle>([new TeachObstacle("The dialog hides", "Alt+Tab")]), ModelWritten = true };
        var read = TeachJson.ReadDraft(JsonNode.Parse(TeachJson.DraftDocument(draft).ToJsonString()));
        Assert.Equal(draft, read);
        Assert.NotNull(read!.Steps[0].Guard);
        Assert.Single(read.Parameters);
        Assert.Single(read.Decisions);
    }

    [Fact]
    public void A_draft_keeps_what_this_build_does_not_know_and_a_newer_draft_is_flagged()
    {
        var document = TeachJson.DraftDocument(Draft());
        document["future"] = new JsonObject { ["x"] = 1 };
        var read = TeachJson.ReadDraft(JsonNode.Parse(document.ToJsonString()));
        Assert.Equal(["future"], read!.Unknown.Keys);
        Assert.Equal(1, TeachJson.DraftDocument(read)["future"]!["x"]!.GetValue<int>());
        document["version"] = TeachDraft.CurrentVersion + 1;
        Assert.True(TeachJson.ReadDraft(document)!.Version > TeachDraft.CurrentVersion);
    }

    [Fact]
    public void A_draft_that_does_not_fit_is_refused_whole()
    {
        var steps = TeachJson.DraftDocument(Draft());
        steps["steps"]![0]!["kind"] = "Explode";
        Assert.Null(TeachJson.ReadDraft(steps));
        var guard = TeachJson.DraftDocument(Draft());
        guard["steps"]![0]!["guard"]!["class"] = "Everything";
        Assert.Null(TeachJson.ReadDraft(guard));
        var number = TeachJson.DraftDocument(Draft());
        number["steps"]![0]!["n"] = 0;
        Assert.Null(TeachJson.ReadDraft(number));
        var recording = TeachJson.DraftDocument(Draft());
        recording["recording"] = "../x";
        Assert.Null(TeachJson.ReadDraft(recording));
    }
}
