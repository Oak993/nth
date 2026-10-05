// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The observable face of the import controller (the pattern of LibraryModel): the views track its Revision, which grows on the UI thread after every change.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportModel : MainActorModel
{
    private int revision;
    private bool posted;

    private int pulse;

    public ImportModel(ImportController controller, ImportDetector? detector = null)
    {
        Controller = controller;
        Detector = detector;
        controller.Changed += OnChanged;
        if (detector is not null) detector.Changed += OnChanged;
        revision = controller.Revision;
    }

    /// <summary>The finder of installed apps' skills (OD-0118 addendum); null when the page does not offer it.</summary>
    public ImportDetector? Detector { get; }

    public ImportController Controller { get; }

    /// <summary>Asks the user for a folder (the view that is on screen binds this to the system's folder chooser); null when nothing is bound or the user cancels.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
            return;
        }
        lock (this)
        {
            if (posted) return;
            posted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (this) posted = false;
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
        });
    }
}
