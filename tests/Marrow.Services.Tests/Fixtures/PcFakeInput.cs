using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: PC control (ED-NEW-pcctl-1). A SendInput stand-in that records each batch and never touches Windows, and a
// clock that only moves when a test (or a Delay) moves it.
public sealed class PcFakeSendInput : ISendInput
{
    public List<IReadOnlyList<PcInputEvent>> Batches { get; } = [];
    public int? Accept { get; set; }

    public IEnumerable<PcInputEvent> All => Batches.SelectMany(b => b);

    public int Send(IReadOnlyList<PcInputEvent> events)
    {
        Batches.Add(events.ToList());
        return Accept ?? events.Count;
    }

    // A stand-in scan code: the virtual key plus 0x100, so a test can tell the two apart.
    public ushort ScanCode(ushort vk) => (ushort)(vk + 0x100);
}

public sealed class PcFakeClock : IPcClock
{
    public long NowMs { get; set; }
    public List<int> Delays { get; } = [];
    public Action<int>? OnDelay { get; set; }

    public Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(milliseconds);
        NowMs += milliseconds;
        OnDelay?.Invoke(milliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
