using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Opinions;

// OpinionAnswerRowsTests' opinions port whose Combine stays pending (ED-639), kept outside the test file (R-TEST-2/3).
internal sealed class OpinionHeldCombine(FakeOpinionsPort inner) : IOpinionsPort
{
    public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }
    public event Action<Guid>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default) => inner.AskAsync(thread, message, accounts, ct);
    public IReadOnlyList<OpinionRound> Rounds(Guid thread) => inner.Rounds(thread);
    public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default) { Calls++; return Pending.Task; }
}
