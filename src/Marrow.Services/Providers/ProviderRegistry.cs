using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), :1-879. Skeleton: ProviderStatus's
// stored properties (:10-15), the Auth cases (:4-8), the observed properties (:33-67, :281, :284) and the seeds
// that are plain data (:134-142, :146, :153) are complete; the @ObservationIgnored and private members (:38-56,
// :61-63, :68-125, :157-160, :230-258, :381-425, :442-449, :521-601, :644-647, :788-878) and every member with
// behaviour are left to the implementing unit.
//
// Part 1 of 5 (PR-MCP-175, PR-PROVACP-048, PR-PROVACP-139, PR-PROVAPI-088, PR-PROVREG-075 to 082, 094 to 105)
// implements: ProviderStatus.IsInstalled/Summary; the constructor's cache load and seeding (:162-196); the
// static seeds ClaudeSeed/MetaSeed and their supporting fields; AvailableProviders, CountsAsInstalled, Status;
// Executable(for:)/FallbackExecutables/Environment(for:); the private helpers OpenCodeModels, MergedACPCatalog,
// IsStaleClaudeSeed (kept internal, not private, so this part's tests can call them directly, matching the
// convention already used elsewhere in this file's dependency, ClaudeSession.cs); and RefreshAPIProvider,
// likewise kept internal. DeepseekSeed, ZaiSeed and CommandcodeSeed stay MARROW_STUB: their Swift callees
// (DeepSeekAPI.option(for:), ZaiAPI.option(for:), CommandCodeAPI.seed) are stubs owned by other units in this
// file's dependency cycle (section 6: missing dependency, reported, not invented here).

namespace Marrow.Services.Providers;

// :3-27. A Swift struct -> immutable record (R-VAL-1).
public sealed record ProviderStatus
{
    // :4-8 (R-ENUM-2). SignedIn's unlabeled String? is named after the binding summary gives it (`let account`).
    public abstract record Auth
    {
        private Auth() { }
        public sealed record Unknown : Auth;                      // :5
        public sealed record SignedIn(string? Account) : Auth;    // :6
        public sealed record SignedOut : Auth;                    // :7
    }

    public SwiftURL? Executable { get; init; }                         // :10
    public string? Version { get; init; }                              // :11
    public Auth ProviderStatusAuth { get; init; } = new Auth.Unknown(); // :12 Swift: auth (R-NAME-3)
    public bool IsChecking { get; init; }                              // :13
    public bool ApiKeyConfigured { get; init; }                        // :15

    // :17
    public bool IsInstalled => Executable is not null || ApiKeyConfigured;

    // U+00B7 MIDDLE DOT, used by Summary's :24 and ClaudeSeed's Fable detail (R-STR-9).
    private const string MiddleDot = "\U000000B7";

    // :19-26
    public string Summary
    {
        get
        {
            if (!IsInstalled) return "Not installed"; // :20
            return ProviderStatusAuth switch
            {
                Auth.SignedIn signedIn => signedIn.Account is { } account ? $"Signed in as {account}" : "Signed in", // :22
                Auth.SignedOut => "Not signed in", // :23
                Auth.Unknown => Version is { } version ? $"Installed {MiddleDot} {version}" : "Installed", // :24
                _ => throw new UnreachableException(),
            };
        }
    }
}

// :30-879. @MainActor @Observable -> MainActorModel (R-ISO-1, R-OBS-6). Partial: [GeneratedRegex] members
// (R-RE-1) require the declaring class to be partial.
public sealed partial class ProviderRegistry : MainActorModel
{
    private ValueMap<ProviderKind, ProviderStatus> statuses = ValueMap<ProviderKind, ProviderStatus>.Empty;
    private ValueMap<ProviderKind, ValueList<ModelOption>> catalogs = ValueMap<ProviderKind, ValueList<ModelOption>>.Empty;
    private ValueMap<string, ValueList<SlashCommand>> commands = new([], SwiftStringComparer.Instance);
    private ValueMap<string, string> commandErrors = new([], SwiftStringComparer.Instance);
    private ValueMap<string, ValueList<string>> recentCommands = new([], SwiftStringComparer.Instance);
    private ValueSet<ProviderKind> loadingCatalogs = ValueSet<ProviderKind>.Empty;
    private ValueMap<ProviderKind, PlanLimits> planLimits = ValueMap<ProviderKind, PlanLimits>.Empty;
    private ValueSet<ProviderKind> loadingLimits = ValueSet<ProviderKind>.Empty;
    private string? redeemingReset;
    private ValueMap<ProviderKind, ProviderCredits> credits = ValueMap<ProviderKind, ProviderCredits>.Empty;
    private ValueSet<ProviderKind> loadingCredits = ValueSet<ProviderKind>.Empty;
    private bool isRefreshing;
    private ProviderKind? signingIn;

    // :47. @ObservationIgnored.
    private readonly ProviderSettings settings;
    // :48
    private readonly string cacheKey = "providerModelCatalogs";
    // :50
    private readonly string catalogDatesKey = "providerModelCatalogDates";
    // :51. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> catalogFetchedAt = new();
    // :61. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> limitsFetchedAt = new();
    // :68. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> creditsFetchedAt = new();

    // U+00B7 MIDDLE DOT, used by ClaudeSeed's Fable detail (:74) (R-STR-9).
    private const string MiddleDot = "\U000000B7";

    // :70
    private static readonly ValueList<string> ClaudeEfforts = new(["low", "medium", "high", "xhigh", "max"]);

    // :71-77
    private static readonly ValueList<ModelOption> ClaudeSeed = new(
    [
        new ModelOption("default", "Default", detail: "Claude Code's recommended model", efforts: ClaudeEfforts, isDefault: true, fastTier: "fast"), // :72
        new ModelOption("opus", "Opus", efforts: ClaudeEfforts, fastTier: "fast"), // :73
        new ModelOption("claude-fable-5-1[1m]", "Fable", detail: $"Fable 5.1 {MiddleDot} Most capable for your hardest and longest-running tasks", efforts: ClaudeEfforts), // :74
        new ModelOption("sonnet", "Sonnet", efforts: ClaudeEfforts), // :75
        new ModelOption("haiku", "Haiku"), // :76
    ]);

    // :122 earlierSeedIDs.
    private static readonly ValueSet<string> EarlierClaudeSeedIDs = new(["default", "opus", "sonnet", "haiku"], SwiftStringComparer.Instance);

    public ValueMap<ProviderKind, ProviderStatus> Statuses { get => Read(statuses); private set => Set(ref statuses, value); } // :33
    public ValueMap<ProviderKind, ValueList<ModelOption>> Catalogs { get => Read(catalogs); private set => Set(ref catalogs, value); } // :34
    public ValueMap<string, ValueList<SlashCommand>> Commands { get => Read(commands); private set => Set(ref commands, value); } // :35
    public ValueMap<string, string> CommandErrors { get => Read(commandErrors); private set => Set(ref commandErrors, value); } // :36
    public ValueMap<string, ValueList<string>> RecentCommands { get => Read(recentCommands); private set => Set(ref recentCommands, value); } // :37
    public ValueSet<ProviderKind> LoadingCatalogs { get => Read(loadingCatalogs); private set => Set(ref loadingCatalogs, value); } // :45
    public ValueMap<ProviderKind, PlanLimits> PlanLimits { get => Read(planLimits); private set => Set(ref planLimits, value); } // :57
    public ValueSet<ProviderKind> LoadingLimits { get => Read(loadingLimits); private set => Set(ref loadingLimits, value); } // :58
    public string? RedeemingReset { get => Read(redeemingReset); private set => Set(ref redeemingReset, value); } // :60
    public ValueMap<ProviderKind, ProviderCredits> Credits { get => Read(credits); private set => Set(ref credits, value); } // :66
    public ValueSet<ProviderKind> LoadingCredits { get => Read(loadingCredits); private set => Set(ref loadingCredits, value); } // :67
    public bool IsRefreshing { get => Read(isRefreshing); private set => Set(ref isRefreshing, value); } // :281
    public ProviderKind? SigningIn { get => Read(signingIn); private set => Set(ref signingIn, value); } // :284

    // :127. DeepSeekAPI.option(for:) (DeepSeekAPI.swift:79) is a MARROW_STUB owned by unit
    // Services.Providers.DeepSeekAPI: missing dependency (section 6), stays unwritten here.
    public static ValueList<ModelOption> DeepseekSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:127 " +
            "(blocked on DeepSeekAPI.Option, unit Services.Providers.DeepSeekAPI)");

    // :128-129. Built with MetaAPI.option(for:).
    public static ValueList<ModelOption> MetaSeed => new(new[]
        {
            "muse-spark-1.3", "muse-spark-1.3-contributor", "muse-spark-1.2", "muse-spark-1.2-contributor", "muse-spark-1.1",
        }
        .Select(MetaAPI.Option)
        .OfType<ModelOption>());

    // :130. ZaiAPI.option(for:) (ZaiAPI.swift:49) is a MARROW_STUB owned by unit Services.Providers.ZaiAPI:
    // missing dependency (section 6), stays unwritten here.
    public static ValueList<ModelOption> ZaiSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:130 " +
            "(blocked on ZaiAPI.Option, unit Services.Providers.ZaiAPI)");

    // :134-142
    public static readonly ValueList<ModelOption> AntigravitySeed = new(new[]
    {
        new ModelOption("gemini-3.8-flash", "Gemini 3.8 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high", isDefault: true), // :135
        new ModelOption("gemini-3.7-flash", "Gemini 3.7 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high"), // :136
        new ModelOption("gemini-3.6-flash", "Gemini 3.6 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high"), // :137
        new ModelOption("gemini-3.1-pro", "Gemini 3.1 Pro", efforts: new(new[] { "low", "high" }), defaultEffort: "high"), // :138
        new ModelOption("claude-sonnet-4-6", "Claude Sonnet 4.6 (Thinking)", efforts: ValueList<string>.Empty), // :139
        new ModelOption("claude-opus-4-6-thinking", "Claude Opus 4.6 (Thinking)", efforts: ValueList<string>.Empty), // :140
        new ModelOption("gpt-oss-120b", "GPT-OSS 120B", efforts: new(new[] { "medium" }), defaultEffort: "medium"), // :141
    });

    // :146
    public static readonly ModelOption CopilotAuto =
        new("auto", "Auto", detail: "Copilot picks the model for each request", isDefault: true);

    // :150. CommandCodeAPI.seed (CommandCodeAPI.swift:349) is a MARROW_STUB owned by unit
    // Services.Providers.CommandCodeAPI: missing dependency (section 6), stays unwritten here.
    public static ValueList<ModelOption> CommandcodeSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:150 " +
            "(blocked on CommandCodeAPI.Seed, unit Services.Providers.CommandCodeAPI)");

    // :153
    public static readonly ValueList<ModelOption> PiSeed = new(new[]
    {
        new ModelOption("default", "Default", detail: "The model pi itself is set to",
            efforts: new(new[] { "off", "minimal", "low", "medium", "high", "xhigh", "max" }), defaultEffort: "medium", isDefault: true),
    });

    // :162-196. ED-001: CaptureRun is not ported (dead in a shipped launch); the non-capture branch is the
    // only one implemented. :164 (recentCommands), :165-178 (cached catalogs), :179-185 (cached dates) and
    // :186-195 (seeding) each read `!CaptureRun.isEnabled` in the source and are ported without that guard.
    public ProviderRegistry(ProviderSettings settings)
    {
        this.settings = settings; // :163

        // :164
        if (UserDefaults.Standard.Current.Dictionary("recentSlashCommands") is { } storedRecent)
        {
            var recent = new Dictionary<string, ValueList<string>>();
            foreach (var (key, value) in storedRecent)
            {
                if (value is IReadOnlyList<object> list && list.All(item => item is string))
                    recent[key] = new ValueList<string>(list.Cast<string>());
            }
            recentCommands = new ValueMap<string, ValueList<string>>(recent, SwiftStringComparer.Instance);
        }

        // :165-178
        if (UserDefaults.Standard.Current.Data(cacheKey) is { } cacheData && DecodeCatalogCache(cacheData) is { } cachedCatalogs)
        {
            foreach (var (key, list) in cachedCatalogs)
            {
                if (SwiftEnum<ProviderKind>.FromRawValue(key) is not { } provider) continue; // :169
                // :170-176. A list cached before the scales were ordered may still run high to low (Grok's
                // did); the slider reads them low to high, so they are put right here.
                var split = OpenCodeModels(list, provider);
                var ordered = new ValueList<ModelOption>(split.Select(option =>
                    option with { Efforts = ACPSession.OrderedEfforts(option.Efforts) }));
                catalogs = catalogs.Mutating(d => d[provider] = ordered);
            }
        }

        // :179-185
        if (UserDefaults.Standard.Current.Data(catalogDatesKey) is { } datesData && DecodeCatalogDates(datesData) is { } rawDates)
        {
            foreach (var (key, seconds) in rawDates)
            {
                if (SwiftEnum<ProviderKind>.FromRawValue(key) is { } provider) catalogFetchedAt[provider] = SwiftDate.From1970Seconds(seconds); // :183
            }
        }

        // :186
        if (IsStaleClaudeSeed(catalogs.TryGetValue(ProviderKind.Claude, out var claudeList) ? claudeList : ValueList<ModelOption>.Empty))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Claude] = ClaudeSeed);
        // :187
        if (!(catalogs.TryGetValue(ProviderKind.Deepseek, out var deepseekList) && deepseekList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Deepseek] = DeepseekSeed);
        // :188
        if (!(catalogs.TryGetValue(ProviderKind.Meta, out var metaList) && metaList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Meta] = MetaSeed);
        // :189
        if (!(catalogs.TryGetValue(ProviderKind.Zai, out var zaiList) && zaiList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Zai] = ZaiSeed);
        // :190-192
        var antigravityStale = !(catalogs.TryGetValue(ProviderKind.Antigravity, out var antigravityList) && antigravityList.Count > 0)
            || antigravityList.Any(option => option.Id.EndsWith("-high", StringComparison.Ordinal)
                || option.Id.EndsWith("-medium", StringComparison.Ordinal)
                || option.Id.EndsWith("-low", StringComparison.Ordinal));
        if (antigravityStale) catalogs = catalogs.Mutating(d => d[ProviderKind.Antigravity] = AntigravitySeed);
        // :193
        if (!(catalogs.TryGetValue(ProviderKind.Copilot, out var copilotList) && copilotList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Copilot] = new ValueList<ModelOption>([CopilotAuto]));
        // :194
        if (!(catalogs.TryGetValue(ProviderKind.Commandcode, out var commandcodeList) && commandcodeList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Commandcode] = CommandcodeSeed);
        // :195
        if (!(catalogs.TryGetValue(ProviderKind.Pi, out var piList) && piList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.Pi] = PiSeed);
    }

    // JSONDecoder().decode([String: [ModelOption]].self, from:) (:167). ModelOption.Decode is the port's
    // per-field reader (R-CODE-1); a malformed cache decodes to nil like the Swift `try?`.
    private static Dictionary<string, ValueList<ModelOption>>? DecodeCatalogCache(byte[] data) => SwiftTry.Optional(() =>
    {
        using var document = JsonDocument.Parse(data);
        var result = new Dictionary<string, ValueList<ModelOption>>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var options = new List<ModelOption>();
            foreach (var element in property.Value.EnumerateArray()) options.Add(ModelOption.Decode(element));
            result[property.Name] = new ValueList<ModelOption>(options);
        }
        return result;
    });

    // JSONDecoder().decode([String: Double].self, from:) (:181).
    private static Dictionary<string, double>? DecodeCatalogDates(byte[] data) => SwiftTry.Optional(() =>
    {
        using var document = JsonDocument.Parse(data);
        var result = new Dictionary<string, double>();
        foreach (var property in document.RootElement.EnumerateObject()) result[property.Name] = property.Value.GetDouble();
        return result;
    });

    // :121-124. Private in Swift; kept internal so this part's tests (PR-PROVACP-048) can exercise it directly.
    internal static bool IsStaleClaudeSeed(IReadOnlyList<ModelOption> list) =>
        list.Count == 0 || list.All(option => EarlierClaudeSeedIDs.Contains(option.Id));

    // :198-200
    public ValueList<ProviderKind> AvailableProviders =>
        new(SwiftEnum<ProviderKind>.AllCases.Where(provider => settings.IsEnabled(provider) && CountsAsInstalled(provider)));

    // :202-208. ED-001: the CaptureRun-only comment context does not change this member; ported as written.
    public bool CountsAsInstalled(ProviderKind provider) =>
        statuses.TryGetValue(provider, out var status) ? status.IsInstalled : true; // :207

    // :210-217. ED-001: the CaptureRun branch (:213-215) is dead in a shipped launch and not ported.
    public ProviderStatus Status(ProviderKind provider) =>
        statuses.TryGetValue(provider, out var status) ? status : new ProviderStatus(); // :216

    // :219-226. Swift: executable(for:).
    public SwiftURL? Executable(ProviderKind provider) =>
        ResolveExecutable(provider) is { } path ? FileUrl(path) : null;

    // The plain-path resolution :219-226 works in, returning a raw path so Environment(for:) needs no
    // URL round trip to find the executable's directory.
    private string? ResolveExecutable(ProviderKind provider)
    {
        if (provider.IsAPIKeyBased) return null; // :220
        var custom = settings.BinaryPath(provider); // :221
        if (custom.Length == 0 && provider.ExecutableName.Length == 0) return null; // :222
        var primary = custom.Length == 0 ? provider.ExecutableName : custom;
        if (LoginEnvironment.Which(primary) is { } found && !RejectsAsCommandInterpreter(provider, found)) return found; // :223
        if (custom.Length != 0) return null; // :224
        foreach (var candidate in FallbackExecutables(provider)) // :225
        {
            if (LoginEnvironment.Which(candidate) is { } resolved && !RejectsAsCommandInterpreter(provider, resolved)) return resolved;
        }
        return null;
    }

    // PA-0016, ED-020, ED-127: Command Code's bare executableName is "cmd" (Provider.swift:54), which on
    // Windows PATH+PATHEXT resolves the operating system's own interpreter first. A candidate is rejected
    // when its version resource identifies the Windows command interpreter (PA-0016 steps 2-3, ported as
    // ProcessLauncher.IsWindowsCommandInterpreter) or its directory is %SystemRoot%\System32 or \SysWOW64
    // (ED-020's %SystemRoot% exclusion, ED-127: implemented by this resolver, not by Which). DEVIATION
    // PA-0016/ED-020/ED-127: the source has no such check, macOS having no `cmd` on PATH.
    private static bool RejectsAsCommandInterpreter(ProviderKind provider, string path)
    {
        if (provider != ProviderKind.Commandcode) return false;
        if (ProcessLauncher.IsWindowsCommandInterpreter(path)) return true;
        var systemRoot = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        var directory = SwiftTry.Optional(() => Path.GetDirectoryName(Path.GetFullPath(path))) ?? "";
        return string.Equals(directory, Path.Join(systemRoot, "System32"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(directory, Path.Join(systemRoot, "SysWOW64"), StringComparison.OrdinalIgnoreCase);
    }

    // :228-258. Private in Swift, kept private: only ResolveExecutable calls it. Each returned candidate is a
    // raw string ResolveExecutable resolves through LoginEnvironment.Which, which reproduces both the
    // source's `which(name)` calls (a bare name, no separator) and its `isExecutableFile` tests (a
    // constructed path, containing a separator) with one PATHEXT-aware rule (ED-031).
    private static IReadOnlyList<string> FallbackExecutables(ProviderKind provider)
    {
        switch (provider)
        {
            case ProviderKind.Copilot: // :232-237
            {
                var environment = LoginEnvironment.Current;
                var dataDirectory = NonEmpty(environment, "GH_DATA_DIR")
                    ?? (NonEmpty(environment, "XDG_DATA_HOME") is { } xdg ? SwiftPath.AppendingPathComponent(xdg, "gh") : null)
                    ?? SwiftPath.AppendingPathComponent(LoginEnvironment.HomeDirectory, ".local/share/gh");
                return [SwiftPath.AppendingPathComponent(dataDirectory, "copilot/copilot")];
            }
            case ProviderKind.Commandcode: // :238-251
            {
                var home = LoginEnvironment.HomeDirectory;
                var candidates = new List<string> { "command-code", "commandcode", "cmd" }; // :243-245
                foreach (var directory in new[] { $"{home}/.local/bin", "/opt/homebrew/bin", "/usr/local/bin", $"{home}/.npm-global/bin", $"{home}/.volta/bin" }) // :246
                {
                    foreach (var name in new[] { "command-code", "cmd" }) candidates.Add(SwiftPath.AppendingPathComponent(directory, name)); // :247-249
                }
                return candidates;
            }
            case ProviderKind.Pi: // :252-254
                var piHome = LoginEnvironment.HomeDirectory;
                return
                [
                    $"{piHome}/.npm-global/bin/pi", "/opt/homebrew/bin/pi", "/usr/local/bin/pi",
                    $"{piHome}/.local/bin/pi", $"{piHome}/.volta/bin/pi", $"{piHome}/.bun/bin/pi",
                ];
            default:
                return []; // :255-257
        }
    }

    private static string? NonEmpty(IReadOnlyDictionary<string, string> environment, string key) =>
        environment.TryGetValue(key, out var value) && value.Length != 0 ? value : null;

    // ENGINEERING DECISION, matching ClaudeSessionTests.cs's FileUrl: F.URL (SwiftURL) only builds
    // URL(string:), and a "file://" URL's .Path forces swift-foundation's POSIX FileSystemPath() branch,
    // which prepends a slash before a Windows drive letter ("/C:/x") that CreateProcessW rejects. A
    // non-"file" scheme's .Path takes the plain RFC3986Parser.PercentDecode branch instead, so it round
    // trips a real Windows path unchanged; every caller of Executable(for:) reads only .Path, never .Scheme.
    private static SwiftURL FileUrl(string path) =>
        SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/')) ?? throw SwiftTraps.Fatal("Invalid executable path.");

    // :260-279. Swift: environment(for:).
    public ValueMap<string, string> Environment(ProviderKind provider)
    {
        var environment = new Dictionary<string, string>(LoginEnvironment.Current, StringComparer.OrdinalIgnoreCase); // :261, ED-128
        if (ResolveExecutable(provider) is { } executablePath) // :262
        {
            var directory = SwiftPath.DeletingLastPathComponent(executablePath);
            if (environment.TryGetValue("PATH", out var path)) // :263
            {
                // :264. DEVIATION PR-PROVREG-099 windows_path: compared case-insensitively, as Windows paths are.
                var entries = path.Split(';');
                if (!entries.Contains(directory, StringComparer.OrdinalIgnoreCase)) environment["PATH"] = directory + ";" + path; // :265
            }
        }
        // :267-272. A Studio key pasted into Settings reaches the CLI the way its docs say, and takes
        // precedence over the `cmd login` in `~/.commandcode/auth.json`.
        if (provider == ProviderKind.Commandcode)
        {
            var key = settings.ApiKey(ProviderKind.Commandcode); // :270
            if (key.Length != 0) environment["COMMAND_CODE_API_KEY"] = key; // :271
        }
        // :273-275. OpenCode takes its MCP servers as config content in the environment, so the connected
        // servers reach it without a line written into its config files.
        if (provider == ProviderKind.Opencode || provider == ProviderKind.OpencodeGo)
        {
            foreach (var (key, value) in MCPSessionOverrides.OpencodeEnvironment()) environment[key] = value;
        }
        // :276-277. The CLI reads system settings from that path, so the connected servers reach it without a
        // line in `~/.gemini/settings.json`.
        if (provider == ProviderKind.Antigravity && MCPProviderConfig.GeminiSettingsPath() is { } settingsPath)
        {
            environment["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = settingsPath;
        }
        return new ValueMap<string, string>(environment, StringComparer.OrdinalIgnoreCase); // :278
    }

    // :289-310
    public Task<string?> SignIn(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:289");

    // :315-333
    public Task RefreshEverything() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:315");

    // :335-338. How many providers are checked at the same time.
    private const int ProbeWidth = 4;

    // :340-356. R-TASK-5: withTaskGroup(of: Void.self) -> TaskGroup.Run; addTask -> AddTask; for await _ in
    // group -> while (await group.Next()).
    public async Task RefreshAll()
    {
        var providers = SwiftEnum<ProviderKind>.AllCases; // :341
        var next = 0;
        await TaskGroup.Run(async group =>
        {
            while (next < providers.Count && next < ProbeWidth) // :344
            {
                var provider = providers[next];
                // R-TASK-5, R-ISO-4: the child body hops onto the main actor for Refresh's own state
                // mutations (Swift: `self.refresh(provider)`, a MainActor-isolated call, hops implicitly).
                group.AddTask(() => MainActor.Invoke(() => Refresh(provider))); // :346
                next++; // :347
            }
            while (await group.Next()) // :349
            {
                if (next >= providers.Count) continue; // :350
                var provider = providers[next]; // :351
                next++; // :352
                group.AddTask(() => MainActor.Invoke(() => Refresh(provider))); // :353
            }
        });
    }

    // :358-378
    public async Task Refresh(ProviderKind provider)
    {
        var status = statuses.TryGetValue(provider, out var existing) ? existing : new ProviderStatus(); // :359
        status = status with { IsChecking = true }; // :360
        statuses = statuses.Mutating(d => d[provider] = status); // :361
        if (provider.IsAPIKeyBased) // :362
        {
            await RefreshAPIProvider(provider); // :363
            return; // :364
        }
        await LoginEnvironment.Load(); // :366
        var executable = Executable(provider); // :367
        if (executable is null)
        {
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus()); // :368
            return; // :369
        }
        var environment = Environment(provider); // :371
        // :372-374. Both probes spawn the CLI: run one after the other, not together.
        var version = await Version(executable.Path, environment); // :375
        var auth = await Auth(provider, executable, environment); // :376
        statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :377
        {
            Executable = executable,
            Version = version,
            ProviderStatusAuth = auth,
        });
    }

    // :788-796. Swift: version(_:_:).
    internal static async Task<string?> Version(string executable, IReadOnlyDictionary<string, string> environment)
    {
        var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
            await Shell.Run(executable, ["--version"], environment: environment, timeout: 20)); // :789-790
        if (result is not { Succeeded: true }) return null;
        var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput); // :791
        var match = VersionPattern().Match(text); // :792-794
        return match.Success ? match.Value : null; // :795
    }

    // :792. \d+\.\d+(?:\.\d+)?(?:[-.][0-9A-Za-z]+)?
    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?(?:[-.][0-9A-Za-z]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    // :798-867. Swift: auth(_:_:_:). Kept internal, matching the convention this file's dependency
    // (ClaudeSession.cs) already uses, so this part's tests can call it directly without going through
    // Refresh's executable resolution.
    internal static async Task<ProviderStatus.Auth> Auth(ProviderKind provider, SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        switch (provider)
        {
            case ProviderKind.Codex: // :799-805
            {
                var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                    await Shell.Run(executable.Path, ["login", "status"], environment: environment, timeout: 20));
                if (result is null) return new ProviderStatus.Auth.Unknown();
                var text = result.Output + result.ErrorOutput;
                return result.Succeeded && SwiftString.LocalizedCaseInsensitiveContains(text, "logged in")
                    ? new ProviderStatus.Auth.SignedIn(null)
                    : new ProviderStatus.Auth.SignedOut();
            }
            case ProviderKind.Claude: // :806-809
            {
                var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                    await Shell.Run(executable.Path, ["auth", "status"], environment: environment, timeout: 20));
                var json = result is null ? null : JSONValue.Parse(result.Stdout);
                if (json is null) return new ProviderStatus.Auth.Unknown();
                return json["loggedIn"]?.JSONValueBool == true
                    ? new ProviderStatus.Auth.SignedIn(json["email"]?.JSONValueString)
                    : new ProviderStatus.Auth.SignedOut();
            }
            case ProviderKind.Cursor: // :810-816
            {
                var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                    await Shell.Run(executable.Path, ["status"], environment: environment, timeout: 20));
                if (result is null) return new ProviderStatus.Auth.Unknown();
                var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
                var match = CursorAccountPattern().Match(text);
                if (match.Success) return new ProviderStatus.Auth.SignedIn(match.Groups[1].Value);
                return SwiftString.LocalizedCaseInsensitiveContains(text, "not logged in")
                    ? new ProviderStatus.Auth.SignedOut()
                    : new ProviderStatus.Auth.Unknown();
            }
            case ProviderKind.Devin: // :817-824
            {
                var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                    await Shell.Run(executable.Path, ["auth", "status"], environment: environment, timeout: 20));
                if (result is null) return new ProviderStatus.Auth.Unknown();
                var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
                if (SwiftString.LocalizedCaseInsensitiveContains(text, "not logged in")) return new ProviderStatus.Auth.SignedOut();
                if (!SwiftString.LocalizedCaseInsensitiveContains(text, "logged in")) return new ProviderStatus.Auth.Unknown();
                return new ProviderStatus.Auth.SignedIn(DevinAccount(text));
            }
            case ProviderKind.Antigravity: // :825
                return await AntigravitySession.AuthStatus(executable, environment);
            case ProviderKind.Copilot: // :827
                return await CopilotSession.AuthStatus(executable, environment);
            case ProviderKind.Commandcode: // :829
                return await CommandCodeAPI.AuthStatus(executable, environment);
            case ProviderKind.Pi: // :831
                return await PiCLI.AuthStatus(executable, environment);
            case ProviderKind.Opencode: // :833-851
            case ProviderKind.OpencodeGo:
            {
                var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                    await Shell.Run(executable.Path, ["auth", "list"], environment: environment, timeout: 20));
                if (result is null) return new ProviderStatus.Auth.Unknown();
                var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput); // :834
                var lines = text.Split("\n"); // :835. O-13: components(separatedBy: "\n").
                var credentialLines = lines.TakeWhile(line => !line.Contains("Environment", StringComparison.Ordinal)); // :835
                var credentials = new List<string>();
                foreach (var rawLine in credentialLines) // :836-844
                {
                    var trimmed = FoundationText.Trim(rawLine); // :837
                    if (!SwiftString.HasPrefix(trimmed, "\U000025CF")) continue; // U+25CF BLACK CIRCLE, :838
                    var name = FoundationText.Trim(SwiftString.DropFirst(trimmed)); // :839
                    foreach (var suffix in new[] { " api", " oauth" }) // :840
                    {
                        if (SwiftString.HasSuffix(SwiftString.Lowercased(name), suffix)) name = SwiftString.DropLast(name, suffix.Length); // :841
                    }
                    if (name.Length != 0) credentials.Add(name); // :843
                }
                // :846-848. Go carries its own credential in the one auth file; Zen is whatever else is there.
                var chosen = provider == ProviderKind.OpencodeGo
                    ? credentials.FirstOrDefault(credential => SwiftString.LocalizedCaseInsensitiveContains(credential, "opencode go"))
                    : credentials.FirstOrDefault();
                if (chosen is not null) return new ProviderStatus.Auth.SignedIn(chosen); // :849-850
                return result.Succeeded ? new ProviderStatus.Auth.SignedOut() : new ProviderStatus.Auth.Unknown(); // :851
            }
            case ProviderKind.Grok: // :852-863
            {
                var path = testGrokAuthPath ?? SwiftPath.AppendingPathComponent(LoginEnvironment.HomeDirectory, ".grok/auth.json");
                var data = SwiftTry.Optional(() => File.ReadAllBytes(path));
                using var document = data is null ? null : SwiftTry.Optional(() => JsonDocument.Parse(data));
                if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return new ProviderStatus.Auth.Unknown(); // :854
                var properties = document.RootElement.EnumerateObject().ToList();
                if (properties.Count == 0) return new ProviderStatus.Auth.SignedOut(); // :855
                var entries = properties.Select(property => property.Value).Where(value => value.ValueKind == JsonValueKind.Object).ToList(); // :856
                if (Field(entries, "email") is { } email) return new ProviderStatus.Auth.SignedIn(email); // :857-859
                if (Field(entries, "first_name") is { } firstName) return new ProviderStatus.Auth.SignedIn(firstName); // :860-862
                return new ProviderStatus.Auth.SignedIn(null); // :863
            }
            case ProviderKind.Deepseek: // :864-865
            case ProviderKind.Meta:
            case ProviderKind.Zai:
                return new ProviderStatus.Auth.Unknown();
            default:
                throw new UnreachableException();
        }
    }

    // :815. Logged in as (\S+)
    [GeneratedRegex(@"Logged in as (\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex CursorAccountPattern();

    // :857-862. The first non-empty string field across the entries, or null.
    private static string? Field(IReadOnlyList<JsonElement> entries, string key)
    {
        foreach (var entry in entries)
        {
            if (entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text) return text;
        }
        return null;
    }

    // PORT-ONLY testing support (matching MCPSessionOverrides.cs's convention): overrides the Grok auth.json
    // path so PR-PROVREG-121/122's tests never read %USERPROFILE%\.grok\auth.json.
    private static string? testGrokAuthPath;

    internal static void SetGrokAuthPathForTesting(string path) => testGrokAuthPath = path;

    internal static void ResetGrokAuthPathForTesting() => testGrokAuthPath = null;

    // :869-878. Swift: devinAccount(from:).
    private static string? DevinAccount(string text)
    {
        foreach (var line in SwiftString.ComponentsSeparatedByNewlines(text)) // :870
        {
            var trimmed = line.Trimmed(); // :871
            if (!SwiftString.HasPrefix(SwiftString.Lowercased(trimmed), "name:")) continue; // :872
            var name = FoundationText.Trim(SwiftString.DropFirst(trimmed, "name:".Length)); // :873
            return name.Length == 0 ? null : name; // :874
        }
        return null; // :876
    }

    // :380-425. Private in Swift; kept internal so this part's tests (PR-PROVAPI-088) can call it directly,
    // the same convention ClaudeSession.cs already uses for its own private-in-Swift members. Called by
    // Refresh (:362-364), a MARROW_STUB in this file left to a later part of this unit.
    internal async Task RefreshAPIProvider(ProviderKind provider)
    {
        var apiKey = settings.ApiKey(provider); // :382
        if (apiKey.Length == 0) // :383
        {
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); // :384
            return;
        }
        if (provider == ProviderKind.Deepseek) // :387
        {
            var valid = await DeepSeekAPI.Validate(apiKey); // :388
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :389-393
            {
                Version = "API",
                ProviderStatusAuth = valid ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.SignedOut(),
                ApiKeyConfigured = true,
            });
            // :396-401. The key just proved itself, so read the balance behind it right away: the usage
            // panel has it from launch, and a rejected key must not leave a stale one.
            if (valid)
            {
                RefreshCredits(ProviderKind.Deepseek, force: true); // :397
            }
            else
            {
                credits = credits.Mutating(d => d.Remove(ProviderKind.Deepseek)); // :399
                creditsFetchedAt.Remove(ProviderKind.Deepseek); // :400
            }
        }
        else if (provider == ProviderKind.Meta) // :402
        {
            var valid = await MetaAPI.Validate(apiKey); // :403
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :404-408
            {
                Version = "API",
                ProviderStatusAuth = valid ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.SignedOut(),
                ApiKeyConfigured = true,
            });
        }
        else if (provider == ProviderKind.Zai) // :409
        {
            var valid = await ZaiAPI.Validate(apiKey); // :410
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :411-415
            {
                Version = "API",
                ProviderStatusAuth = valid ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.SignedOut(),
                ApiKeyConfigured = true,
            });
            if (valid)
            {
                RefreshPlanLimits(ProviderKind.Zai, force: true); // :417
            }
            else
            {
                planLimits = planLimits.Mutating(d => d.Remove(ProviderKind.Zai)); // :419
                limitsFetchedAt.Remove(ProviderKind.Zai); // :420
            }
        }
        else // :422
        {
            statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :423
            {
                ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null),
                ApiKeyConfigured = true,
            });
        }
    }

    // :103-119 mergedACPCatalog. Private in Swift; kept internal so this part's tests (PR-PROVACP-139) can
    // call it directly.
    internal static ValueList<ModelOption> MergedACPCatalog(IReadOnlyList<ModelOption> live, IReadOnlyList<ModelOption> cached)
    {
        if (cached.Count == 0) return new ValueList<ModelOption>(live); // :109
        var known = new Dictionary<string, ModelOption>(SwiftStringComparer.Instance);
        foreach (var option in cached) known.TryAdd(option.Id, option); // :110, uniquingKeysWith: first wins
        return new ValueList<ModelOption>(live.Select(option =>
        {
            if (option.Efforts.Count != 0 || !known.TryGetValue(option.Id, out var kept)) return option; // :112
            var merged = option with { Efforts = kept.Efforts }; // :113-114
            if (merged.DefaultEffort is null) merged = merged with { DefaultEffort = kept.DefaultEffort }; // :115
            if (merged.FastTier is null) merged = merged with { FastTier = kept.FastTier }; // :116
            return merged;
        }));
    }

    // :541-559 openCodeModels. Private in Swift; kept internal so this part's tests (PR-PROVACP-048,
    // PR-PROVACP-139) can call it directly.
    internal static ValueList<ModelOption> OpenCodeModels(IReadOnlyList<ModelOption> list, ProviderKind provider)
    {
        const string GoPrefix = "opencode-go/"; // :544
        IEnumerable<ModelOption> half = provider switch // :545-549
        {
            ProviderKind.Opencode => list.Where(option => !SwiftString.HasPrefix(option.Id, GoPrefix)),
            ProviderKind.OpencodeGo => list.Where(option => SwiftString.HasPrefix(option.Id, GoPrefix)),
            _ => list,
        };
        // :551-552. The CLI names a row "OpenCode Go/DeepSeek V4 Flash"; the provider already says which of
        // the two it is, so the redundant half of the name goes.
        var namePrefix = provider.DisplayName + "/"; // :552
        return new ValueList<ModelOption>(half.Select(option =>
            SwiftString.HasPrefix(option.Name, namePrefix)
                ? option with { Name = SwiftString.DropFirst(option.Name, namePrefix.Length) } // :554-556
                : option));
    }

    // :429-431. Swift: models(for:).
    public ValueList<ModelOption> Models(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:429");

    // :433-440. Swift: model(_:for:).
    public ModelOption? Model(string? id, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:433");

    // :451-457. Swift: defaultModel(for:).
    public ModelOption? DefaultModel(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:451");

    // :459-517
    public Task LoadCatalog(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:459");

    // :606-641
    public void RefreshPlanLimits(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:606");

    // :650-676
    public Task<PlanLimits.ResetOutcome> ConsumeResetCredit(ProviderKind provider, string? creditID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:650");

    // :681-700
    public void RefreshCredits(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:681");

    // :704-707
    public void InvalidateCredits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:704");

    // :712-715
    public void InvalidatePlanLimits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:712");

    // :717-734. Swift: updateCatalog(_:for:).
    public void UpdateCatalog(ValueList<ModelOption> list, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:717");

    // :736-738
    public static string CommandKey(ProviderKind provider, string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:736");

    // :740-751. Swift: updateCommands(_:for:directory:).
    public void UpdateCommands(ValueList<SlashCommand> list, ProviderKind provider, string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:740");

    // :753-759. Swift: recordCommand(_:for:).
    public void RecordCommand(string name, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:753");

    // :762-784
    public Task LoadCommands(ProviderKind provider, string directory, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:762");
}
