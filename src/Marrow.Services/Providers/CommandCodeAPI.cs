using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Usage;

// Source: DroppyCode/Services/Providers/CommandCodeAPI.swift (B3 52bc7926), :1-460. Not ported yet: the approvalMod
// literal (:374-439), left as its skeleton stub because the Windows form of its orphan predicate at :430 is not
// specified (PR-PROVCLI-254 open_question against ED-021; STOP #1 of this unit).
//
// PORT-ONLY seams, for tests only (R-TEST-9, R-TEST-10): FetchBaseURL (an Ambient<T> over the static baseURL :13 that
// fetch :168-175 resolves its route against; production always reads BaseURL), SupportDirectoryUnder and
// InstallMod(localApplicationData) (the OS app-data folder given explicitly, as MCPBridgeSource.InstallBridge does,
// so no test touches the real %LOCALAPPDATA%\Marrow Code), and MoneyFormatter (an Ambient<T> over the :93 call into
// ProviderCredits.money; production always calls the real ProviderCredits.Money). No production behaviour differs.

namespace Marrow.Services.Providers;

// :12-460
public static partial class CommandCodeAPI
{
    public static readonly Uri BaseURL = new("https://api.commandcode.ai");      // :13
    public static readonly Uri BillingURL = new("https://commandcode.ai/billing"); // :14

    /// <summary>PORT-ONLY test seam: the base fetch resolves its route against; always <see cref="BaseURL"/> in production.</summary>
    internal static Ambient<Uri> FetchBaseURL { get; } = new(BaseURL);

    /// <summary>PORT-ONLY test seam (:93): production always calls <see cref="ProviderCredits.Money"/> (Services.Providers.ProviderCredits,
    /// a sibling unit in this dependency cycle, MARROW_STUB at this commit). Windows() tests override it so PR-PROVCLI-257's own
    /// window-building logic (title concatenation, percent clamp, resetAt) can be verified without depending on that unit landing.</summary>
    internal static Ambient<Func<double, string, string>> MoneyFormatter { get; } = new(ProviderCredits.Money);

    // MARK: - Login (:16)

    // :18-24
    public static string? ApiKey(IReadOnlyDictionary<string, string> environment)
    {
        // :20-22
        if (environment.TryGetValue("COMMAND_CODE_API_KEY", out var raw) && raw.Trimmed() is { Length: > 0 } key)
        {
            return key;
        }
        // :23
        return CommandCodeAPIStoredLogin(environment)?.ApiKey;
    }

    // :26-29
    public sealed record StoredLogin(string ApiKey, string? UserName = null);

    // :31-39. Swift: storedLogin(environment:) (R-NAME-3: named like the nested type StoredLogin).
    public static StoredLogin? CommandCodeAPIStoredLogin(IReadOnlyDictionary<string, string> environment)
    {
        // :34-35
        var file = SwiftPath.FileURLAppendingPathComponentPath(Home(environment), ".commandcode/auth.json");
        // :36-37
        if (SwiftTry.Optional(() => File.ReadAllBytes(file)) is not { } data || JSONValue.Parse(data) is not { } json
            || json["apiKey"]?.JSONValueString?.Trimmed() is not { Length: > 0 } key)
        {
            return null;
        }
        // :38
        return new StoredLogin(key, json["userName"]?.JSONValueString ?? json["name"]?.JSONValueString);
    }

    // :34, :44. environment["HOME"], nil when empty, else LoginEnvironment.homeDirectory. HOME is normally unset on
    // Windows, so the fallback carries it there (PR-PROVCLI-255 windows_path).
    private static string Home(IReadOnlyDictionary<string, string> environment) =>
        environment.TryGetValue("HOME", out var home) && home.Length > 0 ? home : LoginEnvironment.HomeDirectory;

    // :41-48
    public static JSONValue? UserConfig(IReadOnlyDictionary<string, string> environment)
    {
        // :44-45
        var file = SwiftPath.FileURLAppendingPathComponentPath(Home(environment), ".commandcode/config.json");
        // :46
        if (SwiftTry.Optional(() => File.ReadAllBytes(file)) is not { } data) return null;
        // :47
        return JSONValue.Parse(data);
    }

    // :50-69
    public static async Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        // :53
        if (ApiKey(environment) is null) return new ProviderStatus.Auth.SignedOut();
        // :54-56
        if (await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Shell.Run(executable.Path, ["status", "--no-auto-update"], environment: environment, timeout: 25)) is not { } result)
        {
            return new ProviderStatus.Auth.Unknown();
        }
        // :57
        var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
        // :58-60
        if (FirstMatch(AuthenticatedAsPattern(), text) is { } match)
        {
            return new ProviderStatus.Auth.SignedIn(match.Groups[1].Value);
        }
        // :61-64
        if (result.Status == 3 || SwiftString.LocalizedCaseInsensitiveContains(text, "not authenticated")
            || SwiftString.LocalizedCaseInsensitiveContains(text, "not logged in"))
        {
            return new ProviderStatus.Auth.SignedOut();
        }
        // :65-67
        if (SwiftString.LocalizedCaseInsensitiveContains(text, "authentication verified"))
        {
            return new ProviderStatus.Auth.SignedIn(CommandCodeAPIStoredLogin(environment)?.UserName);
        }
        // :68
        return new ProviderStatus.Auth.Unknown();
    }

    // :58 #/Authenticated as (\S+)/#
    [GeneratedRegex(@"Authenticated as (\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex AuthenticatedAsPattern();

    // R-RE-2: CLI output can hold combining marks, so the first match whose start and capture end fall on Character
    // boundaries is Swift's first match; a match that splits a Character is no match.
    private static Match? FirstMatch(Regex pattern, string text)
    {
        for (var match = pattern.Match(text); match.Success; match = match.NextMatch())
        {
            var capture = match.Groups[1];
            if (SwiftString.IsCharacterBoundary(text, match.Index)
                && SwiftString.IsCharacterBoundary(text, capture.Index + capture.Length)) return match;
        }
        return null;
    }

    // MARK: - Plan and credits (:71)

    // :73-101
    public static async Task<PlanLimits?> PlanLimits(IReadOnlyDictionary<string, string> environment, bool reportedWindowsOnly = false)
    {
        // :79
        if (ApiKey(environment) is not { } key) return null;
        PlanLimits.Window[] found;
        JSONValue? subscription;
        {
            // :80-83. async let for both routes, awaited credits first.
            await using var scope = new AsyncLetScope();
            var creditsFetch = scope.Start(() => Fetch("/alpha/billing/credits", key, reportedWindowsOnly));
            var subscriptionFetch = scope.Start(() => Fetch("/alpha/billing/subscriptions", key, reportedWindowsOnly));
            var credits = await creditsFetch;
            subscription = await subscriptionFetch;
            // :84
            if (credits is null) return null;
            // :85-97
            found = Windows(credits, reportedWindowsOnly);
        }
        // :98
        var planName = PlanName(subscription?["data"]?["planId"]?.JSONValueString);
        // :99
        if (found.Length == 0 && planName is null) return null;
        // :100
        return new PlanLimits { PlanName = planName, Windows = new ValueList<PlanLimits.Window>(found) };
    }

    // :85-97
    private static PlanLimits.Window[] Windows(JSONValue credits, bool reportedWindowsOnly)
    {
        var windows = new List<PlanLimits.Window>(); // :85
        var limits = credits["windowLimits"] ?? new JSONValue.Null(); // :86
        foreach (var (key, title) in new[] { ("fiveHour", "5-hour limit"), ("weekly", "Weekly") }) // :87
        {
            // :88
            if (limits[key] is not { } window || window.IsNull || window["cap"]?.JSONValueDouble is not { } cap || !(cap > 0)) continue;
            // :89
            var used = window["used"]?.JSONValueDouble ?? 0;
            // :90. resetAt is epoch milliseconds, absent unless greater than zero.
            DateTimeOffset? resetAt = window["resetAt"]?.JSONValueDouble is { } milliseconds && milliseconds > 0
                ? SwiftDate.From1970Seconds(milliseconds / 1_000)
                : null;
            // :94 min(100, used / cap * 100): Swift's min(x, y) is `y < x ? y : x`.
            var percent = used / cap * 100;
            windows.Add(new PlanLimits.Window // :91-96
            {
                Id = key, // :92
                DurationMinutes = reportedWindowsOnly ? key == "fiveHour" ? 300 : 10_080 : null, // OD-0114 / ED-406
                Title = title + " \U000000B7 " + MoneyFormatter.Current(cap, "USD"), // :93 U+00B7 MIDDLE DOT
                Percent = percent < 100 ? percent : 100, // :94
                ResetsAt = resetAt, // :95
            });
        }
        return [.. windows];
    }

    // :103-109. Swift: credits(environment:).
    public static async Task<ProviderCredits?> Credits(IReadOnlyDictionary<string, string> environment)
    {
        // :107
        if (ApiKey(environment) is not { } key) return null;
        // :108
        return await Credits(key);
    }

    // :111-125. Swift: credits(apiKey:).
    public static async Task<ProviderCredits?> Credits(string apiKey)
    {
        // :112-113
        if (await Fetch("/alpha/billing/credits", apiKey) is not { } json || json["credits"] is not { } credits || credits.IsNull)
        {
            return null;
        }
        var monthly = credits["monthlyCredits"]?.JSONValueDouble ?? 0;     // :114
        var purchased = credits["purchasedCredits"]?.JSONValueDouble ?? 0; // :115
        var free = credits["freeCredits"]?.JSONValueDouble ?? 0;           // :116
        return new ProviderCredits // :117-124
        {
            Total = monthly + purchased + free, // :118
            Currency = "USD",                   // :119
            ToppedUp = purchased,               // :120
            Granted = free,                     // :121
            Monthly = monthly,                  // :122
            IsUsable = credits["belowThreshold"]?.JSONValueBool != true, // :123
        };
    }

    // :127-141. The multipliers are U+00D7 MULTIPLICATION SIGN.
    private static readonly IReadOnlyDictionary<string, string> PlanNames = Table(
    [
        ("individual-go", "Go"),
        ("individual-goat", "GOAT"),
        ("individual-pro", "Pro"),
        ("individual-pro-v1", "Pro"),
        ("individual-provider", "Provider"),
        ("individual-max", "Max 10\U000000D7"),   // U+00D7 MULTIPLICATION SIGN
        ("individual-ultra", "Max 20\U000000D7"), // U+00D7 MULTIPLICATION SIGN
        ("teams-pro", "Teams Pro"),
    ]);

    // A Swift dictionary literal (R-COLL-3: a duplicate key traps).
    private static Dictionary<string, TValue> Table<TValue>((string Key, TValue Value)[] entries)
    {
        var table = new Dictionary<string, TValue>(SwiftStringComparer.Instance);
        foreach (var (key, value) in entries)
        {
            if (!table.TryAdd(key, value)) throw SwiftTraps.Fatal("Dictionary literal contains duplicate keys");
        }
        return table;
    }

    // :143-166
    public static string? PlanName(string? planID)
    {
        // :148
        if (planID is null || planID.Length == 0) return null;
        // :149
        if (PlanNames.TryGetValue(SwiftString.Lowercased(planID), out var known)) return known;
        // :150-151
        var parts = SwiftString.Split(planID, "-").ToList();
        if (parts.Count > 0 && SwiftString.Equal(SwiftString.Lowercased(parts[0]), "individual")) parts.RemoveAt(0);
        // :152
        if (parts.Count == 0) return null;
        // :153-165
        return string.Join(" ", parts.Select(part => SwiftString.Lowercased(part) switch
        {
            "goat" => "GOAT",            // :155
            "pro" => "Pro",              // :156
            "go" => "Go",                // :157
            "max" => "Max",              // :158
            "ultra" => "Ultra",          // :159
            "teams" or "team" => "Teams", // :160
            // :161-163
            _ => MultiplierPattern().Match(part) is { Success: true } multiplier
                ? multiplier.Groups[1].Value + "\U000000D7" // U+00D7 MULTIPLICATION SIGN
                : SwiftString.Uppercased(SwiftString.Prefix(part, 1)) + SwiftString.DropFirst(part),
        }));
    }

    // :162 #/^(\d+)x$/#, matched against the whole part. UNVERIFIED O-13: `$` written \z.
    [GeneratedRegex(@"\A(\d+)x\z", RegexOptions.CultureInvariant)]
    private static partial Regex MultiplierPattern();

    // :168-175
    private static async Task<JSONValue?> Fetch(string path, string key, bool reportThrottling = false)
    {
        // :169. baseURL.appendingPathComponent(path): the base's path is empty and every route starts with "/", so
        // resolving the route against the base gives the same URL (https://api.commandcode.ai/alpha/billing/...).
        Uri url = new(FetchBaseURL.Current, path);
        var request = new URLRequest(url, timeoutInterval: 15);
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization"); // :170
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept");    // :171
        // :172-173
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (reportThrottling && result?.Response is HTTPURLResponse response) LimitsPollException.ThrowIfThrottled(response);
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse { StatusCode: 200 })
        {
            return null;
        }
        // :174
        return JSONValue.Parse(exchange.Data);
    }

    // MARK: - Models (:177)

    // :179-192
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        // :184
        var result = await Shell.Run(executable.Path, ["--list-models", "--no-auto-update"], environment: environment, timeout: 30);
        // :185
        var text = TextCleanup.StripANSI(result.Output);
        // :186-187
        var configured = UserConfig(environment);
        var list = ParseModels(text, configuredModel: configured?["model"]?.JSONValueString, configuredEfforts: configured?["reasoningEffort"]);
        // :188-190
        if (list.Count == 0)
        {
            throw new ProviderError(new ProviderError.Case.Failed(
                TextCleanup.LastLines(result.ErrorOutput + result.Output) ?? "Command Code listed no models."));
        }
        // :191
        return list;
    }

    // :194-234
    public static ValueList<ModelOption> ParseModels(string text, string? configuredModel, JSONValue? configuredEfforts)
    {
        var options = new List<ModelOption>(); // :195
        string? section = null; // :196
        foreach (var rawLine in SwiftString.ComponentsSeparatedByNewlines(text)) // :197
        {
            var line = FoundationText.Trim(rawLine); // :198
            if (line.Length == 0) continue; // :199
            // :200-201
            if (SwiftString.HasPrefix(line, "Available models") || SwiftString.HasPrefix(line, "Pass the full id")
                || SwiftString.HasPrefix(line, "cmd ") || SwiftString.HasPrefix(line, "Docs:") || SwiftString.HasPrefix(line, "Usage")) continue;
            // :202-206
            if (RowMatch(line) is not var (id, rest))
            {
                // A line without a two-space gap is a section header, such as "Anthropic".
                // :204. line.contains(" "): a literal search, as Foundation's range(of:) does.
                if (!line.Contains(' ', StringComparison.Ordinal) && IsUppercase(SwiftString.Characters(line).First())) section = line;
                else if (SwiftString.Count(line) < 24) section = line;
                continue;
            }
            // :208
            if (!id.Contains('-', StringComparison.Ordinal) && !id.Contains('/', StringComparison.Ordinal)) continue;
            var detail = FoundationText.Trim(rest); // :209
            var isRecommended = false; // :210
            var isCLIDefault = false;  // :211
            // :212-213. range(of:) is a literal search; the first occurrence is removed.
            var recommended = detail.IndexOf(" (recommended)", StringComparison.Ordinal);
            if (recommended >= 0) { detail = detail.Remove(recommended, " (recommended)".Length); isRecommended = true; }
            var cliDefault = detail.IndexOf(" (default)", StringComparison.Ordinal);
            if (cliDefault >= 0) { detail = detail.Remove(cliDefault, " (default)".Length); isCLIDefault = true; }
            // :214-221. The detail joins the section and the rest with U+00B7 MIDDLE DOT, nils dropped.
            var option = new ModelOption(
                id,
                DisplayName(id),
                detail: string.Join(" \U000000B7 ", new[] { section, detail.Length == 0 ? null : detail }.OfType<string>()),
                efforts: Efforts(id),
                defaultEffort: configuredEfforts?[id]?.JSONValueString,
                isDefault: isRecommended || isCLIDefault);
            // :222
            if (configuredModel is not null && Matches(id, configuredModel)) option = option with { IsDefault = true };
            options.Add(option); // :223
        }
        // :225-232. One default only: the CLI's configured model wins, then the recommended row.
        var chosen = configuredModel is null ? -1 : options.FindIndex(o => Matches(o.Id, configuredModel));
        if (chosen >= 0)
        {
            for (var i = 0; i < options.Count; i++) options[i] = options[i] with { IsDefault = i == chosen }; // :227
            var moved = options[chosen]; // :228
            options.RemoveAt(chosen);
            options.Insert(0, moved); // :229
        }
        else if (options.FindIndex(o => o.IsDefault) is var first and >= 0)
        {
            for (var i = 0; i < options.Count; i++) options[i] = options[i] with { IsDefault = i == first }; // :231
        }
        return new ValueList<ModelOption>(options); // :233
    }

    // :202 #/^(\S+)\s{2,}(.+)$/#. UNVERIFIED O-13: `$` written \z.
    [GeneratedRegex(@"\A(\S+)\s{2,}(.+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ModelRowPattern();

    // R-RE-2: the CLI's text can hold combining marks, so a match whose id end or detail start splits a Character is
    // no match.
    private static (string Id, string Detail)? RowMatch(string line)
    {
        var match = ModelRowPattern().Match(line);
        if (!match.Success) return null;
        var id = match.Groups[1];
        var rest = match.Groups[2];
        if (!SwiftString.IsCharacterBoundary(line, id.Index + id.Length) || !SwiftString.IsCharacterBoundary(line, rest.Index)) return null;
        return (id.Value, rest.Value);
    }

    // :204 Character.isUppercase, including non-ASCII scalars and extended grapheme clusters.
    private static bool IsUppercase(string character) => SwiftCharacter.IsUppercase(character);

    // :236-244
    public static bool Matches(string id, string query)
    {
        var lhs = SwiftString.Lowercased(id);    // :238
        var rhs = SwiftString.Lowercased(query); // :239
        if (SwiftString.Equal(lhs, rhs)) return true; // :240
        var shortLHS = SwiftString.Split(lhs, "/") is { Count: > 0 } l ? l[^1] : lhs; // :241
        var shortRHS = SwiftString.Split(rhs, "/") is { Count: > 0 } r ? r[^1] : rhs; // :242
        return SwiftString.Equal(shortLHS, shortRHS); // :243
    }

    // :246-279
    public static string DisplayName(string id)
    {
        var @short = SwiftString.Split(id, "/") is { Count: > 0 } s ? s[^1] : id;               // :250
        var @base = SwiftString.Split(@short, ":") is { Count: > 0 } b ? b[0] : @short;         // :251
        var suffix = @short.Contains(":free", StringComparison.Ordinal) ? " Free" : "";          // :252
        var tokens = SwiftString.Split(@base, "-").ToList();                                     // :253
        // :254-263. Trailing version pairs read as one number: `claude-fable-5-1` is Fable 5.1.
        var index = 0;
        while (index + 1 < tokens.Count)
        {
            if (AllNumbers(tokens[index]) && AllNumbers(tokens[index + 1]))
            {
                tokens[index] += "." + tokens[index + 1];
                tokens.RemoveAt(index + 1);
            }
            else
            {
                index += 1;
            }
        }
        // :264-274
        var words = tokens.Select(token => SwiftString.Lowercased(token) switch
        {
            "gpt" => "GPT", // :266
            "glm" => "GLM", // :267
            "hy3" or "hy4" => SwiftString.Uppercased(token), // :268
            "v4" or "v4.1" or "v2.5" => SwiftString.Uppercased(token), // :269
            // :270-272
            _ => SwiftString.Characters(token).FirstOrDefault() is { } first && SwiftCharacter.IsNumber(first)
                ? token
                : SwiftString.Uppercased(SwiftString.Prefix(token, 1)) + SwiftString.DropFirst(token),
        }).ToList();
        // :275-277
        if (words.Count > 1 && SwiftString.Equal(words[0], "GPT"))
        {
            return "GPT-" + string.Join(" ", words.Skip(1)) + suffix;
        }
        return string.Join(" ", words) + suffix; // :278
    }

    // :257 tokens[i].allSatisfy(\.isNumber)
    private static bool AllNumbers(string token) => SwiftString.Characters(token).All(SwiftCharacter.IsNumber);

    // :281-310
    public static ValueList<string> Efforts(string id)
    {
        string[] full = ["low", "medium", "high", "xhigh", "max"]; // :284
        string[] toXHigh = ["low", "medium", "high", "xhigh"];     // :285
        string[] toHigh = ["low", "medium", "high"];               // :286
        string[] highMax = ["high", "max"];                        // :287
        string[] lowHighMax = ["low", "high", "max"];              // :288
        var table = Table<string[]>( // :289-308
        [
            ("claude-sonnet-5", full), ("claude-sonnet-4-6", full), ("claude-fable-5-1", full), ("claude-fable-5", full),
            ("claude-opus-5-5", full), ("claude-opus-5", full), ("claude-opus-4-8", full), ("claude-opus-4-7", full),
            ("gpt-6-astra", full), ("gpt-5.6-sol", full), ("gpt-5.6-terra", full), ("gpt-5.6-luna", full),
            ("gpt-5.5", toXHigh), ("gpt-5.4", toXHigh), ("gpt-5.3-codex", toXHigh), ("gpt-5.4-mini", toHigh),
            ("deepseek/deepseek-v4-pro", highMax), ("deepseek/deepseek-v4-flash", highMax),
            ("deepseek/deepseek-v4-flash-vision-exp", highMax), ("deepseek/deepseek-v4-flash-fast", lowHighMax),
            ("deepseek/deepseek-v4.1-flash", lowHighMax),
            ("moonshotai/kimi-k3", lowHighMax), ("zai-org/glm-5.3", lowHighMax), ("z-ai/glm-5.3-flash", lowHighMax),
            ("zai-org/glm-5.2", highMax),
            ("google/gemini-3.8-flash", toHigh), ("google/gemini-3.7-flash", toHigh), ("google/gemini-3.6-flash", toHigh),
            ("google/gemini-3.5-flash", toHigh), ("google/gemini-3.5-flash-lite", toHigh), ("google/gemini-3.1-flash-lite", toHigh),
            ("tencent/hy4-preview", toHigh), ("sakana/fugu-ultra", ["high", "xhigh"]),
            ("xai/grok-4.5", toHigh), ("xai/grok-4.6", toXHigh),
            ("qwen/qwen3.8-max-0902", ["low", "medium", "xhigh"]), ("qwen/qwen3.8-max", ["low", "medium", "xhigh"]),
            ("qwen/qwen3.8-27b", ["low", "medium", "xhigh"]), ("qwen/qwen3.8-flash", ["low", "medium", "xhigh"]),
            ("meta/muse-spark-1.1", toXHigh), ("meta/muse-spark-1.2", toXHigh), ("meta/muse-spark-1.2-contributor", toXHigh),
            ("meta/muse-spark-1.3", full), ("meta/muse-spark-1.3-contributor", toXHigh),
            ("minimaxai/minimax-m3", toHigh), ("minimax/minimax-m3-free", toHigh),
        ]);
        // :309
        return table.TryGetValue(SwiftString.Lowercased(id), out var efforts) ? new ValueList<string>(efforts) : ValueList<string>.Empty;
    }

    // :312-345
    public static long? ContextWindow(string? id)
    {
        // :314
        if (id is null) return null;
        var table = Table<long>( // :315-341
        [
            ("claude-sonnet-5", 1_000_000), ("claude-sonnet-4-6", 1_000_000), ("claude-fable-5-1", 1_000_000), ("claude-fable-5", 1_000_000),
            ("claude-opus-5-5", 1_000_000), ("claude-opus-5", 1_000_000), ("claude-opus-4-8", 1_000_000), ("claude-opus-4-7", 1_000_000), ("claude-haiku-4-5", 200_000),
            ("claude-haiku-4-5-20251001", 200_000),
            ("gpt-6-astra", 1_050_000), ("gpt-5.6-sol", 1_050_000), ("gpt-5.6-terra", 1_050_000), ("gpt-5.6-luna", 1_050_000),
            ("gpt-5.5", 400_000), ("gpt-5.4", 400_000), ("gpt-5.3-codex", 400_000), ("gpt-5.4-mini", 400_000),
            ("deepseek/deepseek-v4-pro", 1_000_000), ("deepseek/deepseek-v4-flash", 1_000_000), ("deepseek/deepseek-v4-flash-vision-exp", 1_000_000),
            ("deepseek/deepseek-v4-flash-fast", 1_000_000), ("deepseek/deepseek-v4.1-flash", 1_000_000),
            ("moonshotai/kimi-k3", 1_000_000), ("moonshotai/kimi-k2.7-code", 256_000), ("moonshotai/kimi-k2.7-code-highspeed", 262_000),
            ("moonshotai/kimi-k2.6", 256_000), ("moonshotai/kimi-k2.5", 256_000),
            ("zai-org/glm-5.3", 1_000_000), ("z-ai/glm-5.3-flash", 1_048_576), ("zai-org/glm-5.2", 1_000_000), ("zai-org/glm-5.2-fast", 1_000_000),
            ("zai-org/glm-5", 200_000), ("zai-org/glm-5.1", 200_000),
            ("minimaxai/minimax-m3", 1_000_000), ("minimaxai/minimax-m2.7", 197_000), ("minimaxai/minimax-m2.5", 200_000),
            ("xiaomi/mimo-v2.5-pro", 1_000_000), ("xiaomi/mimo-v2.5", 1_000_000),
            ("qwen/qwen3.7-max", 1_000_000), ("qwen/qwen3.7-plus", 1_000_000), ("qwen/qwen3.8-max-0902", 1_000_000), ("qwen/qwen3.8-max", 1_000_000),
            ("qwen/qwen3.8-27b", 262_144), ("qwen/qwen3.8-flash", 1_000_000), ("qwen/qwen3.7-flash", 1_000_000),
            ("meituan/longcat-2.0:free", 1_048_576), ("stepfun/step-3.7-flash", 256_000), ("stepfun/step-3.5-flash", 1_000_000),
            ("tencent/hy4-preview", 1_048_576), ("tencent/hy3-paid", 262_144),
            ("google/gemini-3.5-flash", 1_000_000), ("google/gemini-3.8-flash", 1_000_000), ("google/gemini-3.7-flash", 1_048_576),
            ("google/gemini-3.6-flash", 1_000_000), ("google/gemini-3.5-flash-lite", 1_000_000), ("google/gemini-3.1-flash-lite", 1_000_000),
            ("thinkingmachines/inkling", 256_000), ("thinkingmachines/inkling-small", 1_000_000),
            ("poolside/laguna-s-2.1-free", 256_000), ("inclusionai/ling-3.0-flash-sante:free", 262_144),
            ("sakana/fugu-ultra", 1_000_000), ("xai/grok-4.5", 500_000), ("xai/grok-4.6", 500_000),
            ("meta/muse-spark-1.1", 1_048_576), ("meta/muse-spark-1.2", 1_048_576), ("meta/muse-spark-1.2-contributor", 1_048_576),
            ("meta/muse-spark-1.3", 1_048_576), ("meta/muse-spark-1.3-contributor", 1_048_576),
            ("nvidia/nemotron-3-ultra-550b-a55b", 1_000_000),
        ]);
        // :342-343
        var lowered = SwiftString.Lowercased(id);
        if (table.TryGetValue(lowered, out var window)) return window;
        // :344. The first key the CLI's own rule matches, in the dictionary's unspecified order (R-COLL-2; every key's
        // name after its last slash is distinct, so at most one key can match).
        foreach (var (key, value) in table)
        {
            if (Matches(key, lowered)) return value;
        }
        return null;
    }

    // :347-361. The separators are U+00B7 MIDDLE DOT with a space on each side; the ampersands are literal.
    public static ValueList<ModelOption> Seed { get; } = new(
    [
        new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic \U000000B7 best combo of speed & intelligence", efforts: Efforts("claude-sonnet-5"), isDefault: true), // :350
        new ModelOption("claude-fable-5-1", "Claude Fable 5.1", detail: "Anthropic \U000000B7 most capable for demanding reasoning & long-horizon agents", efforts: Efforts("claude-fable-5-1")), // :351
        new ModelOption("claude-opus-5-5", "Claude Opus 5.5", detail: "Anthropic \U000000B7 the newest Opus, for everyday complex work and coding", efforts: Efforts("claude-opus-5-5")), // :352
        new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic \U000000B7 most intelligent Opus for agents and coding", efforts: Efforts("claude-opus-5")), // :353
        new ModelOption("gpt-6-astra", "GPT-6 Astra", detail: "OpenAI \U000000B7 most capable OpenAI model for demanding reasoning & agents", efforts: Efforts("gpt-6-astra")), // :354
        new ModelOption("gpt-5.6-sol", "GPT-5.6 Sol", detail: "OpenAI \U000000B7 latest frontier model for general complex work", efforts: Efforts("gpt-5.6-sol")), // :355
        new ModelOption("gpt-5.6-luna", "GPT-5.6 Luna", detail: "OpenAI \U000000B7 fast and efficient model", efforts: Efforts("gpt-5.6-luna")), // :356
        new ModelOption("google/gemini-3.8-flash", "Gemini 3.8 Flash", detail: "Google \U000000B7 newest Gemini Flash, improved core reasoning", efforts: Efforts("google/gemini-3.8-flash")), // :357
        new ModelOption("deepseek/deepseek-v4-flash", "DeepSeek V4 Flash", detail: "Open Source \U000000B7 fast hybrid-attention reasoning", efforts: Efforts("deepseek/deepseek-v4-flash")), // :358
        new ModelOption("moonshotai/kimi-k3", "Kimi K3", detail: "Open Source \U000000B7 long-horizon coding & knowledge work with 1M context", efforts: Efforts("moonshotai/kimi-k3")), // :359
        new ModelOption("zai-org/glm-5.3", "GLM 5.3", detail: "Open Source \U000000B7 frontier coding with emergent cyber capabilities", efforts: Efforts("zai-org/glm-5.3")), // :360
    ]);

    // MARK: - The approval mod (:363)

    // :365-439. A static let string (R-STR-9), the Swift raw string literal as it evaluates (four-space indentation
    // of the closing delimiter stripped, no final newline). PR-PROVCLI-254's open_question on the orphan predicate at
    // :430 is settled by ED-021 (engineering_decisions.md:91 Q57): replace the POSIX-only `process.ppid === 1` with
    // the Windows-correct signal-0 liveness probe of the same parent, keeping the 6-hour cap, the unlink and the
    // `block: true` verbatim. Carries the OD-0012 F1 wordings of :424 and :432 ('The user declined this in Marrow.',
    // 'Marrow did not answer.').
    public static string ApprovalMod =>
        """
        import fs from 'node:fs'
        import path from 'node:path'

        // Tools that only look: they never wait on the user.
        const READ_ONLY = new Set([
          'read_file', 'read_directory', 'glob', 'grep', 'shell_output', 'shell_tasks',
          'todo_write', 'task_create', 'task_update', 'task_list', 'task_get', 'sleep',
          'web_search', 'web_fetch', 'agent', 'agent_output', 'activate_skill', 'taste',
          'get_diagnostics', 'ask_user_question', 'plan_review', 'enter_plan_mode', 'exit_plan_mode',
          'cron_list', 'run_command',
        ])

        const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

        export default function (cmd: any) {
          const bridgePath = process.env.DROPPY_CODE_MCP_BRIDGE
          if (bridgePath) {
            import(bridgePath).then(async (bridge: any) => {
              for (const tool of await bridge.loadMCPTools()) {
                cmd.addTool({
                  schema: { name: 'mcp__' + tool.server + '__' + tool.name, description: tool.description, input_schema: tool.inputSchema },
                  run: async ({input}: any) => {
                    const text = await tool.call(input || {})
                    return { ok: true, content: [{ type: 'text', text }] }
                  },
                })
              }
            }).catch((error: any) => console.error('droppy mcp', error))
          }
          const dir = process.env.DROPPY_CODE_APPROVALS
          if (!dir) return
          cmd.hooks({
            beforeToolCall: async ({toolCallId, toolName, input}: any, ctx: any) => {
              if (READ_ONLY.has(toolName)) return
              const id = String(toolCallId || `${Date.now()}-${Math.random().toString(36).slice(2)}`).replace(/[^A-Za-z0-9_.-]/g, '_')
              const request = path.join(dir, `${id}.request.json`)
              const reply = path.join(dir, `${id}.reply.json`)
              try { fs.unlinkSync(reply) } catch {}
              fs.writeFileSync(`${request}.tmp`, JSON.stringify({toolCallId: id, toolName, input}))
              fs.renameSync(`${request}.tmp`, request)
              const started = Date.now()
              for (;;) {
                let raw: string | undefined
                try { raw = fs.readFileSync(reply, 'utf8') } catch {}
                if (raw !== undefined) {
                  try { fs.unlinkSync(reply) } catch {}
                  let answer: any = {}
                  try { answer = JSON.parse(raw) } catch {}
                  if (answer.decision === 'allow') return
                  return {block: true, additionalContext: answer.reason || 'The user declined this in nth.'}
                }
                if (ctx && ctx.signal && ctx.signal.aborted) {
                  try { fs.unlinkSync(request) } catch {}
                  return {block: true, additionalContext: 'The turn was stopped.'}
                }
                if ((() => { try { process.kill(process.ppid, 0); return false } catch (e) { return e.code === 'ESRCH' } })() || Date.now() - started > 6 * 60 * 60 * 1000) {
                  try { fs.unlinkSync(request) } catch {}
                  return {block: true, additionalContext: 'nth did not answer.'}
                }
                await sleep(40)
              }
            },
          })
        }
        """;

    // :441-446. DEVIATION OD-0008 C7: Application Support is %LOCALAPPDATA% and AppInfo.name is the port's data
    // identity "Marrow Code", so the directory is %LOCALAPPDATA%\Marrow Code\CommandCode (PR-PROVCLI-207, ED-023). There
    // is no macOS Library/Application Support fallback (:444, no Windows counterpart).
    public static SwiftURL SupportDirectory => SupportDirectoryUnder(LocalApplicationData());

    private static string LocalApplicationData() => DevDataRoot.LocalApplicationData(); // ED-212

    /// <summary>PORT-ONLY test seam: <see cref="SupportDirectory"/> under an explicit OS app-data folder.</summary>
    internal static SwiftURL SupportDirectoryUnder(string localApplicationData) =>
        FileURL(SupportDirectoryPath(localApplicationData), isDirectory: true);

    // :445 base.appendingPathComponent("\(AppInfo.name)/CommandCode", isDirectory: true)
    private static string SupportDirectoryPath(string localApplicationData) =>
        SwiftPath.AppendingPathComponent(localApplicationData, AppInfo.DataFolderName + "/CommandCode"); // OD-0135

    // :448-459
    public static SwiftURL InstallMod() => InstallMod(LocalApplicationData());

    /// <summary>PORT-ONLY test seam: <see cref="InstallMod()"/> under an explicit OS app-data folder.</summary>
    internal static SwiftURL InstallMod(string localApplicationData)
    {
        // :451-452 createDirectory(at:withIntermediateDirectories: true); a failure propagates.
        var directory = SupportDirectoryPath(localApplicationData);
        Directory.CreateDirectory(directory);
        // :453. DEVIATION OD-0012 F1: "marrow-approvals.ts" (upstream "droppy-code-approvals.ts").
        var file = SwiftPath.AppendingPathComponent(directory, "marrow-approvals.ts");
        // :454
        var data = Encoding.UTF8.GetBytes(ApprovalMod);
        // :455-457. Written atomically only when the bytes on disk differ; (try? Data(contentsOf:)) is nil for a
        // missing or unreadable file, which is never equal. The CLI may be reading the file (port_impacts.md 22).
        var existing = SwiftTry.Optional(() => File.ReadAllBytes(file));
        if (existing is null || !existing.AsSpan().SequenceEqual(data))
        {
            FileOps.WriteAtomic(file, data);
        }
        // :458
        return FileURL(file, isDirectory: false);
    }

    // URL(fileURLWithPath:) for a path this file builds. ENGINEERING DECISION mirroring Core/Models/Library.cs Url and
    // Core/Models/Timeline.cs Attachment.Url: F.URL (SwiftURL) only builds URL(string:), so the path is percent-encoded
    // into a file:// URL by hand (the ASCII unreserved set and '/' and ':' kept literal); isDirectory adds the
    // trailing "/" a directory URL carries.
    private static SwiftURL FileURL(string path, bool isDirectory)
    {
        var builder = new StringBuilder("file://");
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized[0] != '/') builder.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".Contains(c, StringComparison.Ordinal))) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (isDirectory && builder[^1] != '/') builder.Append('/');
        return SwiftURL.Create(builder.ToString()) ?? throw SwiftTraps.Fatal("Invalid Command Code support path.");
    }
}
