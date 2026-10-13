using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 barge-in within 200 ms, 12.6 Stop all is flags only): the conversation's two schedulers, the UI thread's and the background runner's, are queues the test
// empties by hand, so the order in which things happen across threads is chosen, not hoped for. A result that arrives after a barge-in or a stop is harmless; the speech is cut on the audio
// thread without waiting for either queue.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-8")]
public sealed class VoiceConversationSchedulerTests
{
    private static (VoiceRig Rig, Queue<Action> Posted, Queue<Func<Task>> Work) Rig()
    {
        var posted = new Queue<Action>();
        var work = new Queue<Func<Task>>();
        var rig = new VoiceRig(post: posted.Enqueue, background: job =>
        {
            work.Enqueue(job);
            return Task.CompletedTask;
        });
        return (rig, posted, work);
    }

    private static void Pump(Queue<Action> posted)
    {
        while (posted.Count > 0) posted.Dequeue()();
    }

    // Everything the background runner was handed, in the order it was handed: the live passes while the owner spoke, then the final one. Returns the tasks.
    private static List<Task> RunAll(Queue<Func<Task>> work)
    {
        var tasks = new List<Task>();
        while (work.Count > 0) tasks.Add(work.Dequeue()());
        return tasks;
    }

    [Fact]
    public void A_barge_in_while_the_request_is_still_being_understood_makes_its_result_harmless()
    {
        var (rig, posted, work) = Rig();
        using var disposeRig = rig;
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Heard.Enqueue("stop everything");
        rig.Say(SyntheticSpeech.Voice(2, 1.6, 0.12));
        rig.Wait(1.5);
        Assert.Equal(VoicePhase.Thinking, rig.Conversation.Phase);
        Assert.NotEmpty(work); // the live passes and the final one wait to run

        rig.Say(SyntheticSpeech.Voice(3, 0.8, 0.15)); // the owner speaks again before the answer is ready
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);

        var finished = RunAll(work);
        Pump(posted);

        Assert.All(finished, task => Assert.True(task.IsCompleted));
        Assert.Empty(rig.Controls.Calls); // the old request's "stop everything" is not acted on
        Assert.Empty(rig.Synth.Requests);
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
        Assert.Equal("", rig.Conversation.Reply);
    }

    [Fact]
    public void Stop_all_while_it_thinks_is_flags_only_and_the_late_result_says_nothing()
    {
        var (rig, posted, work) = Rig();
        using var disposeRig = rig;
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Heard.Enqueue("pause everything");
        rig.Say(SyntheticSpeech.Voice(2, 1.6, 0.12));
        rig.Wait(1.5);
        Assert.Equal(VoicePhase.Thinking, rig.Conversation.Phase);
        Pump(posted);

        ((IStopScope)rig.Conversation).Stop();

        Assert.Equal(VoicePhase.Thinking, rig.Conversation.Phase); // only flags so far: the UI thread has not run
        Assert.Single(posted);
        var finished = RunAll(work);
        Pump(posted);
        Assert.All(finished, task => Assert.True(task.IsCompleted));
        Assert.Empty(rig.Controls.Calls);
        Assert.Empty(rig.Synth.Requests);
        Assert.Equal(VoicePhase.Idle, rig.Conversation.Phase);
        Assert.False(rig.Capture.IsRunning);
    }

    [Fact]
    public void The_speech_is_cut_on_the_audio_thread_before_the_UI_thread_or_the_background_runner_has_done_anything()
    {
        var (rig, posted, work) = Rig();
        using var disposeRig = rig;
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Heard.Enqueue("what is running");
        rig.Say(SyntheticSpeech.Voice(2, 1.6, 0.12));
        rig.Wait(1.5);
        _ = RunAll(work); // the request is understood and Dextra starts to speak; the final pass's task ends with the speech
        // each step of the request that belongs to the UI thread waits for the queue to be emptied, and its continuation then runs on the pool: empty it until Dextra speaks
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Pump(posted);
            return rig.Conversation.Phase == VoicePhase.Speaking;
        }, TimeSpan.FromSeconds(10)));
        rig.Wait(1.0);
        Assert.Equal(0, rig.Player.StopCount);

        rig.Heard.Enqueue("never mind");
        rig.Say(SyntheticSpeech.Voice(3, 0.5, 0.15));

        Assert.True(rig.Player.StopCount >= 1); // nobody pumped the UI queue and nobody ran the background queue
        Assert.NotEmpty(posted);
        Assert.Empty(work);
    }
}
