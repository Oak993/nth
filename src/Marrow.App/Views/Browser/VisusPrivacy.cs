using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W10, Settings > Visus > "Clear browsing data" (per profile) and "Forget agent data" (E:/Droppy/work/design/web-surfaces/
// DESIGN.md 8.1 "Data: 'Clear browsing data' per profile (cookies, cache, history, site permissions), 'Forget agent data', and private tabs
// leave nothing", 13.4; ED-NEW-web-w10-6). No Swift counterpart. What nth itself keeps goes at once (the visits, the kept site answers, the
// remembered app answers, the site zooms; for the model, the standing grants). What the browser keeps (cookies, cache, site storage) is
// removed through a page of the profile: at once when one is open, else before the profile's next page navigates, in this run or after a
// restart (WebProfileClears writes the request down first). The words returned say which of the two happened.

namespace Marrow.App.Views.Browser;

public static class VisusPrivacy
{
    /// <summary>The web data folder the requests are written in: the one Visus keeps its own files in, a level up; null keeps them in memory (tests).</summary>
    public static Func<string?> DataFolder { get; set; } = static () => VisusHost.DataFolder is { Length: > 0 } nth ? Path.GetDirectoryName(nth) : null;

    /// <summary>Clears one profile (browse or preview). Returns the sentence Settings shows.</summary>
    public static async Task<string> ClearBrowsingDataAsync(WebProfileKind profile, string? name = null)
    {
        var profileName = profile switch
        {
            WebProfileKind.Browse => WebProfiles.BrowseProfile,
            WebProfileKind.Preview => WebProfiles.PreviewProfile,
            _ => null,
        };
        if (profileName is null) return "";
        var words = name ?? (profile == WebProfileKind.Browse ? "Visus tabs" : "Project previews");

        VisusHost.History.Clear(profile);
        VisusHost.Permissions.Forget(profile);
        VisusHost.SchemeChoices.Forget(profile);
        VisusHost.SiteZoom.ClearProfile(profileName);

        var clears = ClearsRecord();
        await Task.Run(() => clears.Request(profileName)); // written down first, off the UI thread
        var now = VisusHost.IsStarted ? await VisusHost.Surface.ClearProfilesNowAsync(creation => creation.ProfileName == profileName) : 0;
        return now > 0 ? $"Cleared the browsing data of {words}." : $"Cleared the history and saved answers of {words}. Cookies, cache and site storage go before its next page opens.";
    }

    /// <summary>"Forget agent data": every standing grant, and the browser data of every agent profile. Returns the sentence Settings shows.</summary>
    public static async Task<string> ForgetAgentDataAsync()
    {
        if (VisusAgentStartup.Grants is { } grants) await Task.Run(grants.Clear); // writes its file: off the UI thread
        var clears = ClearsRecord();
        await Task.Run(clears.RequestAllAgents);
        var now = VisusHost.IsStarted ? await VisusHost.Surface.ClearProfilesNowAsync(creation => creation.Kind == WebProfileKind.Agent) : 0;
        return now > 0
            ? "Forgot the agent data. The sites you always allowed are gone too."
            : "Forgot the sites you always allowed. What the model's pages kept goes before each one's next page opens.";
    }

    // The engine's own record when it has one (so a request and the engine's view of it are one), else the file's.
    private static WebProfileClears ClearsRecord()
    {
        if (VisusHost.IsStarted && VisusHost.Surface.Engine.ProfileClears is { } live) return live;
        return DataFolder() is { Length: > 0 } folder ? WebProfileClears.ForFolder(folder) : new WebProfileClears();
    }
}
