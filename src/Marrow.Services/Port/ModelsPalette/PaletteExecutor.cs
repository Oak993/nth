// PORT-ONLY: AC-3's dedicated BelowNormal actor target (never the render or UI thread); ED-617.
using System.Collections.Concurrent;
using System.Globalization;
using Marrow.Core.Port;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed class PaletteExecutor : IExecutor,IDisposable
{
    private readonly BlockingCollection<Action> jobs=new();
    private readonly Thread thread;
    public PaletteExecutor()
    {
        thread=new Thread(()=>
        {Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;Thread.CurrentThread.CurrentUICulture=CultureInfo.InvariantCulture;
            foreach(var work in jobs.GetConsumingEnumerable())work();}){Name="Marrow palette engine",IsBackground=true,Priority=ThreadPriority.BelowNormal};thread.Start();
    }
    public void Enqueue(Action work){if(!jobs.IsAddingCompleted)try{jobs.Add(work);}catch(InvalidOperationException){}}
    public void Dispose(){jobs.CompleteAdding();if(Thread.CurrentThread!=thread)thread.Join(TimeSpan.FromSeconds(2));}
}
