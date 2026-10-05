using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-14 test double of the Speech model list's backend: which files are installed and which downloads run are what the test says, and a download ends when the test ends it. No folder,
/// file or connection is touched. <see cref="InUse"/> names the selected model (the page's settings), which a removal refuses.
/// </summary>
internal sealed class ScriptedSpeechModelBackend : ISpeechModelBackend
{
    private readonly HashSet<string> installed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptedSpeechModelDownload> running = new(StringComparer.Ordinal);

    public Func<string?> InUse { get; set; } = () => null;

    /// <summary>The ids whose download was started, in order.</summary>
    public List<string> Started { get; } = [];

    /// <summary>The ids removed, in order.</summary>
    public List<string> Removed { get; } = [];

    public void Install(params string[] ids)
    {
        foreach (var id in ids) installed.Add(id);
    }

    public ScriptedSpeechModelDownload Transfer(string id) => running[id];

    /// <summary>Ends a download the way the downloader would: the file is in the store before the end is told.</summary>
    public void Finish(string id, ModelDownloadStatus status = ModelDownloadStatus.Installed)
    {
        var download = running[id];
        if (status == ModelDownloadStatus.Installed)
        {
            installed.Add(id);
            download.Move(1);
        }
        download.End(status);
    }

    public ModelInstallState State(ModelFile file)
    {
        if (running.TryGetValue(file.Id, out var download)) return new ModelInstallState(ModelInstallKind.Downloading, download.Fraction);
        if (!installed.Contains(file.Id)) return new ModelInstallState(ModelInstallKind.NotInstalled);
        return new ModelInstallState(string.Equals(InUse(), file.Id, StringComparison.Ordinal) ? ModelInstallKind.InUse : ModelInstallKind.Installed);
    }

    public long InstalledBytes => installed.Sum(id => ModelCatalog.Find(id)!.Bytes);

    public ISpeechModelDownload Download(ModelFile file)
    {
        if (running.TryGetValue(file.Id, out var current)) return current;
        var download = new ScriptedSpeechModelDownload { Ended = _ => running.Remove(file.Id) };
        running[file.Id] = download;
        Started.Add(file.Id);
        return download;
    }

    public ModelRemoveResult Remove(ModelFile file)
    {
        if (!installed.Contains(file.Id)) return ModelRemoveResult.NotInstalled;
        if (string.Equals(InUse(), file.Id, StringComparison.Ordinal)) return ModelRemoveResult.InUse;
        installed.Remove(file.Id);
        Removed.Add(file.Id);
        return ModelRemoveResult.Removed;
    }
}
