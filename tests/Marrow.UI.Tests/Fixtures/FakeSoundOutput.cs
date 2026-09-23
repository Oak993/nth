using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// PORT-ONLY. The test output behind SoundOutput.Current (ED-151): it plays nothing, records every play with the
// sound and the volume it reached the output with, records every stop, and finishes a playback only when a test
// says so. Install it with `using var _ = SoundOutput.Current.Override(fake);`.

/// <summary>A fake <see cref="ISoundOutput"/> that records plays, stops and volumes and completes on demand.</summary>
public sealed class FakeSoundOutput : ISoundOutput
{
    private readonly object gate = new();
    private readonly List<FakeSoundPlayback> plays = [];
    private readonly List<string> log = [];

    /// <summary>When set, the device refuses to start (waveOutOpen failing), so <c>Start</c> returns null.</summary>
    public bool Refuses { get; set; }

    /// <summary>Every playback started, oldest first.</summary>
    public IReadOnlyList<FakeSoundPlayback> Plays
    {
        get { lock (gate) return plays.ToArray(); }
    }

    /// <summary>The volume each play reached the output with, oldest first.</summary>
    public IReadOnlyList<float> Volumes
    {
        get { lock (gate) return plays.Select(p => p.Volume).ToArray(); }
    }

    /// <summary>How many playbacks were stopped.</summary>
    public int Stops
    {
        get { lock (gate) return plays.Count(p => p.Stopped); }
    }

    /// <summary>"play" and "stop" in the order the output saw them.</summary>
    public IReadOnlyList<string> Log
    {
        get { lock (gate) return log.ToArray(); }
    }

    public ISoundPlayback? Start(SwiftSound sound, float volume, Action finished)
    {
        lock (gate)
        {
            if (Refuses) return null;
            var playback = new FakeSoundPlayback(this, sound, volume, finished);
            plays.Add(playback);
            log.Add("play");
            return playback;
        }
    }

    /// <summary>Plays the last sample of the most recent playback: the output reports it finished.</summary>
    public void CompleteLast()
    {
        FakeSoundPlayback last;
        lock (gate) last = plays[^1];
        last.Complete();
    }

    internal void RecordStop()
    {
        lock (gate) log.Add("stop");
    }
}

/// <summary>One playback the fake started.</summary>
public sealed class FakeSoundPlayback : ISoundPlayback
{
    private readonly FakeSoundOutput output;
    private readonly Action finished;

    internal FakeSoundPlayback(FakeSoundOutput output, SwiftSound sound, float volume, Action finished)
    {
        this.output = output;
        this.finished = finished;
        Sound = sound;
        Volume = volume;
    }

    public SwiftSound Sound { get; }
    public float Volume { get; }
    public bool Stopped { get; private set; }
    public bool Completed { get; private set; }

    public void Stop()
    {
        if (Stopped || Completed) return;
        Stopped = true;
        output.RecordStop();
    }

    /// <summary>
    /// The device's done report racing a stop: the finish callback runs although the playback was stopped, as a
    /// WOM_DONE already in flight when waveOutReset ran would.
    /// </summary>
    public void ReportFinishedLate() => finished();

    /// <summary>The device played the last sample: report it the way waveOut's WOM_DONE does.</summary>
    public void Complete()
    {
        if (Stopped || Completed) return;
        Completed = true;
        finished();
    }
}
