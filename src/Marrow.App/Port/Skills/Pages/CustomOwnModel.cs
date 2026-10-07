// PORT-ONLY RR-18: app-lifetime projection of T35's persisted own-items registry. All registry IO stays off the UI thread.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Custom;
using OwnKind = Marrow.Services.Port.Skills.Custom.CustomAddKind;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class CustomOwnModel(CustomSources sources) : MainActorModel
{
    private readonly SemaphoreSlim refreshGate = new(1);
    private readonly Dictionary<CustomAddKind, string?> selection = [];
    private OwnItem[] items = [];
    private int revision;
    private string? error;
    internal int Revision { get => Read(revision); private set => Set(ref revision, value); }
    internal string? Error { get => Read(error); private set => Set(ref error, value); }
    internal IReadOnlyList<OwnItem> Items(CustomAddKind kind) { _ = Revision; return items.Where(i => i.Kind == ServiceKind(kind)).ToArray(); }
    internal string? Selected(CustomAddKind kind) { _ = Revision; return selection.GetValueOrDefault(kind); }
    internal void Select(CustomAddKind kind, string? id)
    {
        if (id is not null && !Items(kind).Any(i => i.Id == id)) return;
        if (Selected(kind) == id) return;
        selection[kind] = id;
        Revision++;
    }
    internal async Task Refresh()
    {
        await refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await Task.Run(() => Enum.GetValues<OwnKind>().SelectMany(sources.List).ToArray()).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                items = snapshot;
                foreach (var kind in selection.Keys.ToArray())
                    if (selection[kind] is { } id && !items.Any(i => i.Id == id)) selection[kind] = null;
                Revision++;
            });
        }
        finally { refreshGate.Release(); }
    }
    internal async Task SetEnabled(string id, bool value) { await Task.Run(() => sources.SetEnabled(id, value)); await Refresh(); }
    internal async Task Remove(string id) { await Task.Run(() => sources.Remove(id)); await Refresh(); }
    // Event handlers report failures visibly; tests/direct callers retain normal exception propagation.
    internal async Task Perform(Func<Task> work)
    {
        try { await work(); Error = null; }
        catch (Exception e) { Error = e.Message; }
        Revision++;
    }
    internal static OwnKind ServiceKind(CustomAddKind kind) => kind switch
    {
        CustomAddKind.Plugins => OwnKind.Plugins, CustomAddKind.Skills => OwnKind.Skills, CustomAddKind.Tools => OwnKind.Tools,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
