// PORT-ONLY: SK-15 (DL-17). One library page on a fake backend and a scripted install seam, with the waits a test needs for work that runs on another thread.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class LibraryRig : IDisposable
{
    public LibraryRig(LibrarySection section = LibrarySection.Skills, SkillsCatalogue? catalogue = null, IFirstInstall? install = null)
    {
        Install = install as ScriptedInstall ?? new ScriptedInstall();
        Backend = new FakeLibraryBackend(catalogue ?? LibraryFixtures.Catalogue(), install ?? Install);
        Page = new LibraryController(section, Backend);
    }

    public ScriptedInstall Install { get; }

    public FakeLibraryBackend Backend { get; }

    public LibraryController Page { get; }

    public LibraryRow Row(string id) => Page.Rows.Single(r => r.Id == id);

    /// <summary>Opens the confirmation and presses Continue: the run's task.</summary>
    public Task Go()
    {
        Page.OpenDownloadAll();
        return Page.Continue();
    }

    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition never held.");
            await Task.Delay(5);
        }
    }

    public static Task Within(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Dispose() => Page.Dispose();
}
