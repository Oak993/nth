using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>An app-side host that answers from a script, for the end-to-end test of the setup's client against the app's own pipe server (nth installer PLAN.md 10, unit U4).</summary>
public sealed class ScriptedNthHost(Func<NthControlRequest, NthControlAnswer> answer) : INthControlHost
{
    private readonly object gate = new();
    private readonly List<NthControlRequest> requests = [];

    public IReadOnlyList<NthControlRequest> Requests
    {
        get
        {
            lock (gate) return [.. requests];
        }
    }

    public Task<NthControlAnswer> HandleAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        lock (gate) requests.Add(request);
        var result = answer(request);
        return Task.FromResult(result with { Response = result.Response with { Id = request.Id } });
    }
}
