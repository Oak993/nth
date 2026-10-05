using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Port.Voice;

// VT-14 (voice-SPEC V-10.6, V-16.3.2, V-16.3.3, V-9.5, V-T93, V-T95; OD-0099 Q-V-13 B, Q-V-1 C): what the Speech model section does, with no page in sight. The probe, the store, the downloads
// and the self-test are scripted doubles: no hardware is read, nothing is fetched, no model is loaded.
[Collection("Headless")]
public sealed class SpeechModelControllerTests(HeadlessSession headless)
{
    private const string Candidate = "large-v3-turbo-q8_0";

    private static ModelFile File(string id) => ModelCatalog.Find(id)!;

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-10.6: the probe's rows are shown as they complete, then the recommendation
    public void A_check_reports_each_row_as_the_probe_completes_then_recommends() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var probe = (GatedHardwareProbe)HardwareProbeSeamsCurrent();
        var changes = 0;
        scene.Controller.Changed += () => changes++;
        Assert.Equal(HardwareCardPhase.Idle, scene.Controller.Phase);
        _ = scene.Controller.Check();
        Assert.Equal(HardwareCardPhase.Running, scene.Controller.Phase);
        Assert.Empty(scene.Controller.Rows);
        for (var i = 1; i <= 4; i++)
        {
            probe.Release();
            scene.Until(() => scene.Controller.Rows.Count == i);
            Assert.Equal(HardwareCardPhase.Running, scene.Controller.Phase);
            Assert.Null(scene.Controller.Recommendation);
        }
        Assert.Equal(["Processor", "Memory", "Graphics", "GPU backend"], scene.Controller.Rows.Select(r => r.Name));
        Assert.Equal("Vulkan, working", scene.Controller.Rows[3].Text);
        probe.Release();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.Equal(Candidate, scene.Controller.Recommendation!.ModelId);
        Assert.Equal(probe.Profile, scene.Controller.Profile);
        Assert.True(changes >= 6, "every row and the end tell the page: " + changes);
    }, probe: GatedHardwareProbe.OwnersDesktop());

    private static Marrow.Services.Port.Voice.Probe.IHardwareProbe HardwareProbeSeamsCurrent() => Marrow.Services.Port.Voice.Probe.HardwareProbeSeams.Probe.Current;

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.4 and V-10.6: a finished check leaves its record, origin manual, and downloads nothing
    public void A_finished_check_stores_the_record_and_downloads_nothing() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        scene.Runtime.Time.Advance(TimeSpan.FromSeconds(15.4));
        var now = scene.Runtime.Time.GetUtcNow();
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Model = "small-q5_1" };
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        var record = scene.Model.Settings.VoiceHardware!;
        Assert.Equal(HardwareOrigin.Manual, record.Origin);
        Assert.Equal(Candidate, record.Candidate);
        Assert.Equal("small-q5_1", record.Model);
        Assert.Equal(VoiceBackend.Vulkan, record.Backend);
        Assert.Equal(HardwareKey.Compute(scene.Controller.Profile!, HardwareCheckIdentity.App, HardwareCheckIdentity.Runtime), record.Key);
        Assert.Equal(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero), record.At);
        Assert.Empty(backend.Started);
        Assert.False(scene.Controller.Decided);
    }, backend: new ScriptedSpeechModelBackend(), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-2.5: a probe that cannot run (the fail-closed default, a read that throws) leaves the card as it was, and nothing else happens
    public void A_probe_that_fails_leaves_the_card_idle_and_stores_nothing()
    {
        VoiceTypingPageRig.Run(headless, scene =>
        {
            _ = scene.Controller.Check();
            scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Idle);
            Assert.Empty(scene.Controller.Rows);
            Assert.Null(scene.Controller.Recommendation);
            Assert.Null(scene.Model.Settings.VoiceHardware);
        });
        VoiceTypingPageRig.Run(headless, scene =>
        {
            _ = scene.Controller.Check();
            scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Idle);
            Assert.Equal(4, scene.Controller.Rows.Count);
            Assert.Null(scene.Controller.Profile);
            Assert.Null(scene.Model.Settings.VoiceHardware);
        }, probe: new GatedHardwareProbe(GatedHardwareProbe.OwnersDesktop().Profile, [new Marrow.Services.Port.Voice.Probe.ProbeRow("Processor", "x"), new Marrow.Services.Port.Voice.Probe.ProbeRow("Memory", "y"),
            new Marrow.Services.Port.Voice.Probe.ProbeRow("Graphics", "z"), new Marrow.Services.Port.Voice.Probe.ProbeRow("GPU backend", "w")], gated: false, failure: new InvalidOperationException("a read failed")));
    }

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // DESIGN voice.js:67: a second check starts over; a check already running is not started twice
    public void A_second_check_starts_over_and_a_running_one_is_not_started_twice() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var probe = (GatedHardwareProbe)HardwareProbeSeamsCurrent();
        var first = scene.Controller.Check();
        var again = scene.Controller.Check();
        Assert.Same(first, again);
        probe.Release(5);
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.Equal(1, probe.Runs);
        _ = scene.Controller.Check();
        Assert.Equal(HardwareCardPhase.Running, scene.Controller.Phase);
        Assert.Empty(scene.Controller.Rows);
        Assert.Null(scene.Controller.Recommendation);
        probe.Release(5);
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.Equal(2, probe.Runs);
    }, probe: GatedHardwareProbe.OwnersDesktop());

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the recommendation does not use the language when the processor decides it for an English-only forced language
    public void The_recommendation_follows_the_forced_language() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Lang = "en" };
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.Equal("small.en-q8_0", scene.Controller.Recommendation!.ModelId);
        Assert.Equal(RecommendationBasis.Cpu, scene.Controller.Recommendation.Basis);
    }, probe: new GatedHardwareProbe(new HardwareProfile("CPU", 8, 16, "iGPU", GpuVendor.Intel, 128L << 20, 1L << 30, "1", GpuBackend.None), [], gated: false));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: Use recommended with the candidate installed selects it at once and the choice is made
    public void Use_recommended_selects_an_installed_candidate() => VoiceTypingPageRig.Run(headless, scene =>
    {
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Controller.UseRecommended();
        Assert.Equal(Candidate, scene.Model.Settings.VoiceTyping.Model);
        Assert.True(scene.Controller.Decided);
        Assert.Null(scene.Controller.PendingId);
        Assert.Empty(((ScriptedSpeechModelBackend)scene.Backend).Started);
    }, backend: Installed(Candidate), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    private static ScriptedSpeechModelBackend Installed(params string[] ids)
    {
        var backend = new ScriptedSpeechModelBackend();
        backend.Install(ids);
        return backend;
    }

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: "Use recommended downloads then selects"; until it ends the old model stays in use and the line says Downloading, then in use
    public void Use_recommended_downloads_then_selects() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Model = "small-q5_1" };
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Controller.UseRecommended();
        Assert.Equal([Candidate], backend.Started);
        Assert.Equal(Candidate, scene.Controller.PendingId);
        Assert.True(scene.Controller.Decided);
        Assert.Equal("small-q5_1", scene.Model.Settings.VoiceTyping.Model);
        Assert.Equal(ModelInstallKind.Downloading, scene.Controller.State(File(Candidate)).Kind);
        backend.Transfer(Candidate).Move(.5);
        scene.Until(() => scene.Controller.State(File(Candidate)).Fraction == .5);
        backend.Finish(Candidate);
        scene.Until(() => scene.Model.Settings.VoiceTyping.Model == Candidate);
        Assert.Null(scene.Controller.PendingId);
        Assert.Equal(ModelInstallKind.InUse, scene.Controller.State(File(Candidate)).Kind);
    }, backend: Installed("small-q5_1"), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-9.5 item 6 and ED: a download that fails gives the choice back, and nothing is selected
    public void A_failed_download_of_the_recommendation_gives_the_choice_back() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Controller.UseRecommended();
        backend.Finish(Candidate, ModelDownloadStatus.Failed);
        scene.Until(() => scene.Controller.PendingId is null);
        Assert.False(scene.Controller.Decided);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model);
        Assert.Equal(ModelInstallKind.NotInstalled, scene.Controller.State(File(Candidate)).Kind);
    }, backend: new ScriptedSpeechModelBackend(), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: cancelling the recommendation's download cancels the "then in use" too (the partial is kept, V-9.5 item 4)
    public void Cancelling_the_recommendations_download_clears_the_pending_choice() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Controller.UseRecommended();
        var transfer = backend.Transfer(Candidate);
        scene.Controller.Cancel(File(Candidate));
        Assert.True(transfer.Cancelled);
        scene.Until(() => scene.Controller.PendingId is null);
        Assert.False(scene.Controller.Decided);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model);
    }, backend: new ScriptedSpeechModelBackend(), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-10.6 via the self-test seam: Use recommended selects what the self-test decided, with its timing and live words recorded
    public void Use_recommended_selects_the_self_tests_result_and_records_its_timing()
    {
        var selfTest = new ScriptedSelfTest(_ => new SelfTestOutcome("medium-q8_0", 0.3, false));
        VoiceTypingPageRig.Run(headless, scene =>
        {
            _ = scene.Controller.Check();
            scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
            scene.Controller.UseRecommended();
            scene.Until(() => scene.Model.Settings.VoiceTyping.Model == "medium-q8_0");
            Assert.Equal(["large-v3-turbo-q5_0"], selfTest.Candidates);
            var record = scene.Model.Settings.VoiceHardware!;
            Assert.Equal("medium-q8_0", record.Model);
            Assert.Equal(0.3, record.Rtf);
            Assert.False(record.LiveWords);
            Assert.Equal("large-v3-turbo-q5_0", record.Candidate);
            Assert.Null(scene.Controller.PendingId);
        }, backend: Installed("large-v3-turbo-q5_0"), probe: new GatedHardwareProbe(new HardwareProfile("CPU", 8, 16, "iGPU", GpuVendor.Intel, 128L << 20, 1L << 30, "1", GpuBackend.None), [], gated: false), selfTest: selfTest);
    }

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the reason line appears once a time was measured for the recommended model
    public void The_reason_follows_the_measured_time_of_the_recommended_model()
    {
        var selfTest = new ScriptedSelfTest(id => new SelfTestOutcome(id, 0.037, true));
        VoiceTypingPageRig.Run(headless, scene =>
        {
            _ = scene.Controller.Check();
            scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
            Assert.Equal("", scene.Controller.Reason);
            scene.Controller.UseRecommended();
            scene.Until(() => scene.Controller.Reason.Length > 0);
            Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 0.5 seconds after you stop speaking.", scene.Controller.Reason);
            _ = scene.Controller.Check();
            scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
            Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 0.5 seconds after you stop speaking.", scene.Controller.Reason); // the same hardware keeps its timing
        }, backend: Installed(Candidate), probe: GatedHardwareProbe.OwnersDesktop(gated: false), selfTest: selfTest);
    }

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: Keep decides and the line reads You kept the model in use
    public void Keep_decides_without_changing_the_model_in_use() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Model = "small-q5_1" };
        _ = scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Controller.Keep();
        Assert.True(scene.Controller.Decided);
        Assert.Equal("small-q5_1", scene.Model.Settings.VoiceTyping.Model);
        _ = scene.Controller.Check();
        Assert.False(scene.Controller.Decided); // a new check asks again
    }, backend: Installed("small-q5_1"), probe: GatedHardwareProbe.OwnersDesktop(gated: false));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: Download downloads without selecting
    public void Download_fetches_a_variant_without_selecting_it() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var file = File("small-q8_0");
        scene.Controller.Download(file);
        Assert.Equal(["small-q8_0"], backend.Started);
        Assert.Equal(ModelInstallKind.Downloading, scene.Controller.State(file).Kind);
        backend.Finish("small-q8_0");
        scene.Until(() => scene.Controller.State(file).Kind == ModelInstallKind.Installed);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model);
        Assert.Null(scene.Controller.PendingId);
    }, backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: Use selects an installed variant, which then shows In use
    public void Use_selects_an_installed_variant() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var file = File("small-q8_0");
        scene.Controller.Use(file);
        Assert.Equal("small-q8_0", scene.Model.Settings.VoiceTyping.Model);
        Assert.Equal(ModelInstallKind.InUse, scene.Controller.State(file).Kind);
        scene.Controller.Use(File("tiny-q5_1")); // not installed: nothing changes
        Assert.Equal("small-q8_0", scene.Model.Settings.VoiceTyping.Model);
    }, backend: Installed("small-q8_0"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-9.5 item 4: the cross cancels a download and keeps the partial
    public void The_cross_cancels_a_download() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var file = File("small-q8_0");
        scene.Controller.Download(file);
        var transfer = backend.Transfer("small-q8_0");
        scene.Controller.Cancel(file);
        Assert.True(transfer.Cancelled);
        scene.Until(() => scene.Controller.State(file).Kind == ModelInstallKind.NotInstalled);
    }, backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: removal; the model in use is refused (the store says so)
    public void Remove_deletes_an_installed_variant_and_refuses_the_one_in_use() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        scene.Controller.Use(File("small-q8_0"));
        Assert.Equal(ModelRemoveResult.InUse, scene.Controller.Remove(File("small-q8_0")));
        Assert.Equal(ModelRemoveResult.Removed, scene.Controller.Remove(File("small-q5_1")));
        Assert.Equal(["small-q5_1"], backend.Removed);
        Assert.Equal(ModelInstallKind.NotInstalled, scene.Controller.State(File("small-q5_1")).Kind);
        Assert.Equal(File("small-q8_0").Bytes, scene.Controller.InstalledBytes);
    }, backend: Installed("small-q8_0", "small-q5_1"));

    [Fact]
    [Trait("Spec", "V-T96")]
    [Trait("Decision", "ED-267")] // V-16.3.4: the footer follows the installed bytes
    public void The_installed_bytes_follow_the_store() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(File("tiny-q5_1").Bytes + File("small-q8_0").Bytes, scene.Controller.InstalledBytes);
    }, backend: Installed("tiny-q5_1", "small-q8_0"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-2.5: with no backend bound nothing downloads and nothing throws into the page
    public void With_the_fail_closed_backend_a_download_changes_nothing() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var file = File("tiny-q5_1");
        scene.Controller.Download(file);
        scene.Controller.Use(file);
        Assert.Equal(ModelInstallKind.NotInstalled, scene.Controller.State(file).Kind);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model);
        Assert.Equal(0, scene.Controller.InstalledBytes);
    });

    private static VoiceHardwareRecord SlowCpu(string model, bool live, string? key = null) => new()
    {
        Key = key ?? "k", Origin = HardwareOrigin.Auto, Backend = VoiceBackend.Cpu, Candidate = "large-v3-turbo-q5_0", Model = model, Rtf = 0.3, LiveWords = live,
        At = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C: the answer is stored with the hardware part of the key and decides the model (R5, R6)
    public void An_answer_of_live_takes_the_rung_below_and_an_answer_of_accurate_takes_it_back() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var settings = scene.Model.Settings;
        settings.VoiceHardware = SlowCpu("large-v3-turbo-q5_0", live: false);
        settings.VoiceTyping = settings.VoiceTyping with { Model = "large-v3-turbo-q5_0" };
        var part = HardwareKey.ComputeHardwarePart(GatedHardwareProbe.CpuOnlyLaptop().Profile);

        var live = scene.Controller.Answer(LiveWordsChoice.Live);
        scene.Until(() => live.IsCompleted);
        Assert.Equal(LiveWordsChoice.Live, settings.VoiceTyping.SlowChoice);
        Assert.Equal(part, settings.VoiceTyping.SlowChoiceKey);
        Assert.Equal("medium-q8_0", settings.VoiceTyping.Model);
        Assert.Equal("medium-q8_0", settings.VoiceHardware!.Model);
        Assert.True(settings.VoiceHardware.LiveWords);
        Assert.Equal(LiveWordsChoice.Live, SelfTestPlan.AnswerFor(settings.VoiceTyping, part));

        var accurate = scene.Controller.Answer(LiveWordsChoice.Accurate);
        scene.Until(() => accurate.IsCompleted);
        Assert.Equal(LiveWordsChoice.Accurate, settings.VoiceTyping.SlowChoice);
        Assert.Equal("large-v3-turbo-q5_0", settings.VoiceTyping.Model);
        Assert.Equal("large-v3-turbo-q5_0", settings.VoiceHardware!.Model);
        Assert.False(settings.VoiceHardware.LiveWords);
    }, backend: Installed("large-v3-turbo-q5_0", "medium-q8_0"), probe: GatedHardwareProbe.CpuOnlyLaptop(gated: false));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // R6: the smaller model is downloaded when it is not there, and chosen when it arrives
    public void A_live_answer_downloads_the_smaller_model_and_selects_it_when_it_arrives() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var settings = scene.Model.Settings;
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        settings.VoiceHardware = SlowCpu("large-v3-turbo-q5_0", live: false);
        settings.VoiceTyping = settings.VoiceTyping with { Model = "large-v3-turbo-q5_0" };
        var answer = scene.Controller.Answer(LiveWordsChoice.Live);
        scene.Until(() => answer.IsCompleted);
        Assert.Equal(["medium-q8_0"], backend.Started);
        Assert.Equal("large-v3-turbo-q5_0", settings.VoiceTyping.Model);
        Assert.Equal("medium-q8_0", scene.Controller.PendingId);
        backend.Finish("medium-q8_0");
        scene.Until(() => settings.VoiceTyping.Model == "medium-q8_0");
        Assert.True(settings.VoiceHardware!.LiveWords);
        Assert.Equal("medium-q8_0", settings.VoiceHardware.Model);
    }, backend: Installed("large-v3-turbo-q5_0"), probe: GatedHardwareProbe.CpuOnlyLaptop(gated: false));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // an answer needs the hardware it is given for: with no probe it is not stored (R7: nothing half-stored)
    public void An_answer_that_cannot_be_keyed_to_the_hardware_is_not_stored() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var settings = scene.Model.Settings;
        settings.VoiceHardware = SlowCpu("large-v3-turbo-q5_0", live: false);
        settings.VoiceTyping = settings.VoiceTyping with { Model = "large-v3-turbo-q5_0" };
        var answer = scene.Controller.Answer(LiveWordsChoice.Live); // the fail-closed probe cannot read the hardware
        scene.Until(() => answer.IsCompleted);
        Assert.Null(settings.VoiceTyping.SlowChoice);
        Assert.Equal("large-v3-turbo-q5_0", settings.VoiceTyping.Model);
    }, backend: Installed("large-v3-turbo-q5_0", "medium-q8_0"));
}
