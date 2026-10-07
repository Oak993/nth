using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Core.Support;
using Marrow.Services.MCP;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B3 52bc792), :1-958.
// Runtime writes use the observable setters (R-OBS-1/3/6). Constructor fields stay silent per Swift init.
// Historical part notes below retain B2 ranges. ProviderStatus's
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
// likewise kept internal. DeepseekSeed (PR-PROVREG-140) uses DeepSeekAPI.Option, which landed with unit
// Services.Providers.DeepSeekAPI. ZaiSeed and CommandcodeSeed (part 3, PR-PROVREG-142/145) likewise use
// their dependency units' implementations.

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
    // PORT-ONLY (ED-410): availability without a credential or executable.
    public bool LocalServerAvailable { get; init; }
    public string? LocalSummary { get; init; }

    // :17
    public bool IsInstalled => Executable is not null || ApiKeyConfigured || LocalServerAvailable;

    // U+00B7 MIDDLE DOT, used by Summary's :24 and ClaudeSeed's Fable detail (R-STR-9).
    private const string MiddleDot = "\U000000B7";

    // :19-26
    public string Summary
    {
        get
        {
            if (!IsInstalled) return "Not installed"; // :20
            if (LocalServerAvailable && LocalSummary is { } localSummary) return localSummary; // ED-410
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

// :30-958. @MainActor @Observable -> MainActorModel (R-ISO-1, R-OBS-6). Partial: [GeneratedRegex] members
// (R-RE-1) require the declaring class to be partial.
public sealed partial class ProviderRegistry : MainActorModel, ICliUpdateRunner
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

    // :53-54. Providers whose CLI is being updated right now (see UpdateCLIIfDue). B3.
    private ValueSet<ProviderKind> updatingCLIs = ValueSet<ProviderKind>.Empty;
    // :55. @ObservationIgnored. B3.
    private readonly string cliUpdatesKey = "providerCLIUpdateDates";

    // :47. @ObservationIgnored.
    private readonly ProviderSettings settings;
    // :48
    private readonly string cacheKey = "providerModelCatalogs";
    // :50
    private readonly string catalogDatesKey = "providerModelCatalogDates";
    // :51. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> catalogFetchedAt = new();

    // :157. Views that ask on appear must never re-spawn a CLI while scrolling: a provider that has
    // already delivered (or been probed unforced) this launch is not probed again without force.
    private readonly HashSet<ProviderKind> attemptedCatalogs = [];

    // :160. The single shared OpenCode probe (PR-PROVREG-150): Zen and Go come from one CLI session, so
    // a concurrent ask for either reuses the same in-flight task instead of spawning a second CLI.
    private TaskHandle<ValueList<ModelOption>?>? openCodeCatalogTask;
    // :61. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> limitsFetchedAt = new();
    // :63. @ObservationIgnored. A forced read that arrives while a read is out is remembered, not dropped.
    private readonly HashSet<ProviderKind> forcedLimitReads = [];
    // :68. @ObservationIgnored.
    private readonly Dictionary<ProviderKind, DateTimeOffset> creditsFetchedAt = new();
    // :39. @ObservationIgnored. When each (provider, directory) command list was last delivered.
    private readonly Dictionary<string, DateTimeOffset> commandsFetchedAt = new(StringComparer.Ordinal);
    // :38. @ObservationIgnored. An in-flight command load per (provider, directory) key; a second caller
    // for the same key awaits the first task's completion rather than spawning a second CLI (PR-PROVREG-217).
    private readonly Dictionary<string, TaskHandle> commandLoads = new(StringComparer.Ordinal);

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
    public ValueMap<ProviderKind, PlanLimits> PlanLimits { get => Read(planLimits); private set => Set(ref planLimits, value); } // :61
    public ValueSet<ProviderKind> LoadingLimits { get => Read(loadingLimits); private set => Set(ref loadingLimits, value); } // :62
    public string? RedeemingReset { get => Read(redeemingReset); private set => Set(ref redeemingReset, value); } // :64
    public ValueMap<ProviderKind, ProviderCredits> Credits { get => Read(credits); private set => Set(ref credits, value); } // :70
    public ValueSet<ProviderKind> LoadingCredits { get => Read(loadingCredits); private set => Set(ref loadingCredits, value); } // :71
    public bool IsRefreshing { get => Read(isRefreshing); private set => Set(ref isRefreshing, value); } // :289
    public ProviderKind? SigningIn { get => Read(signingIn); private set => Set(ref signingIn, value); } // :292
    public ValueSet<ProviderKind> UpdatingCLIs { get => Read(updatingCLIs); private set => Set(ref updatingCLIs, value); } // :53-54. B3.

    // :127. Built from the same DeepSeekAPI.option(for:) table used by the live fetch.
    public static ValueList<ModelOption> DeepseekSeed => new(new[] { "deepseek-v4-pro", "deepseek-flash" }
        .Select(DeepSeekAPI.Option)
        .OfType<ModelOption>());

    // :128-129. Built with MetaAPI.option(for:).
    public static ValueList<ModelOption> MetaSeed => new(new[]
        {
            "muse-spark-1.3", "muse-spark-1.3-contributor", "muse-spark-1.2", "muse-spark-1.2-contributor", "muse-spark-1.1",
        }
        .Select(MetaAPI.Option)
        .OfType<ModelOption>());

    // :130. Built with ZaiAPI.option(for:) (ZaiAPI.cs, PR-PROVREG-142): no longer MARROW_STUB, ZaiAPI.Option
    // is implemented.
    public static ValueList<ModelOption> ZaiSeed => new(new[] { "glm-5.3", "glm-5.3-flash" }
        .Select(ZaiAPI.Option)
        .OfType<ModelOption>());

    // PORT-ONLY (ED-234): OpenRouter's router row, so the provider has a default model before its list has loaded.
    public static ValueList<ModelOption> OpenRouterSeed => new([OpenRouterAPI.SeedOption]);

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

    // :150. CommandCodeAPI.seed (CommandCodeAPI.cs, PR-PROVREG-145): no longer MARROW_STUB, CommandCodeAPI.Seed
    // is implemented.
    public static ValueList<ModelOption> CommandcodeSeed => CommandCodeAPI.Seed;

    // :153
    public static readonly ValueList<ModelOption> PiSeed = new(new[]
    {
        new ModelOption("default", "Default", detail: "The model pi itself is set to",
            efforts: new(new[] { "off", "minimal", "low", "medium", "high", "xhigh", "max" }), defaultEffort: "medium", isDefault: true),
    });

    // :162-196. ED-001: CaptureRun is not ported (dead in a shipped launch); the non-capture branch is the
    // only one implemented. :164 (recentCommands), :165-178 (cached catalogs), :179-185 (cached dates) and
    // :186-195 (seeding) each read `!CaptureRun.isEnabled` in the source and are ported without that guard.
    // PORT-ONLY: retain the Local discovery injection used by foundation tests.
    public ProviderRegistry(ProviderSettings settings, Marrow.Services.Port.Local.LocalDiscovery local)
        : this(settings, accounts: null, accountDataRoot: null, local: local) { }

    public ProviderRegistry(ProviderSettings settings, AccountStore? accounts = null, string? accountDataRoot = null, Marrow.Services.Port.Local.LocalDiscovery? local = null)
    {
        this.settings = settings; // :163
        // PORT-ONLY: OD-0114 / ED-404. Existing fixtures can retain the provider-only contract.
        var configuredAccounts = accounts ?? (settings as AccountProviderSettings)?.Accounts;
        accountStore = configuredAccounts is { SaveBlocked: false } ? configuredAccounts : null;
        recoveredAccounts = configuredAccounts is { SaveBlocked: true } ? configuredAccounts : null;
        accountEnvironmentSource = accountStore is null ? null : new(() => new AccountEnvironment(accountDataRoot ?? Marrow.Services.Store.Storage.Root));
        InitializeLocal(local); // PORT-ONLY (ED-410)

        // :164
        if (UserDefaults.Standard.Current.Dictionary("recentSlashCommands") is { } storedRecent)
        {
            // :172 `as? [String: [String]] ?? [:]` is one conditional cast of the whole dictionary: an entry that is not an
            // array of strings fails it, and every entry (the valid ones too) is dropped.
            var recent = new Dictionary<string, ValueList<string>>();
            var castHolds = true;
            foreach (var (key, value) in storedRecent)
            {
                if (value is IReadOnlyList<object> list && list.All(item => item is string))
                    recent[key] = new ValueList<string>(list.Cast<string>());
                else castHolds = false;
            }
            if (castHolds) recentCommands = new ValueMap<string, ValueList<string>>(recent, SwiftStringComparer.Instance);
        }

        // :165-178
        if (UserDefaults.Standard.Current.Data(cacheKey) is { } cacheData && DecodeCatalogCache(cacheData) is { } cachedCatalogs)
        {
            foreach (var (key, list) in cachedCatalogs)
            {
                if (SwiftEnum<ProviderKind>.FromRawValue(key) is not { } provider || provider == ProviderKind.Local) continue; // :169; Local needs a fresh endpoint binding
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
        // PORT-ONLY (ED-234): the same for OpenRouter.
        if (!(catalogs.TryGetValue(ProviderKind.OpenRouter, out var openRouterList) && openRouterList.Count > 0))
            catalogs = catalogs.Mutating(d => d[ProviderKind.OpenRouter] = OpenRouterSeed);
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
        statuses.TryGetValue(provider, out var status) ? status.IsInstalled : provider != ProviderKind.Local; // :207; ED-410: only show Local when found

    // :210-217. ED-001: the CaptureRun branch (:213-215) is dead in a shipped launch and not ported.
    public ProviderStatus Status(ProviderKind provider) =>
        statuses.TryGetValue(provider, out var status) ? status : new ProviderStatus(); // :216

    // :219-226. Swift: executable(for:). The testCLIUpdaterExecutable seam (below, B3) is consulted first so
    // that a test proving UpdateCLIIfDue's success path (which itself calls Refresh and LoadCatalog, each
    // reaching Executable(for:) again) never lets a Claude or Codex resolution fall through to
    // LoginEnvironment.Which's real PATH search (R-TEST-10). CAUTION: when a seam IS active (AsyncLocal.Value
    // is a delegate, even one that answers null for this provider), its answer is FINAL and never falls
    // through to ResolveExecutable: `resolver(provider) ?? ResolveExecutable(...)` would be wrong here, since
    // a delegate correctly answering "no executable" for this test's provider is indistinguishable from "no
    // seam is active" once both are null, and would silently re-enable the real PATH search this seam
    // exists to block. Only the absence of any delegate (no test has called Observe) falls through.
    public SwiftURL? Executable(ProviderKind provider)
    {
        if (testCLIUpdaterExecutable.Value is { } resolver) return resolver(provider);
        return ResolveExecutable(provider) is { } path ? FileUrl(path) : null;
    }

    // The plain-path resolution :219-226 works in, returning a raw path so Environment(for:) needs no
    // URL round trip to find the executable's directory.
    private string? ResolveExecutable(ProviderKind provider)
    {
        if (provider.IsAPIKeyBased || provider == ProviderKind.Local) return null; // :220; ED-410
        var custom = settings.BinaryPath(provider); // :221
        if (custom.Length == 0 && provider.ExecutableName.Length == 0) return null; // :222
        var primary = custom.Length == 0 ? provider.ExecutableName : custom;
        if (LoginEnvironment.Which(primary) is { } found && !RejectsAsCommandInterpreter(provider, found) && AllowedForTesting(found)) return found; // :223
        if (custom.Length != 0) return null; // :224
        foreach (var candidate in FallbackExecutables(provider)) // :225
        {
            if (LoginEnvironment.Which(candidate) is { } resolved && !RejectsAsCommandInterpreter(provider, resolved) && AllowedForTesting(resolved)) return resolved;
        }
        return null;
    }

    // PORT-ONLY test guard, no Swift twin (R-TEST-10; ED-211). Null in production, so it changes nothing there. Each
    // test assembly that can reach this registry sets it once from a module initializer, before any test runs, to the
    // folders a test may legitimately launch a provider CLI from: the repository (tests/fixtures stand-ins) and the
    // temp / MARROW_PORT_DATA_DIR scratch folders. A provider executable resolved anywhere else, i.e. a real installed
    // claude/codex/copilot/... under the user's profile, is then treated as not installed: it FAILS CLOSED, so no test
    // can reach a real CLI through a harness that forgot its own override (2026-09-27: AppModelHydraMergeTests generated
    // commit messages through the owner's real signed-in CLI this way). Process-wide on purpose, unlike the AsyncLocal
    // seams: it must cover every test flow, including ones that install no seam at all.
    public static IReadOnlyList<string>? ExecutableRootsForTesting { get; set; }

    private static bool AllowedForTesting(string path)
    {
        if (ExecutableRootsForTesting is not { } roots) return true;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return false; }
        foreach (var root in roots)
        {
            var prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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
    private static IReadOnlyList<string> FallbackExecutables(ProviderKind provider) =>
        FallbackExecutables(provider, LoginEnvironment.Current, LoginEnvironment.HomeDirectory);

    // Internal overload taking environment and home explicitly (mirrors LoginEnvironment.Which's own
    // internal overload): a test seam for PR-PROVREG-095 to 098 that needs no process-global environment
    // mutation, kept internal for direct testing (InternalsVisibleTo Marrow.Services.Tests).
    internal static IReadOnlyList<string> FallbackExecutables(
        ProviderKind provider, IReadOnlyDictionary<string, string> environment, string home)
    {
        switch (provider)
        {
            case ProviderKind.Copilot: // :232-237
            {
                var dataDirectory = NonEmpty(environment, "GH_DATA_DIR")
                    ?? (NonEmpty(environment, "XDG_DATA_HOME") is { } xdg ? SwiftPath.AppendingPathComponent(xdg, "gh") : null)
                    ?? SwiftPath.AppendingPathComponent(home, ".local/share/gh");
                return [SwiftPath.AppendingPathComponent(dataDirectory, "copilot/copilot")];
            }
            case ProviderKind.Commandcode: // :238-251
            {
                var candidates = new List<string> { "command-code", "commandcode", "cmd" }; // :243-245
                foreach (var directory in new[] { $"{home}/.local/bin", "/opt/homebrew/bin", "/usr/local/bin", $"{home}/.npm-global/bin", $"{home}/.volta/bin" }) // :246
                {
                    foreach (var name in new[] { "command-code", "cmd" }) candidates.Add(SwiftPath.AppendingPathComponent(directory, name)); // :247-249
                }
                return candidates;
            }
            case ProviderKind.Pi: // :252-254
                return
                [
                    $"{home}/.npm-global/bin/pi", "/opt/homebrew/bin/pi", "/usr/local/bin/pi",
                    $"{home}/.local/bin/pi", $"{home}/.volta/bin/pi", $"{home}/.bun/bin/pi",
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
    public ValueMap<string, string> Environment(ProviderKind provider) => accountStore is null
        ? LegacyEnvironment(provider) : Environment(RequireAccount(provider).Id);

    private ValueMap<string, string> LegacyEnvironment(ProviderKind provider)
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

    // PORT-ONLY: SK-11d / WU-9. The environment of an OpenCode chat that has a locked skills generation. A null launch, or any
    // other provider, returns Environment(provider) unchanged.
    public ValueMap<string, string> Environment(ProviderKind provider, Marrow.Services.Port.Skills.Providers.OpenCodeSkillLaunch? skills)
    {
        var environment = Environment(provider);
        if (skills is null || (provider != ProviderKind.Opencode && provider != ProviderKind.OpencodeGo)) return environment;
        return new ValueMap<string, string>(Marrow.Services.Port.Skills.Providers.OpenCodeSkillWiring.Apply(environment, skills), StringComparer.OrdinalIgnoreCase);
    }

    // :286-310. PR-PROVREG-234, PR-PROVREG-235. Signs a CLI provider in from the app: the provider's own login
    // command, then a fresh status check and usage read. Returns the CLI's message when it failed, null on
    // success. The pinned checkout has no caller of this method and no reader of SigningIn (neither is in
    // dead_code.md), so nothing in the app reaches it; it is kept so the behaviour is there if a caller is found.
    public async Task<string?> SignIn(ProviderKind provider)
    {
        if (accountStore is not null) return await SignIn(RequireAccount(provider).Id);
        if (signingIn is not null) return null; // :290
        if (provider.IsAPIKeyBased) return null; // :291
        SigningIn = provider; // :292
        try
        {
            await LoginEnvironment.Load(); // :294
            if (Executable(provider) is not { } executable) return $"{provider.DisplayName} was not found."; // :295
            // :297. The login command starts with the executable's own name; the rest are its arguments.
            var arguments = provider.LoginCommand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
            var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Shell.Run(executable.Path, arguments, environment: Environment(provider), timeout: 300)); // :298
            if (result is { Succeeded: true }) // :299
            {
                await Refresh(provider); // :300
                RefreshPlanLimits(provider, force: true); // :301
                await LoadCatalog(provider, force: true); // :302
                return null; // :303
            }
            var output = result is null ? "" : TextCleanup.StripANSI(result.Output + result.ErrorOutput); // :306
            var lines = SwiftString.Split(output, "\n"); // :307 split(separator: newline, omittingEmptySubsequences: true)
            if (lines.Count == 0 || lines[^1].Length == 0) return $"{provider.DisplayName} did not finish signing in."; // :307-309
            return lines[^1]; // :310
        }
        finally
        {
            SigningIn = null; // :293 defer
        }
    }

    // :315-333. PR-PROVREG-131, PR-PROVREG-132. Single-flight (:316-318), then three phases in order:
    // refreshAll, then a forced limits read for every provider not known to be signed out (:321-326,
    // NOT credits: the comment at :320 notes credits were already re-read in refreshAPIProvider), then a
    // task group forcing loadCatalog for every installed provider (:328-331), unbounded in concurrency
    // unlike the capped refreshAll.
    public async Task RefreshEverything()
    {
        if (isRefreshing) return; // :316
        IsRefreshing = true; // :325
        try
        {
            await RefreshAll(); // :319
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases) // :321-323
            {
                if (Status(provider).ProviderStatusAuth is not ProviderStatus.Auth.SignedOut)
                    RefreshPlanLimits(provider, force: true); // :322
            }
            await TaskGroup.Run(async group => // :328-331
            {
                foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
                {
                    if (Status(provider).IsInstalled)
                        group.AddTask(() => MainActor.Invoke(() => LoadCatalog(provider, force: true)));
                }
            });
        }
        finally
        {
            IsRefreshing = false; // :326 (defer)
        }
    }

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
        if (!HasEnabledAccount(provider))
        {
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() });
            return;
        }
        if (provider == ProviderKind.Local) { await RefreshLocal(); return; } // ED-410
        var status = statuses.TryGetValue(provider, out var existing) ? existing : new ProviderStatus(); // :359
        status = status with { IsChecking = true }; // :360
        Statuses = statuses.Mutating(d => d[provider] = status); // :369
        if (provider.IsAPIKeyBased) // :362
        {
            await RefreshAPIProvider(provider); // :363
            return; // :364
        }
        await LoginEnvironment.Load(); // :366
        var executable = Executable(provider); // :367
        if (executable is null)
        {
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus()); // :376
            return; // :369
        }
        var environment = Environment(provider); // :371
        // :372-374. Both probes spawn the CLI: run one after the other, not together.
        var version = await Version(executable.Path, environment); // :375
        var auth = await Auth(provider, executable, environment); // :376
        Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :385
        {
            Executable = executable,
            Version = version,
            ProviderStatusAuth = auth,
        });
    }

    // PORT-ONLY (owner finding F2, 2026-10-08): after a Login opened in the terminal, Marrow re-checks the provider's sign-in
    // on its own (Droppy's Settings needs a press of Check providers again). Quiet: no IsChecking state, so the row does not
    // flicker while it waits, and the status is written only when the sign-in or the executable actually changed. Returns
    // whether the provider is now signed in.
    public async Task<bool> RecheckSignIn(ProviderKind provider)
    {
        if (provider.IsAPIKeyBased || provider == ProviderKind.Local || !HasEnabledAccount(provider)) return false;
        await LoginEnvironment.Load();
        if (Executable(provider) is not { } executable) return false;
        var environment = Environment(provider);
        var auth = await Auth(provider, executable, environment);
        var current = Status(provider);
        if (current.ProviderStatusAuth.GetType() != auth.GetType() || current.Executable != executable)
        {
            var version = await Version(executable.Path, environment);
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus { Executable = executable, Version = version, ProviderStatusAuth = auth });
        }
        if (auth is not ProviderStatus.Auth.SignedIn) return false;
        RefreshPlanLimits(provider, force: true); // as signIn(_:) does after a successful login (ProviderRegistry.swift:308-310)
        await LoadCatalog(provider, force: true);
        return true;
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
                var path = environment.TryGetValue("GROK_HOME", out var grokHome) && grokHome.Length != 0
                    ? SwiftPath.AppendingPathComponent(grokHome, "auth.json")
                    : testGrokAuthPath.Value ?? SwiftPath.AppendingPathComponent(LoginEnvironment.HomeDirectory, ".grok/auth.json");
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
            case ProviderKind.OpenRouter: // ED-234
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
    private static readonly AsyncLocal<string?> testGrokAuthPath = new();

    internal static void SetGrokAuthPathForTesting(string path) => testGrokAuthPath.Value = path;

    internal static void ResetGrokAuthPathForTesting() => testGrokAuthPath.Value = null;

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
        var apiKey = SelectedApiKey(provider); // :382
        if (apiKey.Length == 0) // :383
        {
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); // :392
            return;
        }
        if (provider == ProviderKind.Deepseek) // :387
        {
            var valid = await DeepSeekAPI.Validate(apiKey); // :388
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :397-401
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
                Credits = credits.Mutating(d => d.Remove(ProviderKind.Deepseek)); // :407
                creditsFetchedAt.Remove(ProviderKind.Deepseek); // :400
            }
        }
        else if (provider == ProviderKind.Meta) // :402
        {
            var valid = await MetaAPI.Validate(apiKey); // :403
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :412-416
            {
                Version = "API",
                ProviderStatusAuth = valid ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.SignedOut(),
                ApiKeyConfigured = true,
            });
        }
        else if (provider == ProviderKind.Zai) // :409
        {
            var valid = await ZaiAPI.Validate(apiKey); // :410
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :419-423
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
                PlanLimits = planLimits.Mutating(d => d.Remove(ProviderKind.Zai)); // :427
                limitsFetchedAt.Remove(ProviderKind.Zai); // :420
            }
        }
        else if (provider == ProviderKind.OpenRouter) // PORT-ONLY (ED-234), shaped like the Meta branch
        {
            var valid = await OpenRouterAPI.Validate(apiKey);
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus
            {
                Version = "API",
                ProviderStatusAuth = valid ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.SignedOut(),
                ApiKeyConfigured = true,
            });
        }
        else // :422
        {
            Statuses = statuses.Mutating(d => d[provider] = new ProviderStatus // :431
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

    // :429-431. Swift: models(for:). PR-PROVREG-147: the empty array, never null, for a provider with no
    // catalog.
    // R-VIEW: read through the observed Catalogs property (:34 an @Observable var), so a view that lists the models (the model
    // list's "Loading models…" line, the chip, the slider card) is drawn again when the catalogue arrives; the field alone read
    // as a view's dependency never was one.
    public ValueList<ModelOption> Models(ProviderKind provider) =>
        Catalogs.TryGetValue(provider, out var list) ? list : ValueList<ModelOption>.Empty;

    // :433-440. Swift: model(_:for:). PR-PROVREG-148: exact match first (so a bracketed seed id like the
    // Claude Fable row still resolves to itself), then the part before the first bracket; only for
    // provider .opencode with an "opencode-go/" id does a miss fall back to the OpenCode Go catalog,
    // one-way only.
    public ModelOption? Model(string? id, ProviderKind provider)
    {
        if (id is null) return null; // :434
        if (Match(id, Models(provider)) is { } match) return match; // :435
        if (provider != ProviderKind.Opencode || !SwiftString.HasPrefix(id, "opencode-go/")) return null; // :436-437
        return Match(id, Models(ProviderKind.OpencodeGo)); // :438-439
    }

    // :442-449. Private in Swift; kept private since only Model(_:for:) calls it.
    private static ModelOption? Match(string id, IReadOnlyList<ModelOption> list)
    {
        foreach (var option in list) // :443-444
            if (option.Id == id) return option;
        var bracket = id.IndexOf('[', StringComparison.Ordinal); // :446
        if (bracket < 0) return null;
        var baseId = id[..bracket]; // :447
        foreach (var option in list) // :448
            if (option.Id == baseId) return option;
        return null; // :449
    }

    // :451-457. PR-PROVREG-087: the last model wins when it resolves, then the first default, then the first row.
    public ModelOption? DefaultModel(ProviderKind provider)
    {
        var list = Models(provider); // :452
        if (settings.LastModel(provider) is { } last && Model(last, provider) is { } match) // :453-455
            return match;
        return list.FirstOrDefault(option => option.IsDefault) ?? list.FirstOrDefault(); // :456-457
    }

    // MARK: - Keeping the CLIs current (B3, new). PR-PROVREG-251 to 258.

    // :474. 12 hours.
    private static readonly TimeSpan CliUpdateInterval = TimeSpan.FromHours(12);

    // :476-479. Swift: updatesOwnCLI(_:). The providers whose CLI carries its own updater (`claude update`,
    // `codex update`). Review fix (finding #4, MEDIUM): public per R-NAME-6, not internal - the Swift caller
    // (ThreadRuntime.swift:2260) is App/Runtime/ThreadRuntime.cs, a different assembly (Marrow.App), which
    // Marrow.Services grants no InternalsVisibleTo to (only Marrow.Services.Tests).
    public static bool UpdatesOwnCLI(ProviderKind provider) => provider == ProviderKind.Claude || provider == ProviderKind.Codex;

    // :482-490. Swift: needsNewerCLI(_:). nonisolated: no MainActor state is touched. Review fix (finding #4,
    // MEDIUM): public per R-NAME-6, matching UpdatesOwnCLI above - ThreadRuntime.swift:2261 calls this too.
    public static bool NeedsNewerCLI(string message)
    {
        var text = SwiftString.Lowercased(message);
        return text.Contains("or newer is required", StringComparison.Ordinal)
            || text.Contains("requires a newer version", StringComparison.Ordinal)
            || text.Contains("claude update", StringComparison.Ordinal)
            || text.Contains("codex update", StringComparison.Ordinal)
            || text.Contains("upgrade codex", StringComparison.Ordinal)
            || text.Contains("update codex", StringComparison.Ordinal);
    }

    // PORT-ONLY test seam, no Swift twin (R-TEST-10): overrides what Executable(for:) resolves to for a
    // provider (see Executable(ProviderKind) above). ExecutableName for Claude and Codex (the only two
    // UpdatesOwnCLI providers) is the real CLI name ("claude"/"codex"), so without this seam a test of
    // UpdateCLIIfDue's success path (which itself calls Refresh and LoadCatalog) would let
    // LoginEnvironment.Which search this machine's real PATH and could reach, and run "update" against, a
    // real installed CLI. AsyncLocal (matching ShellObserver's convention, ED-207), not a plain static:
    // xUnit runs test classes in parallel, and this way a test's override reaches only its own async flow
    // and the flows it starts, never a concurrently running test's. Unset (null) in production and in every
    // test that does not call Observe, which changes nothing.
    private static readonly AsyncLocal<Func<ProviderKind, SwiftURL?>?> testCLIUpdaterExecutable = new();

    // public per R-NAME-6, not internal, matching UpdatesOwnCLI/NeedsNewerCLI above (finding #4): the B3
    // App.Runtime.ThreadRuntime unit (A33) tests ThreadRuntime.FinishTurnUpdatingCLI/ResendAfterCLIUpdate's own
    // orchestration from Marrow.App.Tests, a different assembly Marrow.Services grants no InternalsVisibleTo
    // to (only Marrow.Services.Tests); those tests need this same R-TEST-10 seam to reach it safely.
    public static IDisposable SetCLIUpdaterExecutableForTesting(Func<ProviderKind, SwiftURL?> resolver)
    {
        var previous = testCLIUpdaterExecutable.Value;
        testCLIUpdaterExecutable.Value = resolver;
        return new TestSeamRestorer(() => testCLIUpdaterExecutable.Value = previous);
    }

    private sealed class TestSeamRestorer(Action restore) : IDisposable
    {
        private Action? pending = restore;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }

    // :492-521. Swift: updateCLIIfDue(_:force:). PR-PROVREG-253 to 256: runs the CLI's own updater when one is
    // due (every 12 hours) or forced, and when a newer version went in, reads the provider and its catalog
    // again so the models that version brings show at once. ED-001: the CaptureRun guard (:498) is dropped,
    // matching this file's existing convention (see the constructor's comment at :193-195).
    public async Task<bool> UpdateCLIIfDue(ProviderKind provider, bool force = false)
    {
        if (!UpdatesOwnCLI(provider) || settings.BinaryPath(provider).Length != 0) return false; // :498
        // An update already under way is not run twice; a forced one waits for it and then runs, which is
        // quick when that one already brought the newest version.
        if (updatingCLIs.Contains(provider)) // :500
        {
            if (!force) return false; // :501
            while (updatingCLIs.Contains(provider)) await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(500)); // :503
        }
        var dates = ReadCliUpdateDates(); // :505
        if (!force && dates.TryGetValue(SwiftEnum<ProviderKind>.RawValue(provider), out var last)
            && SwiftDate.Seconds1970(SwiftDate.Now) - last < CliUpdateInterval.TotalSeconds) return false; // :506-507
        await LoginEnvironment.Load(); // :508
        if (Executable(provider) is not { } executable) return false; // :509
        // :510-511. Observable set mutations use Set, including isolation and change notification.
        UpdatingCLIs = updatingCLIs.Mutating(s => s.Add(provider)); // :510
        try
        {
            var environment = LegacyEnvironment(provider); // :512
            var before = await CliVersion(executable.Path, environment); // :513
            _ = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Shell.Run(executable.Path, ["update"], @in: Path.GetTempPath(), environment: environment, timeout: 300)); // :514
            dates[SwiftEnum<ProviderKind>.RawValue(provider)] = SwiftDate.Seconds1970(SwiftDate.Now); // :515
            WriteCliUpdateDates(dates); // :516
            var after = await CliVersion(executable.Path, environment); // :517
            if (after is null || after == before) return false; // :518
            await Refresh(provider); // :519
            await LoadCatalog(provider, force: true); // :520
            return true; // :521
        }
        finally
        {
            UpdatingCLIs = updatingCLIs.Mutating(s => s.Remove(provider)); // :511 defer
        }
    }

    // PORT-ONLY (UE-2, BUILD-PLAN.md 2.4; OD-0100 "Update everything"). The same run as UpdateCLIIfDue, exposed with a result for the update orchestrator: no 12-hour gate (the user asked),
    // a run already under way is waited for as a forced one is (:500-503), the attempt date is written as :515-516 does, and what happened is returned instead of a bool. UpdateCLIIfDue
    // itself is untouched, so the background updates are unchanged. ENGINEERING DECISION (ED-308): the updater's exit status is read here (UpdateCLIIfDue ignores it, :514), so
    // a failed updater whose version did not move is a Failed result.
    public async Task<CliUpdateResult> UpdateCLI(ProviderKind provider)
    {
        if (!UpdatesOwnCLI(provider) || settings.BinaryPath(provider).Length != 0) return new CliUpdateResult(CliUpdateStatus.Skipped, null, null);
        while (updatingCLIs.Contains(provider)) await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(500));
        await LoginEnvironment.Load();
        if (Executable(provider) is not { } executable) return new CliUpdateResult(CliUpdateStatus.NotInstalled, null, null);
        UpdatingCLIs = updatingCLIs.Mutating(s => s.Add(provider));
        try
        {
            var environment = LegacyEnvironment(provider);
            var before = await CliVersion(executable.Path, environment);
            var ran = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Shell.Run(executable.Path, ["update"], @in: Path.GetTempPath(), environment: environment, timeout: 300));
            var dates = ReadCliUpdateDates();
            dates[SwiftEnum<ProviderKind>.RawValue(provider)] = SwiftDate.Seconds1970(SwiftDate.Now);
            WriteCliUpdateDates(dates);
            var after = await CliVersion(executable.Path, environment);
            if (after is not null && after != before)
            {
                await Refresh(provider);
                await LoadCatalog(provider, force: true);
                return new CliUpdateResult(CliUpdateStatus.Updated, before, after);
            }
            return new CliUpdateResult(ran is { Succeeded: true } && after is not null ? CliUpdateStatus.Unchanged : CliUpdateStatus.Failed, before, after);
        }
        finally
        {
            UpdatingCLIs = updatingCLIs.Mutating(s => s.Remove(provider));
        }
    }

    /// <summary>The providers whose CLI carries its own updater: the ones "Update everything" gets a row for.</summary>
    public static IReadOnlyList<ProviderKind> UpdatableProviders { get; } = Enum.GetValues<ProviderKind>().Where(UpdatesOwnCLI).ToList();

    // :505/:516. UserDefaults.standard.dictionary(forKey: cliUpdatesKey) as? [String: Double] ?? [:]. Swift's
    // conditional cast fails for the WHOLE dictionary when any one value cannot bridge to Double, not just
    // that entry, so a stored value of another type is treated as no dates at all.
    private Dictionary<string, double> ReadCliUpdateDates()
    {
        var empty = new Dictionary<string, double>(StringComparer.Ordinal);
        if (UserDefaults.Standard.Current.Dictionary(cliUpdatesKey) is not { } raw) return empty;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            if (Plist.AsDouble(value) is not { } number) return empty;
            result[key] = number;
        }
        return result;
    }

    private void WriteCliUpdateDates(Dictionary<string, double> dates)
    {
        var boxed = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in dates) boxed[key] = value;
        UserDefaults.Standard.Current.Set(cliUpdatesKey, boxed);
    }

    // :524-529. Swift: installedCLIVersion(_:). The version the installed CLI reports, or nil when it cannot
    // be run.
    public async Task<string?> InstalledCLIVersion(ProviderKind provider)
    {
        await LoginEnvironment.Load(); // :526
        if (Executable(provider) is not { } executable) return null; // :527
        return await CliVersion(executable.Path, LegacyEnvironment(provider)); // :528
    }

    // :531-536. Swift: cliVersion(_:environment:). Kept internal so this part's tests can call it directly,
    // the same convention this file already uses for Version/Auth.
    internal static async Task<string?> CliVersion(string executable, IReadOnlyDictionary<string, string> environment)
    {
        var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
            await Shell.Run(executable, ["--version"], environment: environment, timeout: 30)); // :531-532
        if (result is not { Succeeded: true }) return null;
        var version = result.TrimmedOutput; // :533
        return version.Length == 0 ? null : version; // :534
    }

    // :459-517. PR-PROVREG-149 to PR-PROVREG-153: single-flight per provider, the once-per-launch attempt
    // gate, the seeded/stale test, the nine-arm dispatch table, and the OpenCode sibling delivery.
    public async Task LoadCatalog(ProviderKind provider, bool force = false)
    {
        if (!HasEnabledAccount(provider)) return; // OD-0114 / ED-404: Off accounts cannot be probed.
        if (provider == ProviderKind.Local) { await RefreshLocal(); return; } // ED-410
        if (loadingCatalogs.Contains(provider)) return; // :460
        if (!force && attemptedCatalogs.Contains(provider)) return; // :461
        if (provider.IsAPIKeyBased) // :462-464
        {
            await LoadAPICatalog(provider, force);
            return;
        }
        var current = Models(provider);
        var seeded = provider == ProviderKind.Claude || provider == ProviderKind.Codex // :472
            || (provider == ProviderKind.Copilot && current.Equals(new ValueList<ModelOption>([CopilotAuto]))) // :473
            || (provider == ProviderKind.Commandcode && current.Equals(CommandcodeSeed)) // :474
            || (provider == ProviderKind.Pi && current.Equals(PiSeed)) // :475
            || ((provider == ProviderKind.Opencode || provider == ProviderKind.OpencodeGo) && LacksEffortWalk(current)); // :476
        var stale = catalogFetchedAt.TryGetValue(provider, out var fetched)
            ? SwiftDate.Now - fetched > TimeSpan.FromSeconds(21600) // :477, 6 * 3600
            : current.Count != 0; // :477 - undated non-empty list counts as stale
        if (!force && !seeded && !stale && current.Count != 0) return; // :478
        LoadingCatalogs = loadingCatalogs.Mutating(s => s.Add(provider)); // :558
        try
        {
            await LoginEnvironment.Load(); // :481
            if (Executable(provider) is not { } executable) return; // :482
            var environment = Environment(provider); // :483
            ValueList<ModelOption>? raw = provider switch // :484-494
            {
                ProviderKind.Claude => await LoadClaudeCatalog(executable, environment), // :485
                ProviderKind.Codex => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await CodexSession.ListModels(executable, environment)), // :486
                ProviderKind.Antigravity => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await AntigravitySession.ListModels(executable, environment)), // :487
                ProviderKind.Copilot => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await CopilotSession.ListModels(executable, environment)), // :488
                ProviderKind.Commandcode => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await CommandCodeAPI.ListModels(executable, environment)), // :489
                ProviderKind.Pi => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await PiCLI.ListModels(executable, environment)), // :490
                ProviderKind.Cursor or ProviderKind.Grok or ProviderKind.Devin =>
                    await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await ACPSession.ProbeModels(provider, executable, environment)), // :491
                ProviderKind.Opencode or ProviderKind.OpencodeGo => await OpenCodeCatalog(force, executable, environment), // :492
                ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter => null, // :493
                _ => throw new UnreachableException(),
            };
            var list = raw is null ? null : OpenCodeModels(raw, provider); // :495
            if (list is { Count: > 0 })
            {
                UpdateCatalog(list, provider); // :496-498
                catalogFetchedAt[provider] = SwiftDate.Now; // :499
                StoreCatalogDates(); // :500
                // :501-503: only a delivered catalog closes the attempt; a probe that came back with
                // nothing is tried again on the next ask.
                attemptedCatalogs.Add(provider);
                if (raw is not null && (provider == ProviderKind.Opencode || provider == ProviderKind.OpencodeGo)) // :504-515
                {
                    foreach (var kind in new[] { ProviderKind.Opencode, ProviderKind.OpencodeGo })
                    {
                        if (kind == provider) continue;
                        var sibling = OpenCodeModels(raw, kind);
                        if (sibling.Count == 0) continue;
                        UpdateCatalog(sibling, kind);
                        catalogFetchedAt[kind] = SwiftDate.Now;
                        attemptedCatalogs.Add(kind);
                    }
                    StoreCatalogDates();
                }
            }
        }
        finally
        {
            LoadingCatalogs = loadingCatalogs.Mutating(s => s.Remove(provider)); // :559 (defer)
        }
    }

    // :519-523. Private in Swift; kept private since only LoadCatalog calls it (used directly by its own
    // tests through LoadCatalog's observable seeded/stale behavior, PR-PROVREG-151).
    private static bool LacksEffortWalk(IReadOnlyList<ModelOption> list) =>
        list.Count(option => option.Efforts.Count != 0) <= 1;

    // :527-539. Shared by Zen and Go: one CLI session probes as .opencode; a concurrent or later unforced
    // ask reuses the same in-flight task. force clears any stored task first; a nil result (the probe
    // threw, swallowed by ProbeModels' try? equivalent below) clears the memo so the next ask probes again.
    internal Task<ValueList<ModelOption>?> OpenCodeCatalog(bool force, SwiftURL executable, ValueMap<string, string> environment)
    {
        if (force) openCodeCatalogTask = null; // :528
        var task = openCodeCatalogTask ?? MainActor.Spawn(async () => // :529-534
            await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await ACPSession.ProbeModels(ProviderKind.Opencode, executable, environment)));
        openCodeCatalogTask = task;
        return Finish(task);

        async Task<ValueList<ModelOption>?> Finish(TaskHandle<ValueList<ModelOption>?> handle)
        {
            var list = await handle.Value; // :535
            if (list is null) openCodeCatalogTask = null; // :536-537
            return list; // :538
        }
    }

    // :561-566. Private in Swift. The handshake runs somewhere: the models are the same from any folder.
    internal static async Task<ValueList<ModelOption>?> LoadClaudeCatalog(SwiftURL executable, ValueMap<string, string> environment) =>
        await SwiftTry.OptionalAsync(async () =>
        {
            var (models, _) = await ClaudeSession.Handshake(executable, FileUrl(System.IO.Path.GetTempPath()), environment);
            return (ValueList<ModelOption>?)models;
        });

    // :567-591. Private in Swift. The seed keeps API providers usable offline; a live fetch only refines it.
    private async Task LoadAPICatalog(ProviderKind provider, bool force)
    {
        ValueList<ModelOption>? seed = provider switch // :569-573
        {
            ProviderKind.Deepseek => DeepseekSeed,
            ProviderKind.Meta => MetaSeed,
            ProviderKind.Zai => ZaiSeed,
            ProviderKind.OpenRouter => OpenRouterSeed, // PORT-ONLY (ED-234)
            _ => null,
        };
        if (seed is null) return; // :574
        if (Models(provider).Count == 0) UpdateCatalog(seed, provider); // :576
        LoadingCatalogs = loadingCatalogs.Mutating(s => s.Add(provider)); // :656
        try
        {
            var apiKey = SelectedApiKey(provider); // :579
            if (apiKey.Length == 0) return; // :580
            attemptedCatalogs.Add(provider); // :581
            ValueList<ModelOption>? list = provider switch // :582-586
            {
                ProviderKind.Deepseek => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await DeepSeekAPI.ListModels(apiKey)),
                ProviderKind.Meta => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await MetaAPI.ListModels(apiKey)),
                ProviderKind.Zai => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await ZaiAPI.ListModels(apiKey)),
                ProviderKind.OpenRouter => await SwiftTry.OptionalAsync(async () => (ValueList<ModelOption>?)await OpenRouterAPI.ListModels(apiKey)), // PORT-ONLY (ED-234)
                _ => null,
            };
            if (list is { Count: > 0 }) // :588
            {
                UpdateCatalog(list, provider); // :589
                catalogFetchedAt[provider] = SwiftDate.Now; // :590
                StoreCatalogDates(); // :591
            }
        }
        finally
        {
            LoadingCatalogs = loadingCatalogs.Mutating(s => s.Remove(provider)); // :657 (defer)
        }
    }

    // :597-602. Private in Swift; kept private, only LoadCatalog/LoadAPICatalog call it. Persists when
    // each provider last delivered a list, seconds since 1970, key "providerModelCatalogDates".
    private void StoreCatalogDates()
    {
        var raw = new Dictionary<string, double>();
        foreach (var (provider, date) in catalogFetchedAt) raw[SwiftEnum<ProviderKind>.RawValue(provider)] = SwiftDate.Seconds1970(date); // :599-600
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in raw) writer.WriteNumber(key, value);
            writer.WriteEndObject();
        }
        UserDefaults.Standard.Current.Set(catalogDatesKey, stream.ToArray()); // :601
    }

    // :90-106 mergedClaudeCatalog (B3). Private in Swift; kept private, only UpdateCatalog calls it.
    // PR-PROVREG-138: the seed with a live Claude list laid over it; a live row for a seed id lends its
    // name (except the Default row, an empty name or a name equal to the id, :95-98), effort levels,
    // description and fast tier where non-empty/non-nil; a row the seed lacks is appended; no seed row is
    // ever dropped.
    internal static ValueList<ModelOption> MergedClaudeCatalog(IReadOnlyList<ModelOption> live)
    {
        var merged = new List<ModelOption>(ClaudeSeed); // :91
        foreach (var option in live) // :92
        {
            var index = merged.FindIndex(kept => kept.Id == option.Id); // :93
            if (index >= 0)
            {
                var kept = merged[index];
                // :95-98. The CLI's own name carries the version ("Opus 5.5"), and recipes pick the newest
                // family member by it: a seed row named just "Opus" lost to the older "Opus 5" row. The
                // Default row keeps its short name.
                if (option.Id != "default" && option.Name.Length != 0 && option.Name != option.Id) kept = kept with { Name = option.Name };
                if (option.Efforts.Count != 0) kept = kept with { Efforts = option.Efforts }; // :99
                if (option.Detail is { Length: > 0 } detail) kept = kept with { Detail = detail }; // :100
                if (option.FastTier is not null) kept = kept with { FastTier = option.FastTier }; // :101
                if (option.DefaultEffort is not null) kept = kept with { DefaultEffort = option.DefaultEffort }; // :102
                merged[index] = kept; // :103
            }
            else
            {
                merged.Add(option); // :105
            }
        }
        return new ValueList<ModelOption>(merged); // :108
    }

    // :604-605. Reads the provider's plan limits, at most once a minute after a successful read unless
    // forced. The read runs on its own detached MainActor task (R-TASK-1), so closing the popover that
    // asked for it cannot cancel it (PR-PROVREG-168).
    // :606-641
    public void RefreshPlanLimits(ProviderKind provider, bool force = false)
    {
        if (!HasEnabledAccount(provider)) return; // OD-0114 / ED-404: Off accounts cannot be probed.
        if (!PlanLimitsReader.ExposesLimits(provider)) return; // :607, PR-PROVREG-163
        if (loadingLimits.Contains(provider)) // :608
        {
            if (force) forcedLimitReads.Add(provider); // PR-PROVREG-164
            return;
        }
        if (!force && limitsFetchedAt.TryGetValue(provider, out var fetched) && SwiftDate.Now - fetched < TimeSpan.FromSeconds(60)) return; // :609, PR-PROVREG-165
        if (provider.IsAPIKeyBased) // :610
        {
            var apiKey = SelectedApiKey(provider); // :611
            if (apiKey.Length == 0) // :612
            {
                PlanLimits = planLimits.Mutating(d => d.Remove(provider)); // :692, PR-PROVREG-166 (limitsFetchedAt left untouched)
                return; // :614
            }
            LoadingLimits = loadingLimits.Mutating(s => s.Add(provider)); // :695
            var watchdog = MainActor.Spawn(async () => // :617, PR-PROVREG-167
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(25));
                LoadingLimits = loadingLimits.Mutating(s => s.Remove(provider)); // :696
                DrainedForcedRead(provider);
            });
            MainActor.Spawn(async () => // :618, PR-PROVREG-168 (its own task, not cancellable by the requester)
            {
                try
                {
                    var limits = await PlanLimitsReader.Read(provider, apiKey); // :620
                    if (limits is null) return; // :621, PR-PROVREG-170 (previous limits/timestamp left untouched)
                    PlanLimits = planLimits.Mutating(d => d[provider] = limits); // :701
                    limitsFetchedAt[provider] = SwiftDate.Now; // :623
                }
                finally
                {
                    watchdog.Cancel(); // :619 defer
                    LoadingLimits = loadingLimits.Mutating(s => s.Remove(provider)); // :698
                    DrainedForcedRead(provider);
                }
            });
            return; // :625
        }
        if (!PlanLimitsReader.ExposesLimits(provider) || loadingLimits.Contains(provider)) return; // :627 (same guards, transcribed verbatim)
        LoadingLimits = loadingLimits.Mutating(s => s.Add(provider)); // :707
        var cliWatchdog = MainActor.Spawn(async () => // :629, PR-PROVREG-167
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(25));
            LoadingLimits = loadingLimits.Mutating(s => s.Remove(provider)); // :708
            DrainedForcedRead(provider);
        });
        MainActor.Spawn(async () => // :630, PR-PROVREG-168
        {
            try
            {
                // A popover opened right after launch waits for the login shell like everything else
                // (PR-PROVREG-169); before that read the CLI can only be found on the fallback PATH.
                await LoginEnvironment.Load(); // :634
                if (Executable(provider) is not { } executable) return; // :635
                var limits = await PlanLimitsReader.Read(provider, executable, Environment(provider)); // :636
                if (limits is null) return; // :637, PR-PROVREG-170
                PlanLimits = planLimits.Mutating(d => d[provider] = limits); // :717
                limitsFetchedAt[provider] = SwiftDate.Now; // :639
            }
            finally
            {
                cliWatchdog.Cancel(); // :631 defer
                LoadingLimits = loadingLimits.Mutating(s => s.Remove(provider)); // :710
                DrainedForcedRead(provider);
            }
        });
    }

    // :643-647. Runs a forced read that had to wait because a read was already out for this provider.
    private void DrainedForcedRead(ProviderKind provider)
    {
        if (!forcedLimitReads.Remove(provider)) return; // :645
        RefreshPlanLimits(provider, force: true); // :646
    }

    // :649-676. Spends one of a provider's banked resets and re-reads its limits so the bars show the
    // cleared window. Codex through its app-server, Z.ai through its reset-card endpoint.
    public async Task<PlanLimits.ResetOutcome> ConsumeResetCredit(ProviderKind provider, string? creditID)
    {
        RedeemingReset = creditID ?? SwiftEnum<ProviderKind>.RawValue(provider); // :730, PR-PROVREG-196
        try
        {
            switch (provider) // :653
            {
                case ProviderKind.Codex: // :654, PR-PROVREG-193
                {
                    await LoginEnvironment.Load(); // :655
                    if (Executable(ProviderKind.Codex) is not { } executable) throw new ResetCreditError(new ResetCreditError.Case.CodexUnavailable()); // :656
                    var environment = Environment(ProviderKind.Codex); // :657
                    var outcome = await CodexSession.ConsumeResetCredit(executable, environment, creditID); // :658 (not wrapped: throws propagate)
                    if (await SwiftTry.OptionalAsync(() => CodexSession.ReadPlanLimits(executable, environment)) is { } limits) // :659 (try?, non-fatal)
                    {
                        PlanLimits = planLimits.Mutating(d => d[ProviderKind.Codex] = limits); // :739
                        limitsFetchedAt[ProviderKind.Codex] = SwiftDate.Now; // :661
                    }
                    return outcome; // :662
                }
                case ProviderKind.Zai: // :664, PR-PROVREG-194
                {
                    var apiKey = SelectedApiKey(ProviderKind.Zai); // :665
                    if (apiKey.Length == 0 || creditID is null) throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :666
                    var outcome = await ZaiAPI.ConsumeResetCredit(apiKey, creditID); // :667 (throws propagate)
                    if (await ZaiAPI.PlanLimits(apiKey) is { } limits) // :668 (non-throwing)
                    {
                        PlanLimits = planLimits.Mutating(d => d[ProviderKind.Zai] = limits); // :748
                        limitsFetchedAt[ProviderKind.Zai] = SwiftDate.Now; // :670
                    }
                    return outcome; // :671
                }
                default: // :673, PR-PROVREG-195
                    throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :674
            }
        }
        finally
        {
            RedeemingReset = null; // :731 defer, PR-PROVREG-196
        }
    }

    // :678-700. Swift: refreshCredits(_:force:). CreditsReader (ProviderCredits.cs, unit
    // Services.Providers.ProviderCredits) landed as a real implementation (no longer MARROW_STUB) in this
    // file's dependency cycle, un-blocking this member. PR-PROVREG-198: Command Code's key comes from its
    // own environment helper (the CLI's own login, unless Settings holds one, coupled into the environment
    // by Environment(for:)); every other credits-exposing provider reads Settings directly. PR-PROVREG-199:
    // an empty key clears the cached balance but deliberately leaves creditsFetchedAt untouched (upstream
    // defect 5), unlike RefreshPlanLimits' equivalent branch (:613-614), which clears neither. PR-PROVREG-200:
    // the sixty second gate is evaluated AFTER the key check, so a cleared key always wins over the gate.
    // PR-PROVREG-201: the read has no watchdog and no defer (upstream defect 4), so a read that never
    // completes leaves loadingCredits set for the rest of the run and the spinner never stops. PR-PROVREG-202:
    // a forced call arriving while a read is in flight is silently dropped, with no queue (upstream defect 4),
    // unlike RefreshPlanLimits' forcedLimitReads (PR-PROVREG-164).
    public void RefreshCredits(ProviderKind provider, bool force = false)
    {
        if (!HasEnabledAccount(provider)) return; // OD-0114 / ED-404: Off accounts cannot be probed.
        if (!CreditsReader.ExposesCredits(provider) || loadingCredits.Contains(provider)) return; // :682
        // :683-686. Command Code's key is the CLI's own login unless Settings holds one.
        var apiKey = provider == ProviderKind.Commandcode
            ? CommandCodeAPI.ApiKey(Environment(provider)) ?? ""
            : SelectedApiKey(provider);
        if (apiKey.Length == 0) // :687
        {
            Credits = credits.Mutating(d => d.Remove(provider)); // :767 (creditsFetchedAt left untouched, PR-PROVREG-199)
            return; // :689
        }
        if (!force && creditsFetchedAt.TryGetValue(provider, out var fetched) && SwiftDate.Now - fetched < TimeSpan.FromSeconds(60)) return; // :691
        LoadingCredits = loadingCredits.Mutating(s => s.Add(provider)); // :771
        MainActor.Spawn(async () => // :693. No watchdog, no defer (PR-PROVREG-201): unlike RefreshPlanLimits.
        {
            var balance = await CreditsReader.Read(provider, apiKey); // :694
            LoadingCredits = loadingCredits.Mutating(s => s.Remove(provider)); // :774. Plain statement, not a defer.
            if (balance is null) return; // :696
            Credits = credits.Mutating(d => d[provider] = balance); // :776
            creditsFetchedAt[provider] = SwiftDate.Now; // :698
        });
    }

    // :702-707. Swift: invalidateCredits(_:). PR-PROVREG-205: a turn just spent tokens, so the cached balance
    // is older than the spend; only a provider that exposes credits has its gate reopened.
    public void InvalidateCredits(ProviderKind provider)
    {
        if (!CreditsReader.ExposesCredits(provider)) return; // :705
        creditsFetchedAt.Remove(provider); // :706
    }

    // :709-715. PR-PROVREG-206: only Z.ai satisfies both guards (API-key based AND exposes limits), so only
    // its gate reopens at turn end; Codex, Claude, Antigravity, Copilot, Command Code and Pi expose limits
    // but are not API-key based and stay gated for the full sixty seconds.
    public void InvalidatePlanLimits(ProviderKind provider)
    {
        if (!provider.IsAPIKeyBased || !PlanLimitsReader.ExposesLimits(provider)) return; // :713
        limitsFetchedAt.Remove(provider); // :714
    }

    // :717-734. Swift: updateCatalog(_:for:). PR-PROVREG-138/139/154/155/156: the Claude and ACP merges,
    // then the empty/unchanged guard, then the whole-blob write. CaptureRun.isEnabled is always false in
    // a shipped build (dead_code.md entry 44), so that guard always passes and is not ported as a branch.
    public void UpdateCatalog(ValueList<ModelOption> list, ProviderKind provider)
    {
        var merged = provider switch // :722-725
        {
            ProviderKind.Claude => MergedClaudeCatalog(list),
            _ when provider.UsesACP => MergedACPCatalog(list, Models(provider)),
            _ => list,
        };
        if (merged.Count == 0 || merged.Equals(Models(provider))) return; // :727
        Catalogs = catalogs.Mutating(d => d[provider] = merged); // :807
        UserDefaults.Standard.Current.Set(cacheKey, EncodeCatalogs(catalogs)); // :729-733
    }

    // JSONEncoder().encode([String: [ModelOption]]) (:730-733). Writes id, name, detail, efforts,
    // defaultEffort, isDefault and fastTier for every row, mirroring what Decode reads back.
    private static byte[] EncodeCatalogs(IReadOnlyDictionary<ProviderKind, ValueList<ModelOption>> catalogs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (provider, list) in catalogs)
            {
                writer.WriteStartArray(SwiftEnum<ProviderKind>.RawValue(provider));
                foreach (var option in list)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", option.Id);
                    writer.WriteString("name", option.Name);
                    if (option.Detail is { } detail) writer.WriteString("detail", detail);
                    writer.WriteStartArray("efforts");
                    foreach (var effort in option.Efforts) writer.WriteStringValue(effort);
                    writer.WriteEndArray();
                    if (option.DefaultEffort is { } defaultEffort) writer.WriteString("defaultEffort", defaultEffort);
                    if (option.IsDefault) writer.WriteBoolean("isDefault", true);
                    if (option.FastTier is { } fastTier) writer.WriteString("fastTier", fastTier);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    // :736-738. DEVIATION ED-033: the directory is normalized (resolved to an absolute path, folded to a
    // single separator form and invariant lowercase, with no trailing separator) before it enters the key,
    // using the same path-identity normalization SwiftPath.SamePath already uses (Q50/Q64), so two spellings
    // of the same Windows directory are one cache entry instead of two, and two CLI launches instead of one.
    public static string CommandKey(ProviderKind provider, string directory) =>
        SwiftEnum<ProviderKind>.RawValue(provider) + "\n" + NormalizedCommandDirectory(directory); // :737-738

    private static string NormalizedCommandDirectory(string directory)
    {
        var normalized = SwiftPath.Normalize(SwiftPath.StandardizingPath(directory));
        if (normalized.Length > 3 && normalized[^1] == '/') normalized = normalized[..^1];
        return normalized.ToLowerInvariant();
    }

    // :740-751. Swift: updateCommands(_:for:directory:). PR-PROVREG-214: exactly one eviction, of the
    // entry with the oldest fetch timestamp, only when the key is new and the cache already holds 32 or
    // more entries; updating an existing key never evicts.
    public void UpdateCommands(ValueList<SlashCommand> list, ProviderKind provider, string directory)
    {
        Marrow.Services.Port.Skills.Providers.ProviderSkillsComposition.Observe(provider, directory, list); // RR-16: observe actual skill listings, never infer from commands.
        var key = CommandKey(provider, directory); // :741
        if (!commands.ContainsKey(key) && commands.Count >= 32) // :742
        {
            string? oldest = null;
            var oldestTime = DateTimeOffset.MaxValue;
            foreach (var (candidate, time) in commandsFetchedAt) // :743 min(by:)
            {
                if (time < oldestTime) { oldestTime = time; oldest = candidate; }
            }
            if (oldest is { } oldestKey) // :743
            {
                Commands = commands.Mutating(d => d.Remove(oldestKey)); // :823
                commandsFetchedAt.Remove(oldestKey); // :745
                CommandErrors = commandErrors.Mutating(d => d.Remove(oldestKey)); // :825
            }
        }
        Commands = commands.Mutating(d => d[key] = list); // :827
        commandsFetchedAt[key] = SwiftDate.Now; // :749
        CommandErrors = commandErrors.Mutating(d => d.Remove(key)); // :829
    }

    // :753-759. Swift: recordCommand(_:for:). PR-PROVREG-216: move-to-front with no duplicate left behind,
    // capped at exactly 100 entries per provider, and the WHOLE dictionary is written on every call (not
    // just the changed provider's slice), matching `(CaptureRun.defaults ?? .standard).set(recentCommands,
    // forKey:)`; CaptureRun is not ported (ED-001), so the destination is always UserDefaults.Standard.
    public void RecordCommand(string name, ProviderKind provider)
    {
        var key = SwiftEnum<ProviderKind>.RawValue(provider); // :754
        var recent = recentCommands.TryGetValue(key, out var existing) ? existing.ToList() : new List<string>();
        recent.RemoveAll(entry => SwiftString.Equal(entry, name)); // :755
        recent.Insert(0, name); // :756
        if (recent.Count > 100) recent = recent.GetRange(0, 100); // :757
        RecentCommands = recentCommands.Mutating(d => d[key] = new ValueList<string>(recent)); // :836
        var encoded = new Dictionary<string, object>(SwiftStringComparer.Instance);
        foreach (var (providerKey, names) in recentCommands) encoded[providerKey] = names.Cast<object>().ToArray();
        UserDefaults.Standard.Current.Set("recentSlashCommands", encoded); // :758
    }

    // PORT-ONLY (per-site decision, R-ERR-2, O-14, as PiCLI.StartFailureDescription and the ThreadRuntime
    // catches at :1733 and :2599, UI-RUNTIME-042): Swift stores `error.localizedDescription` (:857). For a CLI
    // that cannot even be launched (the thread's folder is gone, the executable cannot be started, the command
    // line is too long) macOS has Foundation's text for the POSIX/Cocoa launch error; Windows raises a plain BCL
    // Win32Exception, InvalidOperationException or CommandLineTooLongException, none of them an ILocalizedError
    // or an ISwiftErrorIdentity, for which SwiftErrors.LocalizedDescription throws NotSupportedException by
    // design. That exception escaped LoadCommands' catch block, out of the non-throwing main task, and ended
    // the process, where Swift stores the text and the composer shows it with its Retry row. A launch failure
    // therefore stores its own Message (Windows' text for it); every error that has a Swift description keeps it.
    private static string CommandFailureDescription(Exception error) =>
        error is ILocalizedError or ISwiftErrorIdentity ? SwiftErrors.LocalizedDescription(error) : error.Message;

    // :762-784. Swift: loadCommands(_:directory:force:). PR-PROVREG-217/218: only Codex and Claude ever
    // list commands; a second caller for the same (provider, directory) key awaits the first in-flight
    // task instead of spawning a second CLI; the freshness window is exactly 600 seconds and force bypasses
    // it; an unresolved executable returns silently; a failed load stores error.localizedDescription under
    // the same key without touching a previously cached list, and wholesale-clears commandErrors at 32
    // entries or more before storing the new one (PR-PROVREG-215, distinct from UpdateCommands' single-
    // oldest-entry eviction of the command cache itself).
    public async Task LoadCommands(ProviderKind provider, string directory, bool force = false)
    {
        if (!HasEnabledAccount(provider)) return; // OD-0114 / ED-404: Off accounts cannot be probed.
        if (provider != ProviderKind.Codex && provider != ProviderKind.Claude) return; // :763
        var key = CommandKey(provider, directory); // :764
        if (commandLoads.TryGetValue(key, out var inFlight)) // :765
        {
            await inFlight.Completion;
            return;
        }
        if (!force && commandsFetchedAt.TryGetValue(key, out var fetched)
            && SwiftDate.Now - fetched < TimeSpan.FromSeconds(600)) return; // :766, :44 commandFreshness = 600
        if (Executable(provider) is not { } executable) return; // :767
        var environment = Environment(provider); // :768
        var task = MainActor.Spawn(async () => // :769
        {
            try // :770
            {
                var directoryUrl = FileUrl(directory); // :771
                var list = provider == ProviderKind.Codex // :772-774
                    ? await CodexSession.ListCommands(executable, directoryUrl, environment)
                    : (await ClaudeSession.Handshake(executable, directoryUrl, environment)).Commands;
                UpdateCommands(list, provider, directory); // :775
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :776
            {
                if (commandErrors.Count >= 32) CommandErrors = new ValueMap<string, string>([], SwiftStringComparer.Instance); // :856
                CommandErrors = commandErrors.Mutating(d => d[key] = CommandFailureDescription(error)); // :857
            }
        });
        commandLoads[key] = task; // :781
        await task.Completion; // :782
        commandLoads.Remove(key); // :783
    }
}
