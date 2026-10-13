using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W10, "Clear browsing data" and "Forget agent data" (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.1 "Data: 'Clear
// browsing data' per profile (cookies, cache, history, site permissions), 'Forget agent data'", 13.4 "'Forget agent data' removes the agent
// profile's data on disk"; ED-NEW-web-w10-6). No Swift counterpart. The browser's own data (cookies, cache, site storage) can be removed only
// through a page of the profile (WebView2's Profile.ClearBrowsingDataAsync), and Settings is open when no page of the profile is. So a clear
// is a request that is written down first and carried out the moment a page of that profile exists: at once for a profile with a page open,
// else before the first navigation of the profile's next page, in this run or after a restart. Nothing deletes files under the engine's feet.
// The record is a small versioned file in nth's folder inside the web data folder; unknown fields survive a load and save, a file from a newer
// nth is left as it is, and a damaged file reads as no requests.

namespace Marrow.Services.Web;

public sealed class WebProfileClears
{
    public const int FormatVersion = 1;

    /// <summary>The file's name inside nth's folder of the web data folder.</summary>
    public const string FileName = "profile-clears.json";

    /// <summary>The most agent profiles whose last clear is remembered (the oldest go first; an agent profile is one per thread folder).</summary>
    public const int MaxAgentProfiles = 1000;

    private static readonly Logger Log = new(Logger.Subsystem, "web");

    private readonly string? file;
    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> pending = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, long> agentsCleared = new(StringComparer.Ordinal);
    private long agentsGeneration;
    private JsonObject? unknown;
    private bool newerFile;

    public WebProfileClears(string? file = null, TimeProvider? time = null)
    {
        this.file = file;
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>The record for a web data folder: <c>nth\profile-clears.json</c> inside it, read now.</summary>
    public static WebProfileClears ForFolder(string userDataFolder, TimeProvider? time = null)
    {
        var path = Path.Combine(userDataFolder, "nth", FileName);
        var clears = new WebProfileClears(path, time);
        clears.Load();
        return clears;
    }

    /// <summary>Whether a file holds the record; false keeps it in memory.</summary>
    public bool IsPersistent => file is not null;

    /// <summary>The profiles with a clear waiting (agent profiles are not listed: they are many, and named by their thread folders).</summary>
    public IReadOnlyList<string> PendingProfiles
    {
        get
        {
            lock (gate) return [.. pending.Keys.Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Whether "Forget agent data" is waiting for some agent profile.</summary>
    public bool AgentsRequested
    {
        get
        {
            lock (gate) return agentsGeneration > 0;
        }
    }

    /// <summary>
    /// Writes down that a profile's browser data is to be cleared. A private or sandboxed profile keeps nothing, so it is refused (false);
    /// an agent profile is cleared through <see cref="RequestAllAgents"/>.
    /// </summary>
    public bool Request(string profileName)
    {
        if (string.IsNullOrEmpty(profileName) || WebProfiles.IsAgentProfile(profileName)) return false;
        if (profileName is not (WebProfiles.BrowseProfile or WebProfiles.PreviewProfile)) return false;
        lock (gate) pending[profileName] = time.GetUtcNow();
        Save();
        return true;
    }

    /// <summary>
    /// Writes down "Forget agent data": every agent profile is cleared before its next page, whatever thread folder it is for. Each request is a
    /// new generation, and a profile is cleared once per generation (a count, not a time, so a clock that moves back changes nothing).
    /// </summary>
    public void RequestAllAgents()
    {
        lock (gate) agentsGeneration++;
        Save();
    }

    /// <summary>Whether a clear is waiting for the profile (a view of it is about to navigate).</summary>
    public bool IsPending(string profileName)
    {
        lock (gate)
        {
            if (!WebProfiles.IsAgentProfile(profileName)) return pending.ContainsKey(profileName);
            return agentsGeneration > 0 && (!agentsCleared.TryGetValue(profileName, out var cleared) || cleared < agentsGeneration);
        }
    }

    /// <summary>The profile's data was cleared: its request is done.</summary>
    public void Complete(string profileName)
    {
        lock (gate)
        {
            if (!WebProfiles.IsAgentProfile(profileName))
            {
                if (!pending.Remove(profileName)) return;
            }
            else
            {
                if (agentsGeneration == 0) return;
                agentsCleared.Remove(profileName); // moved to the end: the front is the one cleared longest ago
                agentsCleared[profileName] = agentsGeneration;
                while (agentsCleared.Count > MaxAgentProfiles) agentsCleared.RemoveAt(0);
            }
        }
        Save();
    }

    /// <summary>The record as text; null when the file came from a newer nth and must stay as it is.</summary>
    public string? ToJson()
    {
        lock (gate)
        {
            if (newerFile) return null;
            var root = new JsonObject
            {
                ["version"] = FormatVersion,
                ["pending"] = new JsonObject(pending.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, Stamp(pair.Value)))),
            };
            if (agentsGeneration > 0)
            {
                root["agents"] = new JsonObject
                {
                    ["generation"] = agentsGeneration,
                    ["cleared"] = new JsonObject(agentsCleared.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
                };
            }
            if (unknown is not null)
                foreach (var (name, value) in unknown) root[name] = value?.DeepClone();
            return root.ToJsonString();
        }
    }

    /// <summary>Reads the file (a missing, damaged or newer file reads as no requests).</summary>
    public void Load()
    {
        string? text = null;
        if (file is not null)
        {
            try
            {
                if (File.Exists(file)) text = File.ReadAllText(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Error("web: the profile clears file could not be read (" + error.GetType().Name + "); no clear is waiting");
                text = null;
            }
        }
        Read(text);
    }

    /// <summary>Reads a record from text (the load, and tests).</summary>
    public void Read(string? json)
    {
        lock (gate)
        {
            pending.Clear();
            agentsCleared.Clear();
            agentsGeneration = 0;
            unknown = null;
            newerFile = false;
            if (string.IsNullOrWhiteSpace(json)) return;
            JsonObject? root;
            try
            {
                root = JsonNode.Parse(json) as JsonObject;
            }
            catch (JsonException)
            {
                return;
            }
            if (root is null) return;
            if (root["version"] is JsonValue version && version.TryGetValue<int>(out var number) && number > FormatVersion)
            {
                newerFile = true;
                return;
            }
            if (root["pending"] is JsonObject requests)
                foreach (var (name, value) in requests)
                    if (name is WebProfiles.BrowseProfile or WebProfiles.PreviewProfile && ReadStamp(value) is { } at) pending[name] = at;
            if (root["agents"] is JsonObject agents && agents["generation"] is JsonValue generation && generation.TryGetValue<long>(out var count) && count > 0)
            {
                agentsGeneration = count;
                if (agents["cleared"] is JsonObject cleared)
                    foreach (var (name, value) in cleared)
                        if (WebProfiles.IsAgentProfile(name) && value is JsonValue stamp && stamp.TryGetValue<long>(out var at) && at > 0 && agentsCleared.Count < MaxAgentProfiles) agentsCleared[name] = Math.Min(at, count);
            }
            unknown = new JsonObject();
            foreach (var (name, value) in root)
                if (name is not ("version" or "pending" or "agents")) unknown[name] = value?.DeepClone();
            if (unknown.Count == 0) unknown = null;
        }
    }

    private void Save()
    {
        if (file is null) return;
        var text = ToJson();
        if (text is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The request stays in memory for this run; a request that cannot be written down is carried out now or not at all.
            Log.Error("web: the profile clears file could not be written (" + error.GetType().Name + "); the request waits in memory only");
        }
    }

    private static JsonNode Stamp(DateTimeOffset at) => JsonValue.Create(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))!;

    private static DateTimeOffset? ReadStamp(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at : null;
}
