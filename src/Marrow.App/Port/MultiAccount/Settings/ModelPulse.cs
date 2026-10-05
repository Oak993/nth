// PORT-ONLY: T11 step 2. The observable face of a page model (the pattern of LibraryModel and ImportModel): the views track Revision, which grows on the UI thread after every change the model announces.
using Avalonia.Threading;
using Marrow.Core.Port;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class ModelPulse : MainActorModel
{
    private int revision;
    private int posted;

    /// <param name="subscribe">Hands the pulse's handler to the model's Changed event.</param>
    public ModelPulse(Action<Action> subscribe) => subscribe(OnChanged);

    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    /// <summary>Draws again now (the model changed something outside its own events).</summary>
    public void Bump() => Revision++;

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) { Revision++; return; }
        if (Interlocked.Exchange(ref posted, 1) == 1) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref posted, 0); Revision++; });
    }
}
