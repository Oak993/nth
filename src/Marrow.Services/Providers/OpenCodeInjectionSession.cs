// PORT-ONLY: OD-0116, OD-0122, ED-439. OpenCode waits for the switch with the context stored by a `noReply` message on its own HTTP server; at the handover the
// conversation's ACP session loads that stored session (`session/load`). The ACP session is only opened at the handover, so until then this object is the waiting standby.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

public sealed class OpenCodeInjectionSession(SessionConfiguration configuration, Func<SessionConfiguration, ProviderSession> acp) : ProviderSession, IStandbyInjectionSession
{
    private readonly object gate = new();
    private IOpenCodeServer? server;
    private ProviderSession? live;
    private ProviderSession? starting;
    private string? sessionId;
    private bool waiting;
    private bool stopped;

    public Action<ProviderEvent>? OnEvent { get; set; }
    public string? StandbyViolation => null;
    public Action<string>? OnStandbyViolation { get; set; }

    public bool IsRunning => live is { } session ? session.IsRunning : waiting && !stopped;

    public async Task<bool> SupportsStandbyInjection(CancellationToken ct)
    {
        if (configuration.Executable is not { } executable) return false;
        if (OpenCodeHistoryInjection.Known(executable) is { } known) return known;
        IOpenCodeServer? probe = null;
        try
        {
            probe = await OpenCodeHistoryInjection.Server.Current(configuration, ct);
            var supported = OpenCodeHistoryInjection.OpenApiSupportsNoReply(await probe.OpenApiAsync(ct));
            OpenCodeHistoryInjection.Remember(executable, supported);
            return supported;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            probe?.Stop();
        }
    }

    public async Task<string> StartInjectedStandby(CancellationToken ct)
    {
        var executable = configuration.Executable ?? throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider));
        if (OpenCodeHistoryInjection.Known(executable) == false)
            throw new StandbyUnavailableException("This OpenCode has no message that is stored without a reply.");
        try
        {
            server = await OpenCodeHistoryInjection.Server.Current(configuration, ct);
            if (!OpenCodeHistoryInjection.OpenApiSupportsNoReply(await server.OpenApiAsync(ct)))
            {
                OpenCodeHistoryInjection.Remember(executable, false);
                throw new StandbyUnavailableException("This OpenCode has no message that is stored without a reply.");
            }

            OpenCodeHistoryInjection.Remember(executable, true);
            sessionId = await server.CreateSessionAsync(ct);
            waiting = true;
            return sessionId;
        }
        catch (StandbyUnavailableException)
        {
            ReleaseServer();
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReleaseServer();
            throw new StandbyUnavailableException("OpenCode's server did not accept the standby: " + error.Message);
        }
        catch
        {
            ReleaseServer();
            throw;
        }
    }

    public async Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        if (!waiting || server is null || sessionId is null) throw new StandbyUnavailableException("The standby is no longer waiting.");
        try
        {
            await server.InjectAsync(sessionId, text, ct);
            var stored = await server.MessageTextsAsync(sessionId, ct);
            if (stored.Any(part => part == text || part.Contains(text, StringComparison.Ordinal))) return;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await DiscardStandby();
            throw new StandbyUnavailableException("OpenCode did not store the context: " + error.Message);
        }

        await DiscardStandby();
        throw new StandbyUnavailableException("OpenCode did not store the context in its session.");
    }

    public async Task EndStandbyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!waiting || sessionId is not { } id) throw new InvalidOperationException("The standby is no longer waiting.");
        // The stored session stays on disk when the server stops; the conversation's own ACP session loads it.
        ReleaseServer();
        var session = acp(configuration with { ResumeID = id });
        session.OnEvent = value => OnEvent?.Invoke(value);
        using var cancellation = ct.Register(session.Stop);
        try
        {
            lock (gate)
            {
                if (stopped) throw new OperationCanceledException();
                starting = session;
            }
            var loaded = await session.Start();
            ct.ThrowIfCancellationRequested();
            if (loaded != id) throw new InvalidOperationException("OpenCode loaded a different session than the one that holds the context.");
            lock (gate)
            {
                if (stopped) throw new OperationCanceledException();
                live = session;
                waiting = false;
            }
        }
        catch
        {
            session.OnEvent = null;
            session.Stop();
            waiting = false;
            throw;
        }
        finally
        {
            lock (gate) if (ReferenceEquals(starting, session)) starting = null;
        }
    }

    public async Task DiscardStandby()
    {
        waiting = false;
        stopped = true;
        if (server is { } current && sessionId is { } id)
        {
            try { await current.DeleteSessionAsync(id, CancellationToken.None); }
            catch { /* A session that cannot be deleted is left on disk; it holds only the loaded context. */ }
        }

        ReleaseServer();
        Stop();
    }

    private void ReleaseServer()
    {
        var current = server;
        server = null;
        current?.Stop();
    }

    public Task<string> Start() => throw new InvalidOperationException("An injected standby is started with StartInjectedStandby.");

    public Task Send(TurnInput input) => live is { } session
        ? session.Send(input)
        : throw new ProviderError(new ProviderError.Case.Failed("The standby is waiting for the switch and cannot run a turn."));

    public Task Interrupt() => live?.Interrupt() ?? Task.CompletedTask;
    public void ResolveApproval(string requestID, string optionID) => live?.ResolveApproval(requestID, optionID);
    public void ResolveApproval(string requestID, string optionID, string? reason) => live?.ResolveApproval(requestID, optionID, reason);
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) => live?.AnswerQuestion(requestID, answers);
    public Task Compact() => live?.Compact() ?? Task.CompletedTask;
    public Task<bool> StopAgent(string id) => live?.StopAgent(id) ?? Task.FromResult(false);

    public void Stop()
    {
        ProviderSession? pending;
        ProviderSession? active;
        lock (gate)
        {
            stopped = true;
            waiting = false;
            pending = starting;
            active = live;
        }
        ReleaseServer();
        pending?.Stop();
        active?.Stop();
    }
}
