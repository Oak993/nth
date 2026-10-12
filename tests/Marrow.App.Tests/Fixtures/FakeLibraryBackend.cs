// PORT-ONLY: SK-15 (DL-17). The backend of a library page with nothing behind it: a catalogue of Marrow-made items, a state a test sets, and a record of what the page asked of it. No file,
// no network, no process, no real data root.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.App.Tests.Fixtures;

internal sealed class FakeLibraryBackend(SkillsCatalogue catalogue, IFirstInstall installs) : ISkillsLibraryBackend
{
    private readonly HashSet<string> on = [];
    private readonly HashSet<string> accepted = [];

    public SkillsCatalogue Catalogue { get; } = catalogue;

    public AddonsState State { get; set; } = AddonsState.Empty;

    public IFirstInstall Installs { get; } = installs;

    public bool CanFetch { get; set; } = installs is not FailClosedFirstInstall;

    /// <summary>ED-NEW-dlhonest-1: items the bound seam could not fetch (a tool no plan can install, an unpinned item). Empty: every item answers <see cref="CanFetch"/>.</summary>
    public HashSet<string> Unfetchable { get; } = [];

    public bool CanFetchItem(string itemId) => CanFetch && !Unfetchable.Contains(itemId);

    public DriveSpace? FreeSpace { get; set; }

    public List<IReadOnlyList<ProviderSkillGroup>> Reports { get; } = [];

    public List<string> Calls { get; } = [];

    /// <summary>SK-14: how many of an item's files the folder is said to hold; what the page asked of the manual fallback.</summary>
    public int ManualMatches { get; set; }

    public string ManualFolder => "C:\\fixture\\manual";

    public int PlaceManualFiles(CatalogueItem item)
    {
        Calls.Add("place " + item.Id);
        return ManualMatches;
    }

    public void OpenManualPage(Uri page) => Calls.Add("page " + page.AbsoluteUri);

    public void OpenManualFolder() => Calls.Add("folder");

    public List<(string Key, bool On)> Writes { get; } = [];

    public ISwitchReader Switches => new Reader(on);

    public void SetSwitch(SwitchLine line, bool value)
    {
        Writes.Add((line.Key, value));
        if (value) on.Add(line.Key);
        else on.Remove(line.Key);
    }

    public void TurnOn(string key) => on.Add(key);

    public TermsOffer? Terms(string itemId)
    {
        var item = Catalogue.Items.First(i => i.Id == itemId);
        if (item.TermsClass == TermsClass.Open || accepted.Contains(itemId)) return null;
        return new TermsOffer(itemId, item.Title, "Under " + item.Vendor + "'s terms", item.LicenceName, item.LicenceUrl, "c".PadRight(64, 'c'), "I have read these terms");
    }

    public Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation) => Task.FromResult("Marrow-made fixture terms for " + itemId);

    public void AcceptTerms(string itemId)
    {
        Calls.Add("accept " + itemId);
        accepted.Add(itemId);
        State = State.AcceptTerms("c".PadRight(64, 'c'), itemId, new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
    }

    public Func<string, IProgress<double?>, CancellationToken, Task>? UpdateWork { get; set; }

    public Task UpdateAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation)
    {
        Calls.Add("update " + itemId);
        return UpdateWork?.Invoke(itemId, progress, cancellation) ?? Task.CompletedTask;
    }

    public Task<bool> RepairAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation)
    {
        Calls.Add("repair " + itemId);
        return Task.FromResult(true);
    }

    public Task RemoveAsync(string itemId, CancellationToken cancellation)
    {
        Calls.Add("remove " + itemId);
        State = AddonsState.Restore(State.Items.Where(r => r.Id != itemId), State.AcceptedTerms, State.Declined, State.Found);
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken cancellation)
    {
        Calls.Add("reset");
        State = AddonsState.Restore([], State.AcceptedTerms, [], State.Found);
        return Task.CompletedTask;
    }

    public DriveSpace? Free() => FreeSpace;

    public IReadOnlyList<ProviderSkillGroup> Providers() => Reports.Count == 0 ? [] : Reports[^1];

    public void Install(string id, string pin) =>
        State = AddonsState.Restore(State.Items.Where(r => r.Id != id).Append(new ItemRecord(id, ItemState.Installed, pin, null, null)), State.AcceptedTerms, State.Declined, State.Found);

    private sealed class Reader(HashSet<string> keys) : ISwitchReader
    {
        public bool Skill(string itemId, string path) => keys.Contains(itemId + "/" + path);

        public bool PluginFolder(string itemId, string folder) => keys.Contains(itemId + "/" + folder);
    }
}
