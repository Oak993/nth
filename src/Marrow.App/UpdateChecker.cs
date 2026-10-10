using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;

// Source: DroppyCode/App/UpdateChecker.swift (B2 1fbf81e), :1-327.

namespace Marrow.App;

// :7-38 (R-VAL-1, R-VAL-4)
public sealed record AppVersion : IComparable<AppVersion>
{
    private AppVersion(ValueList<long> components) => Components = components;

    public ValueList<long> Components { get; } // :8

    // :10-22 init?(_ raw: String)
    public static AppVersion? Create(string raw)
    {
        var text = raw.Trimmed(); // :11 whitespacesAndNewlines
        if (SwiftString.HasPrefix(text, "v") || SwiftString.HasPrefix(text, "V")) text = text[1..]; // :12
        var parts = SwiftString.Split(text, ".", omittingEmptySubsequences: false); // :13
        if (parts.Count is < 1 or > 3) return null; // :14
        var numbers = new List<long>();
        foreach (var part in parts) // :16-19
        {
            if (part.Length == 0) return null; // :17 !part.isEmpty
            if (!SwiftString.Characters(part).All(SwiftCharacter.IsNumber)) return null; // :17 allSatisfy(\.isNumber)
            if (SwiftNumber.ParseInt(part) is not { } number) return null; // :17 Int(part)
            numbers.Add(number);
        }
        while (numbers.Count < 3) numbers.Add(0); // :20
        return new AppVersion(new ValueList<long>(numbers)); // :21
    }

    // :24-26 CustomStringConvertible
    public string Description => string.Join(".", Components.Select(c => c.ToString(CultureInfo.InvariantCulture)));

    // :29-33. Swift: text(ofTag:).
    public static string Text(string ofTag)
    {
        var text = ofTag.Trimmed(); // :30
        if (SwiftString.HasPrefix(text, "v") || SwiftString.HasPrefix(text, "V")) text = text[1..]; // :31
        return text; // :32
    }

    // :35-37
    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < Components.Count && i < other.Components.Count; i++)
        {
            var compared = Components[i].CompareTo(other.Components[i]);
            if (compared != 0) return compared;
        }
        return Components.Count.CompareTo(other.Components.Count);
    }

    // :35-37 and the Comparable operators Swift derives from `<`.
    public static bool operator <(AppVersion lhs, AppVersion rhs) => lhs.CompareTo(rhs) < 0;

    public static bool operator >(AppVersion lhs, AppVersion rhs) => lhs.CompareTo(rhs) > 0;

    public static bool operator <=(AppVersion lhs, AppVersion rhs) => lhs.CompareTo(rhs) <= 0;

    public static bool operator >=(AppVersion lhs, AppVersion rhs) => lhs.CompareTo(rhs) >= 0;
}

// :41-51. Codable: a plain JSONEncoder()/JSONDecoder() (R-CODE-4 PlainEncoder), synthesized key order and a URL
// via SwiftURL.JsonConverter (an object {"relative": "..."}, matching Foundation's URL Codable).
public sealed record AvailableUpdate
{
    public required string Version { get; init; }          // :42
    public required string Tag { get; init; }              // :43
    public required SwiftURL DownloadURL { get; init; }    // :44
    public long? Size { get; init; }                       // :45
    public required string Notes { get; init; }            // :47
    public DateTimeOffset? ReleasedAt { get; init; }       // :48
    public required SwiftURL PageURL { get; init; }        // :50

    // :41-51 encode(to:) - declaration order, nil optionals omitted (R-CODE-3).
    public static void Encode(Utf8JsonWriter writer, AvailableUpdate value)
    {
        writer.WriteStartObject();
        writer.WriteString("version", value.Version);
        writer.WriteString("tag", value.Tag);
        WriteUrl(writer, "downloadURL", value.DownloadURL);
        if (value.Size is { } size) writer.WriteNumber("size", size);
        writer.WriteString("notes", value.Notes);
        if (value.ReleasedAt is { } releasedAt) writer.WriteNumber("releasedAt", SwiftDate.ReferenceSeconds(releasedAt));
        WriteUrl(writer, "pageURL", value.PageURL);
        writer.WriteEndObject();
    }

    // :41-51 init(from:) (synthesized).
    public static AvailableUpdate Decode(JsonElement element) => new()
    {
        Version = SwiftDecoding.Decode(element, "version", SwiftDecoding.ReadString),
        Tag = SwiftDecoding.Decode(element, "tag", SwiftDecoding.ReadString),
        DownloadURL = SwiftDecoding.Decode(element, "downloadURL", ReadUrl),
        Size = SwiftDecoding.DecodeIfPresentValue(element, "size", SwiftDecoding.ReadInt),
        Notes = SwiftDecoding.Decode(element, "notes", SwiftDecoding.ReadString),
        ReleasedAt = SwiftDecoding.DecodeIfPresentValue(element, "releasedAt", SwiftDecoding.ReadDeferredDate),
        PageURL = SwiftDecoding.Decode(element, "pageURL", ReadUrl),
    };

    // Foundation's URL Codable: a keyed container carrying "relative" (SwiftURL.JsonConverter in Core/Port is
    // internal to that assembly, so this reproduces the same wire shape by hand).
    private static void WriteUrl(Utf8JsonWriter writer, string propertyName, SwiftURL value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("relative", value.AbsoluteString);
        writer.WriteEndObject();
    }

    private static SwiftURL ReadUrl(JsonElement element)
    {
        var relative = SwiftDecoding.Decode(element, "relative", SwiftDecoding.ReadString);
        return SwiftURL.Create(relative) ?? throw new DecodingError("dataCorrupted", "Invalid URL string."); // Foundation URL Codable throws, never traps
    }

    // :233-237 the bytes persist() writes (or reads back), a plain JSONEncoder()/JSONDecoder() (R-CODE-4).
    public static byte[] EncodeToBytes(AvailableUpdate value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) Encode(writer, value);
        using var document = JsonDocument.Parse(buffer.WrittenMemory, SwiftJson.DocumentOptions);
        return SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder);
    }

    public static AvailableUpdate? DecodeFromBytes(byte[] data)
    {
        using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
        return Decode(document.RootElement);
    }
}

// :61-263. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class UpdateChecker : MainActorModel
{
    // :64. R-LIFE-2: a static let shared with a side-effecting initializer -> Ambient<T>, tests replace it.
    private static readonly Ambient<UpdateChecker> SharedAmbient =
        new(() => new UpdateChecker(AppInfo.Current, Environment.GetCommandLineArgs(), DefaultFeed));

    public static UpdateChecker Shared => SharedAmbient.Current;

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    public static IDisposable OverrideShared(UpdateChecker checker) => SharedAmbient.Override(checker);

    // :67
    public const string ProjectID = "86415974";

    // :68
    public static SwiftURL ProjectURL => SwiftURL.Create("https://gitlab.com/droppyformac1/droppy-code")!;

    // :69
    public static SwiftURL ReleasesURL => ProjectURL.Appending("-/releases");

    // DEVIATION OD-0012 I1: replaces upstream's fixed apiURL (:70). The feed is an internal value with no settings UI; while it is
    // empty a check makes no network request (PR-UPDATE-005). UE-1 (OD-0100, answered 2026-10-04): the value is now the owner's public
    // GitHub repository Oak993/marrow-releases, read through Velopack (MarrowReleaseFeed). An empty feed behaves as OD-0012 I1 says.
    public const string DefaultFeed = MarrowReleaseFeed.Url;

    // :74-75
    private const double CheckIntervalSeconds = 6 * 60 * 60;
    private const double LaunchDelaySeconds = 8;

    // :77-81
    private static class Keys
    {
        public const string Update = "updateChecker_release";
        public const string InstalledRelease = "updateChecker_installedRelease";
        public const string DownloadedRelease = "updateChecker_downloadedRelease";
        public const string LastChecked = "updateChecker_lastChecked";
        public const string NotifiedVersion = "updateChecker_notifiedVersion";
    }

    private readonly AppInfo appInfo;
    private readonly string feed;
    private readonly ISelfUpdateEngine? engine;
    private readonly string? previewVersion;
    private readonly URLSession session;

    private AvailableUpdate? update;
    private readonly AvailableUpdate? installedRelease;
    private bool isChecking;
    private DateTimeOffset? lastCheckedAt;
    private string? lastError;

    private TaskHandle? backgroundTask;
    private bool activationInstalled;

    // PORT-ONLY constructor for private init() (:112-126): AppInfo.version (:83, :108), CommandLine.arguments
    // (:122) and the feed value (DEVIATION OD-0012 I1, replaces the fixed apiURL) are parameters instead of
    // captured globals, so a test can build an isolated checker. Shared passes AppInfo.Current, the process
    // arguments and DefaultFeed, exactly as :64 and :112 do for the real singleton.
    public UpdateChecker(AppInfo appInfo, IReadOnlyList<string> arguments, string feed, ISelfUpdateEngine? engine = null)
    {
        this.appInfo = appInfo;
        this.feed = feed;
        // UE-1: a feed that names the GitHub repository is read through Velopack (the shared engine, or the one given); any other
        // non-empty feed keeps the release-list JSON reader below (what a loopback feed in a test serves). Made on first use only.
        this.engine = engine ?? (MarrowReleaseFeed.IsRepositoryUrl(feed) ? SelfUpdateEngine.Shared : null);
        session = BuildSession(appInfo);

        var defaults = UserDefaults.Standard.Current;
        if (defaults.Data(Keys.Update) is { } data && // :114
            SwiftTry.Optional(() => AvailableUpdate.DecodeFromBytes(data)) is { } saved) // :115
            update = saved; // :116
        // OD-0111 / UE-6: on the first launch of the offered version, retain its notes separately. A later successful
        // no-update check clears the offer, and a future offer replaces it, but neither may erase the installed What's new.
        var downloaded = defaults.Data(Keys.DownloadedRelease) is { } downloadedData
            ? SwiftTry.Optional(() => AvailableUpdate.DecodeFromBytes(downloadedData)) : null;
        var applied = downloaded is not null && string.Equals(downloaded.Version, appInfo.Version, StringComparison.Ordinal)
            ? downloaded : update is not null && string.Equals(update.Version, appInfo.Version, StringComparison.Ordinal) ? update : null;
        if (applied is not null)
        {
            installedRelease = applied;
            defaults.Set(Keys.InstalledRelease, AvailableUpdate.EncodeToBytes(applied));
            if (ReferenceEquals(applied, downloaded)) defaults.RemoveObject(Keys.DownloadedRelease);
        }
        else if (defaults.Data(Keys.InstalledRelease) is { } installedData &&
                 SwiftTry.Optional(() => AvailableUpdate.DecodeFromBytes(installedData)) is { } installed &&
                 string.Equals(installed.Version, appInfo.Version, StringComparison.Ordinal))
            installedRelease = installed;

        lastCheckedAt = defaults.Object(Keys.LastChecked) as DateTimeOffset?; // :118

        // :119-124 `--preview-update 9.9.9` shows the update story without a newer release.
        var index = IndexOf(arguments, "--preview-update");
        if (index >= 0 && index + 1 < arguments.Count) previewVersion = arguments[index + 1];
    }

    private static int IndexOf(IReadOnlyList<string> items, string value)
    {
        for (var i = 0; i < items.Count; i++)
            if (SwiftString.Equal(items[i], value)) return i;
        return -1;
    }

    // :101-110
    private static URLSession BuildSession(AppInfo appInfo)
    {
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.RequestCachePolicy = URLRequest.CachePolicy.ReloadIgnoringLocalCacheData;
        configuration.UrlCache = null;
        configuration.TimeoutIntervalForRequest = 15;
        configuration.TimeoutIntervalForResource = 30;
        configuration.WaitsForConnectivity = false;
        // DEVIATION OD-0012 F1: "Droppy Code/<version>" -> "Marrow/<version>".
        configuration.HttpAdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["User-Agent"] = "nth/" + appInfo.Version,
            ["Accept"] = "application/json",
        };
        return new URLSession(configuration);
    }

    // :83 let currentVersion = AppInfo.version (read once, not recomputed).
    public string CurrentVersion => appInfo.Version;

    public AvailableUpdate? Update { get => Read(update); private set => Set(ref update, value); }             // :85
    /// <summary>UE-6: the notes for this running version, retained across checks and future update offers.</summary>
    public AvailableUpdate? InstalledRelease => installedRelease;

    public bool IsChecking { get => Read(isChecking); private set => Set(ref isChecking, value); }              // :86
    public DateTimeOffset? LastCheckedAt { get => Read(lastCheckedAt); private set => Set(ref lastCheckedAt, value); } // :87
    public string? LastError { get => Read(lastError); private set => Set(ref lastError, value); }             // :89

    // UE-6: preserve the captured offer after a successful download, independently of later checks. It becomes installed notes
    // only when that exact version actually runs, so a failed apply cannot overwrite the running version's What's new.
    internal static void RememberDownloadedRelease(AvailableUpdate release) =>
        UserDefaults.Standard.Current.Set(Keys.DownloadedRelease, AvailableUpdate.EncodeToBytes(release));

    // :93-96
    public bool UpdateAvailable
    {
        get
        {
            if (Update is not { } current) return false;
            if (AppVersion.Create(current.Version) is not { } latest) return false;
            if (AppVersion.Create(CurrentVersion) is not { } running) return false;
            return latest > running;
        }
    }

    // MARK: - Schedule (:128-150)

    // :132-150. The launch check, the six-hourly one after it, and a catch-up when the app comes to the front
    // after a long sleep. PLATFORM BOUNDARY: no Avalonia/Win32 "app became active" notification exists in this
    // tree yet (App.Window.WindowManager, in this unit's dependency cycle, is itself still a skeleton), so the
    // port exposes HandleActivation() for the window-focus site to call instead of subscribing to a system
    // notification directly (windows_path for PR-UPDATE-009: "use the main window gaining focus ... name that as
    // the platform boundary"). The activation observer is never unsubscribed (PR-UPDATE-010): there is no
    // unsubscribe method here, matching upstream having none either.
    public void StartBackgroundChecks()
    {
        // :133 guard backgroundTask == nil, !WebsiteCaptures.isEnabled, !AppInfo.isDevelopment else { return }.
        // ED-001/ED-061: the capture subsystem is dead code in every user launch (dead_code.md entry 28) and is
        // not ported; that guard clause is constant-false and is dropped, leaving the other two.
        if (backgroundTask is not null || appInfo.IsDevelopment) return;
        backgroundTask = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(LaunchDelaySeconds)); // :135
            while (!SwiftTask.IsCancelled) // :136
            {
                await Check(background: true); // :137
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(CheckIntervalSeconds)); // :138
            }
        });
        activationInstalled = true; // :141-149 addObserver
    }

    // :141-149, reached through HandleActivation() (see the PLATFORM BOUNDARY note above).
    // Returns the spawned handle (or null when nothing was spawned) purely so a test can await it deterministically
    // instead of guessing at real elapsed time; upstream's own closure discards it the same way (:144-148).
    public TaskHandle? HandleActivation()
    {
        if (!activationInstalled) return null; // no observer was installed (:133's guard)
        var stale = LastCheckedAt is { } last ? SwiftDate.TimeIntervalSince(SwiftDate.Now, last) > CheckIntervalSeconds : true; // :146
        return stale ? MainActor.Spawn(async () => await Check(background: true)) : null; // :147
    }

    // MARK: - Checking (:152-229)

    // :156-187. @discardableResult.
    public async Task<bool> Check(bool background = false)
    {
        if (appInfo.IsDevelopment) return false; // :158
        if (IsChecking) return UpdateAvailable; // :159
        IsChecking = true; // :160
        try
        {
            // DEVIATION OD-0012 I1: while the feed is empty, no request is made; the failure branch runs with
            // this exact message (PR-UPDATE-005, PR-UPDATE-007, PR-UPDATE-009, PR-UPDATE-014, PR-UPDATE-019).
            if (feed.Length == 0)
            {
                LastError = "No update feed is set.";
                LastCheckedAt = SwiftDate.Now;
                UserDefaults.Standard.Current.Set(Keys.LastChecked, LastCheckedAt);
                return UpdateAvailable;
            }
            if (engine is { IsInstalled: false })
            {
                // UE-1: this run is not a Velopack install (a copy, a build run from its folder): there is nothing to update in place,
                // which is what "no update feed" says (ED-319).
                LastError = "No update feed is set.";
                LastCheckedAt = SwiftDate.Now;
                UserDefaults.Standard.Current.Set(Keys.LastChecked, LastCheckedAt);
                return UpdateAvailable;
            }
            try
            {
                var found = engine is null ? await FindThroughReleaseList() : await FindThroughVelopack(engine); // :163-167
                if (found is null) // UE-1: the feed's newest is the running version: nothing is offered
                {
                    Update = null;
                    LastError = null;
                    LastCheckedAt = SwiftDate.Now;
                    Persist();
                    return UpdateAvailable;
                }
                if (previewVersion is { } preview) found = found with { Version = preview }; // :168-170
                if (found.Size is null && engine is null) found = found with { Size = await DownloadSize(found.DownloadURL) }; // :171-173
                Update = found; // :174
                LastError = null; // :175
                LastCheckedAt = SwiftDate.Now; // :176
                Persist(); // :177
                if (background && UpdateAvailable) NotifyOnce(found); // :178-180
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :181
            {
                LastError = (error as UpdateError)?.ErrorDescription ?? "Couldn't reach GitHub."; // ASCII U+0027 (byte 0x27 at :182); OD-0100: the feed is GitHub's
                LastCheckedAt = SwiftDate.Now; // :183
                UserDefaults.Standard.Current.Set(Keys.LastChecked, LastCheckedAt); // :184
            }
        }
        finally
        {
            IsChecking = false; // :161 defer
        }
        return UpdateAvailable; // :186
    }

    // :163-167, the release-list reader.
    private async Task<AvailableUpdate?> FindThroughReleaseList()
    {
        var releases = await FetchReleases(); // :163
        return NewestRelease(releases) ?? throw new UpdateError(new UpdateError.Case.NoRelease()); // :164-166
    }

    // UE-1: the same answer from Velopack's GithubSource. A newer release becomes an AvailableUpdate as the release list's newest does:
    // the version, the notes the publisher wrote, the package size; there is no per-asset address (Velopack picks and fetches the
    // package itself) so both addresses are the repository's latest-release page, which the About row's button opens (ED-319).
    private static async Task<AvailableUpdate?> FindThroughVelopack(ISelfUpdateEngine engine)
    {
        if (await engine.CheckAsync(CancellationToken.None) is not { } offer) return null;
        var page = SwiftURL.Create(MarrowReleaseFeed.LatestReleasePage)!;
        return new AvailableUpdate
        {
            Version = offer.Version,
            Tag = "v" + offer.Version,
            DownloadURL = page,
            Size = offer.SizeBytes,
            Notes = offer.NotesMarkdown ?? "",
            ReleasedAt = null,
            PageURL = page,
        };
    }

    // :189-195
    private async Task<IReadOnlyList<GitLabRelease>> FetchReleases()
    {
        var (data, response) = await session.Data(new URLRequest(new Uri(feed))); // :190
        if (response is not HTTPURLResponse http || http.StatusCode is < 200 or > 299) // :191
            throw new UpdateError(new UpdateError.Case.BadResponse((response as HTTPURLResponse)?.StatusCode ?? -1)); // :192
        using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
        return SwiftDecoding.ReadArray(document.RootElement, GitLabRelease.Decode); // :194
    }

    // :199-219. Swift: newestRelease(in:).
    public static AvailableUpdate? NewestRelease(IReadOnlyList<GitLabRelease> releases)
    {
        (AppVersion Version, AvailableUpdate Update)? best = null; // :200
        foreach (var release in releases) // :201
        {
            if (release.UpcomingRelease) continue; // :202
            if (AppVersion.Create(release.TagName) is not { } version) continue; // :202
            if (release.ReleasedAt is { } releasedAt && releasedAt > SwiftDate.Now) continue; // :203
            GitLabRelease.Link? link = null; // :204
            foreach (var candidateLink in release.GitLabReleaseAssets.Links)
                if (candidateLink.IsDiskImage) { link = candidateLink; break; }
            if (link is null) continue;
            if (SwiftURL.Create(link.DirectAssetURL ?? link.Url) is not { } downloadURL) continue; // :205
            var candidate = new AvailableUpdate // :206-214
            {
                Version = AppVersion.Text(release.TagName), // :207
                Tag = release.TagName, // :208
                DownloadURL = downloadURL, // :209
                Size = null, // :210
                Notes = release.Description ?? "", // :211
                ReleasedAt = release.ReleasedAt, // :212
                PageURL = ReleasesURL.Appending(release.TagName), // :213
            };
            if (best is { } current && current.Version >= version) continue; // :215
            best = (version, candidate); // :216
        }
        return best?.Update; // :218
    }

    // :221-229
    private async Task<long?> DownloadSize(SwiftURL url)
    {
        (byte[] Data, URLResponse Response)? result;
        try
        {
            var request = new URLRequest(new Uri(url.AbsoluteString)) { HttpMethod = "HEAD" }; // :223-224
            result = await session.Data(request); // :225
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            result = null;
        }
        if (result is not { } r) return null;
        if (r.Response is not HTTPURLResponse http) return null; // :226
        if (http.StatusCode is < 200 or > 299) return null; // :226
        if (http.ExpectedContentLength <= 0) return null; // :226
        return http.ExpectedContentLength; // :227
    }

    // :231-239
    private void Persist()
    {
        var defaults = UserDefaults.Standard.Current;
        if (update is { } found && SwiftTry.Optional(() => AvailableUpdate.EncodeToBytes(found)) is { } data) // :233
            defaults.Set(Keys.Update, data); // :234
        else
            defaults.RemoveObject(Keys.Update); // :236
        defaults.Set(Keys.LastChecked, lastCheckedAt); // :238
    }

    // MARK: - Notification (:241-262)

    // :245-261. One banner per version, the first time a background check finds it.
    private void NotifyOnce(AvailableUpdate found)
    {
        var defaults = UserDefaults.Standard.Current;
        var storedVersion = defaults.String(Keys.NotifiedVersion); // :247
        if (storedVersion is not null && SwiftString.Equal(storedVersion, found.Version)) return; // :247
        defaults.Set(Keys.NotifiedVersion, found.Version); // :248
        // DEVIATION OD-0012 F1: "Droppy Code <version> is available" -> "Marrow <version> is available".
        var title = "nth " + found.Version + " is available"; // :250
        var body = "Open Settings › About to update and restart."; // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK, :251
        // PR-UPDATE-023 windows_path: Windows 10/11 toasts need no runtime permission prompt, so the
        // notDetermined/requestAuthorization branch (:256-258) has no analogue; the port sends the toast
        // directly (WindowsToastCenter, ED-220: it posts through IWindowsToastPlatform and swallows a failed or denied post, as the
        // Swift's `try? await center.add(request)` does at :260; PR-UPDATE-023 failure_cases). The marker above is already written.
        UpdateToastCenter.Ambient.Current.Show("update-" + found.Version, title, body, found.Version); // :253
    }
}

// :265-275 (R-ERR-3). BadResponse's unlabeled Int is named after the binding errorDescription gives it (`let status`).
public sealed class UpdateError(UpdateError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record NoRelease : Case;               // :266
        public sealed record BadResponse(long Status) : Case; // :267
    }

    public Case ErrorCase { get; } = errorCase;

    // :269-274
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.NoRelease => "No release with a disk image was found.", // :271
        Case.BadResponse(var status) => "GitHub answered with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".", // :272
        _ => throw new UnreachableException(),
    };
}

// :278-327. Decodable with a custom init(from:) (:313-321).
public sealed record GitLabRelease
{
    // :279-281
    public sealed record Assets
    {
        public required ValueList<Link> Links { get; init; } // :280

        public static Assets Decode(JsonElement element) => new()
        {
            Links = SwiftDecoding.Decode(element, "links", e => SwiftDecoding.ReadArray(e, Link.Decode)),
        };
    }

    // :283-297
    public sealed record Link
    {
        public required string Name { get; init; }      // :284
        public required string Url { get; init; }       // :285
        public string? DirectAssetURL { get; init; }    // :286

        // DEVIATION OD-0008 C6 (UNK-0009): on Windows the qualifying asset is the Windows release artifact
        // rather than a .dmg; the ruling does not fix the suffix itself (PR-UPDATE-015/016 windows_path), so
        // this is an UNRESOLVED placeholder pending the owner's Windows release-asset format decision. Tests
        // use it only symbolically (the two-branch shape), never its literal text.
        public const string ArtifactSuffix = ".exe";

        // :288-291
        public bool IsDiskImage
        {
            get
            {
                var path = SwiftString.Lowercased(DirectAssetURL ?? Url); // :289
                return SwiftString.HasSuffix(path, ArtifactSuffix) ||
                    SwiftString.HasSuffix(SwiftString.Lowercased(Name), ArtifactSuffix); // :290
            }
        }

        public static Link Decode(JsonElement element) => new()
        {
            Name = SwiftDecoding.Decode(element, "name", SwiftDecoding.ReadString),
            Url = SwiftDecoding.Decode(element, "url", SwiftDecoding.ReadString),
            DirectAssetURL = SwiftDecoding.DecodeIfPresent(element, "direct_asset_url", SwiftDecoding.ReadString),
        };
    }

    public required string TagName { get; init; }                  // :299
    public string? Name { get; init; }                             // :300
    public string? Description { get; init; }                      // :301
    public DateTimeOffset? ReleasedAt { get; init; }               // :302
    public required bool UpcomingRelease { get; init; }            // :303
    public required Assets GitLabReleaseAssets { get; init; }      // :304 Swift: assets (R-NAME-3)

    // :313-321 init(from:).
    public static GitLabRelease Decode(JsonElement element) => new()
    {
        TagName = SwiftDecoding.Decode(element, "tag_name", SwiftDecoding.ReadString), // :315
        Name = SwiftDecoding.DecodeIfPresent(element, "name", SwiftDecoding.ReadString), // :316
        Description = SwiftDecoding.DecodeIfPresent(element, "description", SwiftDecoding.ReadString), // :317
        ReleasedAt = SwiftDecoding.DecodeIfPresent(element, "released_at", SwiftDecoding.ReadString) is { } iso
            ? ParseDate(iso) : null, // :318
        UpcomingRelease = SwiftDecoding.Value(element, "upcoming_release", SwiftDecoding.ReadBool, () => false), // :319
        GitLabReleaseAssets = SwiftDecoding.Value(element, "assets", Assets.Decode, () => new Assets { Links = ValueList<Link>.Empty }), // :320
    };

    // :324-326
    public static DateTimeOffset? ParseDate(string text) => ISO8601.Date(text);
}
