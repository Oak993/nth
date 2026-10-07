using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

internal sealed class ParallelCustomFiles(CustomSourceFake inner) : ICustomSourceFiles
{
    private int publishers;
    public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Both { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => inner.ReadFolder(path, cancel);
    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => inner.ReadZip(path, cancel);
    public byte[]? ReadRegistry(string root) => inner.ReadRegistry(root);
    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => inner.ReadInstalled(root, id);
    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files)
    {
        var count = Interlocked.Increment(ref publishers);
        First.TrySetResult();
        if (count == 2) Both.TrySetResult();
        Release.Task.GetAwaiter().GetResult();
        lock (inner) inner.Publish(root, id, files);
    }
    public void Delete(string root, string id) { lock (inner) inner.Delete(root, id); }
    public void SaveRegistry(string root, byte[] bytes) => inner.SaveRegistry(root, bytes);
}
