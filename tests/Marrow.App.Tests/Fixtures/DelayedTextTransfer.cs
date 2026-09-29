using Avalonia.Input;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A clipboard payload of one text item whose bytes arrive only when the given task completes: the real clipboard read is asynchronous.</summary>
public sealed class DelayedTextTransfer(Task<object?> text) : IAsyncDataTransfer, IAsyncDataTransferItem
{
    public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.Text];

    public IReadOnlyList<IAsyncDataTransferItem> Items => [this];

    public Task<object?> TryGetRawAsync(DataFormat format) => format == DataFormat.Text ? text : Task.FromResult<object?>(null);

    public void Dispose() { }
}
