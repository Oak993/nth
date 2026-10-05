// PORT-ONLY test fixture (SK-11f, DL-14, R-TEST-10). A scripted offer flow: it records every ask and answers when the test says so. It
// has no downloader, so nothing a test does with it can fetch anything.
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class FakeToolOffer : IToolRequestOffer
{
    private readonly TaskCompletionSource<ToolRequestOutcome> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(string Name, string Reason)> asks = [];

    public FakeToolOffer(bool available = true, params string[] catalogue)
    {
        Available = available;
        Names = new HashSet<string>(catalogue, StringComparer.OrdinalIgnoreCase);
    }

    public bool Available { get; }
    public HashSet<string> Names { get; }
    public IReadOnlyList<(string Name, string Reason)> Asks => asks;
    public bool CancelledWhileWaiting { get; private set; }

    public bool InCatalogue(string name) => Names.Contains(name);

    public async Task<ToolRequestOutcome> Ask(string name, string reason, CancellationToken cancellation)
    {
        asks.Add((name, reason));
        try { return await answer.Task.WaitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            CancelledWhileWaiting = true;
            throw;
        }
    }

    public void Answer(ToolRequestOutcome outcome) => answer.SetResult(outcome);

    public void Fail(Exception error) => answer.SetException(error);
}
