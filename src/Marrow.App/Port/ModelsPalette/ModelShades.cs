// PORT-ONLY: AC-3 C18; immutable table reads and one shared 10Hz dispatcher post;
// ED-NEW-models-palette-6. Visible renderer authorised after the standing merge (ED-NEW-models-palette-17).
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
public sealed class ModelShades : IDisposable
{
    public const bool ApplyToUi=true;
    private readonly PaletteEngine engine;
    private readonly Action<Action> post;
    private readonly TimeProvider time;
    private readonly Lock gate=new();
    private PaletteSnapshot current,latest,previous;
    private PaletteRefreshProgress? progress;
    private ITimer? timer;
    private bool queued,disposed;
    private readonly HashSet<string> coldModels=new(StringComparer.Ordinal);
    public PaletteSnapshot Current=>current;
    public PaletteRefreshProgress? Progress {get;private set;}
    public event Action? Changed;
    public ModelShades(PaletteEngine engine,Action<Action>? post=null,TimeProvider? time=null)
    {
        this.engine=engine;this.post=post??MainActor.Post;this.time=time??TimeProvider.System;
        current=latest=previous=engine.Current;engine.Changed+=SnapshotChanged;engine.Progress+=ProgressChanged;
    }
    /// <summary>No I/O, colour math or allocation on a warm lookup. Strike consumes this seam after its merge.</summary>
    public PaletteShade? Resolve(string canonicalModelId,string? effort,bool light)
    {
        if(current.Resolve(canonicalModelId,effort,light) is {} warm)return warm;
        lock(gate)if(!disposed&&coldModels.Add(canonicalModelId))_ =engine.Observe([canonicalModelId]);
        return previous.Resolve(canonicalModelId,effort,light);
    }
    public Task Observe(IReadOnlyList<string> canonicalModelIds)=>engine.Observe(canonicalModelIds);
    private void SnapshotChanged(PaletteSnapshot snapshot){lock(gate){latest=snapshot;Schedule();}}
    private void ProgressChanged(PaletteRefreshProgress value){lock(gate){progress=value;Schedule();}}
    private void Schedule()
    {
        if(queued||disposed)return;queued=true;
        timer=time.CreateTimer(_=>post(Drain),null,TimeSpan.FromMilliseconds(100),Timeout.InfiniteTimeSpan);
    }
    private void Drain()
    {
        lock(gate)
        {
            if(disposed)return;previous=current;current=latest;Progress=progress;queued=false;
            timer?.Dispose();timer=null;
        }
        Changed?.Invoke();
    }
    public void Dispose()
    {lock(gate){disposed=true;timer?.Dispose();}engine.Changed-=SnapshotChanged;engine.Progress-=ProgressChanged;}
}
