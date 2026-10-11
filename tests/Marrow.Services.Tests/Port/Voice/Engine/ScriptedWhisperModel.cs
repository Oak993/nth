using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

/// <summary>VT-6: the loaded model of <see cref="ScriptedWhisperBackend"/>: answers from the backend's script and records its disposal.</summary>
public sealed class ScriptedWhisperModel(ScriptedWhisperBackend owner, string path, RuntimeKind runtime) : IWhisperModel
{
    public string Path { get; } = path;

    public bool Disposed { get; private set; }

    public RuntimeKind Runtime { get; } = runtime;

    public async Task<IReadOnlyList<BackendSegment>> DecodeAsync(ReadOnlyMemory<float> audio, BackendDecode decode, CancellationToken cancellationToken)
    {
        if (Disposed) throw new ObjectDisposedException(nameof(ScriptedWhisperModel));
        return await owner.DecodeAsync(this, audio, decode, cancellationToken);
    }

    public async Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio, IReadOnlyList<string> runnersUp, int threads, CancellationToken cancellationToken)
    {
        if (Disposed) throw new ObjectDisposedException(nameof(ScriptedWhisperModel));
        return await owner.DetectAsync(this, audio, runnersUp, threads, cancellationToken);
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        owner.Log.Add("dispose:" + Path);
    }
}
