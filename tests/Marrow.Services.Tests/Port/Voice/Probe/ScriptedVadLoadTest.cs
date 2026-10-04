using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Probe;

/// <summary>VT-6: <see cref="IVadLoadTest"/> that reports a scripted runtime (or throws) and records what it was asked. No native library is touched.</summary>
public sealed class ScriptedVadLoadTest : IVadLoadTest
{
    private readonly RuntimeKind? result;
    private readonly Exception? error;

    public ScriptedVadLoadTest(RuntimeKind? result) => this.result = result;

    public ScriptedVadLoadTest(Exception error) => this.error = error;

    public List<(IReadOnlyList<RuntimeKind> Order, string? LibraryRoot)> Calls { get; } = [];

    public RuntimeKind? Run(IReadOnlyList<RuntimeKind> order, string? libraryRoot)
    {
        Calls.Add((order, libraryRoot));
        if (error is not null) throw error;
        return result;
    }
}
