// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5: "Limits per artifact, enforced by nth: 4,000 output tokens per call,
// 100,000 tokens per hour, 20 calls per minute, one call at a time; over a limit the call rejects with "rate_limited" and
// the header says "Paused: this artifact hit its model limit""; ED-NEW-web-w13-4). The limits of nth.complete, per
// artifact, on sliding windows of the injected clock. No Swift counterpart.
namespace Marrow.Services.Web;

/// <summary>An artifact's model use now: whether a call runs, the window counts, and until when it is paused.</summary>
public sealed record ArtifactModelStatus(bool Running, int CallsThisMinute, long TokensThisHour, DateTimeOffset? PausedUntil)
{
    public bool IsPaused => PausedUntil is not null;
}

public sealed class ArtifactModelMeter(TimeProvider? time = null)
{
    public const int MaxOutputTokensPerCall = 4_000;
    public const long MaxTokensPerHour = 100_000;
    public const int MaxCallsPerMinute = 20;

    /// <summary>The header's words while an artifact is over a limit.</summary>
    public const string PausedMessage = "Paused: this artifact hit its model limit";

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<ArtifactAddress, State> states = [];

    /// <summary>The output cap of one call: the artifact's own max_tokens, never above 4,000.</summary>
    public static int OutputCap(int? requested) => requested is > 0 and < MaxOutputTokensPerCall ? requested.Value : MaxOutputTokensPerCall;

    /// <summary>
    /// Starts a call, or refuses it (null, with the limit it hit) when one is running, 20 calls started in the last minute,
    /// or the last hour's tokens plus this call's output cap would pass 100,000.
    /// </summary>
    public Lease? TryBegin(ArtifactAddress artifact, int outputCap, out string? limit)
    {
        lock (gate)
        {
            var state = StateOf(artifact);
            var now = clock.GetUtcNow();
            Trim(state, now);
            limit = state.Running ? "one call at a time"
                : state.Calls.Count >= MaxCallsPerMinute ? "20 calls per minute"
                : state.Tokens.Sum(t => t.Tokens) + outputCap > MaxTokensPerHour ? "100,000 tokens per hour"
                : null;
            if (limit is not null) return null;
            state.Running = true;
            state.Calls.Enqueue(now);
            return new Lease(this, artifact);
        }
    }

    public ArtifactModelStatus Status(ArtifactAddress artifact)
    {
        lock (gate)
        {
            var state = StateOf(artifact);
            var now = clock.GetUtcNow();
            Trim(state, now);
            var tokens = state.Tokens.Sum(t => t.Tokens);
            DateTimeOffset? until = null;
            if (state.Calls.Count >= MaxCallsPerMinute) until = state.Calls.Peek() + Minute;
            if (tokens + 1 > MaxTokensPerHour)
            {
                // Paused until enough of the hour's tokens leave the window.
                var left = tokens;
                foreach (var (at, used) in state.Tokens)
                {
                    left -= used;
                    if (left + 1 <= MaxTokensPerHour) { until = Later(until, at + Hour); break; }
                }
            }

            return new ArtifactModelStatus(state.Running, state.Calls.Count, tokens, until);
        }
    }

    private void End(ArtifactAddress artifact, int tokens)
    {
        lock (gate)
        {
            var state = StateOf(artifact);
            state.Running = false;
            if (tokens > 0) state.Tokens.Enqueue((clock.GetUtcNow(), tokens));
        }
    }

    private State StateOf(ArtifactAddress artifact)
    {
        if (!states.TryGetValue(artifact, out var state)) states[artifact] = state = new State();
        return state;
    }

    private static void Trim(State state, DateTimeOffset now)
    {
        while (state.Calls.Count > 0 && now - state.Calls.Peek() >= Minute) state.Calls.Dequeue();
        while (state.Tokens.Count > 0 && now - state.Tokens.Peek().At >= Hour) state.Tokens.Dequeue();
    }

    private static DateTimeOffset Later(DateTimeOffset? a, DateTimeOffset b) => a is { } value && value > b ? value : b;

    private sealed class State
    {
        public bool Running;
        public readonly Queue<DateTimeOffset> Calls = new();
        public readonly Queue<(DateTimeOffset At, int Tokens)> Tokens = new();
    }

    /// <summary>One running call; <see cref="End"/> records its tokens (input and output) and frees the artifact.</summary>
    public sealed class Lease
    {
        private readonly ArtifactModelMeter meter;
        private readonly ArtifactAddress artifact;
        private int ended;

        internal Lease(ArtifactModelMeter meter, ArtifactAddress artifact)
        {
            this.meter = meter;
            this.artifact = artifact;
        }

        public void End(int tokens)
        {
            if (Interlocked.Exchange(ref ended, 1) == 0) meter.End(artifact, tokens);
        }
    }
}
