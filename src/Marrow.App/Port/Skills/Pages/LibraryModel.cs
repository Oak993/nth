// PORT-ONLY: first-release feature unit SK-15 (DL-17). The observable face of one page's controller: the views track its Revision, which grows on the UI thread after every change.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>
/// The controller (Marrow.Services) keeps no Avalonia: it raises <see cref="LibraryController.Changed"/> from whichever thread did the work (a transfer's thread, the UI). This model hands every change
/// to the UI thread and bumps <see cref="Revision"/>, the one value the page's views track (R-VIEW-2 style), so a view is rebuilt or updated only on the UI thread. A model lives as long as its
/// controller: a page that is built again (the sidebar went elsewhere and back) finds the same run, the same dialog and the same selection.
/// </summary>
internal sealed class LibraryModel : MainActorModel
{
    private int revision;
    private bool posted;
    private bool dialogHidden;

    public LibraryModel(LibraryController controller, bool tools = false)
    {
        Controller = controller;
        Tools = tools;
        controller.Changed += OnChanged;
        revision = controller.Revision;
    }

    public LibraryController Controller { get; }

    /// <summary>Optional tools reuse the controller's run mechanics through a presentation adapter; the service always gets the original item.</summary>
    public bool Tools { get; }

    public string Noun => Tools ? "tools" : Controller.Section.Noun();

    public RunView Dialog
    {
        get
        {
            var view = Controller.Dialog;
            if (!Tools) return view;
            return view with
            {
                Title = view.Phase switch { RunPhase.Confirm => "Download all tools?", RunPhase.Downloading => "Downloading all tools", _ => view.Title },
                Foot = view.Phase == RunPhase.Done ? "Tools are used when a step needs them." : view.Foot,
            };
        }
    }

    public string DownloadAllLabel => Tools ? Controller.DownloadAllLabel.Replace("skills", "tools", StringComparison.Ordinal) : Controller.DownloadAllLabel;

    /// <summary>Grows after every change of the controller, on the UI thread.</summary>
    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    /// <summary>Whether the dialog of a run that goes on is hidden by the user (Hide, Escape): a change does not open it again, the Download all button does.</summary>
    public bool DialogHidden { get => Read(dialogHidden); set => Set(ref dialogHidden, value); }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Revision = Controller.Revision;
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
            Revision = Controller.Revision;
        });
    }
}
