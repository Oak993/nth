using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Usage;

// Source: DroppyCode/Services/Providers/PlanLimits.swift (B2 1fbf81e), :1-336.
// ED-168: URLSession, URLSessionConfiguration, URLRequest, URLResponse, HTTPURLResponse, URLError and URLCache
// moved from Marrow.Services.Port to Marrow.Core.Port; used here via "using Marrow.Core.Port;".

[assembly: InternalsVisibleTo("Marrow.Services.Tests")]

namespace Marrow.Services.Providers;

// :4-46. A Swift struct -> immutable record (R-VAL-1).
public sealed record PlanLimits
{
    // :5-11
    public sealed record Window
    {
        public required string Id { get; init; }        // :6
        public required string Title { get; init; }     // :7
        public required double Percent { get; init; }   // :8
        public DateTimeOffset? ResetsAt { get; init; }   // :9
        public string? Detail { get; init; }             // :10
        // DEVIATION OD-0114 / ED-406: duration comes from the reported window, never the plan name.
        public long? DurationMinutes { get; init; }
    }

    public string? PlanName { get; init; }                   // :13
    public required ValueList<Window> Windows { get; init; } // :14
    public ResetCredits? PlanLimitsResetCredits { get; init; } // :15 Swift: resetCredits (R-NAME-3)
    public string? Problem { get; init; }                    // :17
    // PORT-ONLY OD-0114: actual account-owned API balance/allowance, without inventing plan windows.
    public ProviderCredits? AccountCredits { get; init; }

    // :20-25
    public sealed record ResetCredit
    {
        public required string Id { get; init; }         // :21
        public string? Title { get; init; }               // :22
        public string? Detail { get; init; }              // :23
        public DateTimeOffset? ExpiresAt { get; init; }   // :24
    }

    // :28-31
    public sealed record ResetCredits
    {
        public required long AvailableCount { get; init; }                // :29
        public required ValueList<ResetCredit> Credits { get; init; }     // :30
    }

    // :34-35 (R-ENUM-1)
    public enum ResetOutcome
    {
        [RawValue("reset")] Reset,
        [RawValue("nothingToReset")] NothingToReset,
        [RawValue("noCredit")] NoCredit,
        [RawValue("alreadyRedeemed")] AlreadyRedeemed,
    }
}

// :37-44. Members declared on a nested enum -> a static extension class (R-NAME-4).
public static class PlanLimitsResetOutcomeExtensions
{
    extension(PlanLimits.ResetOutcome outcome)
    {
        // :37-44
        public string Message => outcome switch
        {
            PlanLimits.ResetOutcome.Reset => "Usage was reset.",
            PlanLimits.ResetOutcome.NothingToReset => "There was no active usage to reset.",
            PlanLimits.ResetOutcome.NoCredit => "No banked resets are left.",
            PlanLimits.ResetOutcome.AlreadyRedeemed => "That reset was already used.",
        };
    }
}

// :49-61. An error enum keeps its cases as a nested record union (R-ERR-3); the unlabeled String is named after
// the binding errorDescription gives it (`let message`).
public sealed class ResetCreditError(ResetCreditError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record CodexUnavailable : Case;          // :50
        public sealed record UnknownOutcome : Case;            // :51
        public sealed record Rejected(string Message) : Case;  // :52
    }

    public Case ErrorCase { get; } = errorCase;

    // :54-60
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.CodexUnavailable => "Codex CLI not found.",
        Case.UnknownOutcome => "Codex returned an unknown reset result.",
        Case.Rejected(var message) => message,
        _ => throw new UnreachableException(),
    };
}

// :63-336. A @MainActor enum of static members -> static class; its nonisolated members touch no main state.
public static partial class PlanLimitsReader
{
    // :92 \d+x, used by ClaudeLogin.PlanName (:150).
    [GeneratedRegex(@"\d+x", RegexOptions.CultureInvariant)]
    private static partial Regex TierPattern();

    // :333 the microsecond-fraction pattern Claude timestamps carry.
    [GeneratedRegex(@"\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex FractionalSecondsPattern();

    private const string Dot = "\U000000B7"; // U+00B7 MIDDLE DOT

    // FileManager.default.temporaryDirectory (:257): the Windows user temporary directory, the same mapping
    // TextGeneration.cs's TemporaryDirectory gives it.
    private static string TemporaryDirectory =>
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath();

    // :65-70. PR-PROVREG-162, PR-USAGE-022.
    public static bool ExposesLimits(ProviderKind provider) =>
        provider == ProviderKind.Codex || provider == ProviderKind.Claude || provider == ProviderKind.Antigravity ||
        provider == ProviderKind.Copilot || provider == ProviderKind.Commandcode || provider == ProviderKind.Zai ||
        provider == ProviderKind.Pi;

    // :72-82. PR-PROVREG-171. `homeDirectory` and `usageUrl` are PORT-ONLY test seams that reach the Claude
    // arm's own seams (see ReadClaude above); production call sites never pass them.
    public static Task<PlanLimits?> Read(ProviderKind provider, SwiftURL executable, IReadOnlyDictionary<string, string> environment,
        string? homeDirectory = null, Uri? usageUrl = null) => provider switch
    {
        ProviderKind.Codex => SwiftTry.OptionalAsync(() => CodexSession.ReadPlanLimits(executable, environment)),
        ProviderKind.Claude => ReadClaude(executable, environment, homeDirectory, usageUrl),
        ProviderKind.Antigravity => SwiftTry.OptionalAsync(() => AntigravitySession.ReadPlanLimits(executable, environment)),
        ProviderKind.Copilot => SwiftTry.OptionalAsync(() => CopilotSession.ReadPlanLimits(executable, environment)),
        ProviderKind.Commandcode => CommandCodeAPI.PlanLimits(environment),
        ProviderKind.Pi => PiPlanLimits.Read(environment),
        ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or
            ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.Devin or ProviderKind.OpenRouter or ProviderKind.Local => Task.FromResult<PlanLimits?>(null), // ED-410: no quota
    };

    // :84-90. API-key subscriptions read their windows from the API instead of a CLI. PR-PROVREG-171.
    public static Task<PlanLimits?> Read(ProviderKind provider, string apiKey) => provider switch
    {
        ProviderKind.Zai => ZaiAPI.PlanLimits(apiKey),
        ProviderKind.Codex or ProviderKind.Claude or ProviderKind.Cursor or ProviderKind.Opencode or
            ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Deepseek or ProviderKind.Meta or
            ProviderKind.Devin or ProviderKind.Antigravity or ProviderKind.Copilot or ProviderKind.Commandcode or
            ProviderKind.Pi or ProviderKind.OpenRouter or ProviderKind.Local => Task.FromResult<PlanLimits?>(null), // ED-410
    };

    // DEVIATION OD-0114 / ED-406: explicit account selection must never consult a global login.
    // The environment/key is supplied by the account store; copying it prevents later edits redirecting a read.
    // OS-vault CLI sign-ins remain single-account. No credential collection or persistence is added here.
    public static Task<PlanLimits?> Read(UsageAccountKey account, SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var selected = environment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (account.IsDefault) return account.Provider == ProviderKind.Commandcode
            ? CommandCodeAPI.PlanLimits(selected, reportedWindowsOnly: true) : Read(account.Provider, executable, selected);
        bool Root(string key) => selected.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) && System.IO.Path.IsPathFullyQualified(value);
        return account.Provider switch
        {
            ProviderKind.Claude when Root("CLAUDE_CONFIG_DIR") => ReadClaude(executable, selected, allowHomeFallback: false),
            ProviderKind.Codex when Root("CODEX_HOME") => Read(account.Provider, executable, selected),
            ProviderKind.Pi when Root("PI_CODING_AGENT_DIR") => PiPlanLimits.Read(selected, reportThrottling: true),
            ProviderKind.Commandcode when (selected.TryGetValue("COMMAND_CODE_API_KEY", out var key) && !string.IsNullOrWhiteSpace(key)) || Root("HOME") => CommandCodeAPI.PlanLimits(selected, reportedWindowsOnly: true),
            _ => Task.FromResult<PlanLimits?>(null),
        };
    }

    public static Task<PlanLimits?> Read(UsageAccountKey account, string apiKey) => account.Provider switch
    {
        ProviderKind.Zai => ZaiAPI.PlanLimits(apiKey, reportedWindowsOnly: true),
        ProviderKind.Deepseek or ProviderKind.OpenRouter => AccountCreditLimitsReader.Read(account, apiKey),
        _ => Read(account.Provider, apiKey),
    };

    // :92-102. PR-PROVREG-173, PR-USAGE-078 (USAGE.json duplicates PROVREG.json's record for these lines).
    public static string WindowTitle(long? minutes)
    {
        if (minutes is not { } value) return "Usage limit";
        return value switch
        {
            300 => "5-hour limit",
            1_440 => "Daily",
            10_080 => "Weekly",
            >= 40_320 and <= 44_640 => "Monthly",
            _ => value % 1_440 == 0 ? $"{value / 1_440}-day limit" : $"{Math.Max(1L, value / 60)}-hour limit",
        };
    }

    // :104-107. PR-PROVREG-174.
    public static string? PlanName(string? raw)
    {
        if (raw is not { Length: > 0 } value) return null;
        return SwiftString.Uppercased(SwiftString.Prefix(value, 1)) + SwiftString.DropFirst(value, 1);
    }

    // :113-128. PR-PROVREG-175.
    public static string? CodexPlanName(string? raw)
    {
        if (raw is not { Length: > 0 } value) return null;
        return SwiftString.Lowercased(value.Trimmed()) switch
        {
            "pro" => "Pro 20x",
            "prolite" or "pro lite" => "Pro 5x",
            "ent26" or "hc" => "Enterprise",
            "self_serve_business_prolite" => "Business Premium",
            "self_serve_business_usage_based" => "Business",
            "enterprise_cbp_automation" => "Enterprise (Automation)",
            "enterprise_cbp_usage_based" => "Enterprise",
            "education" => "Edu",
            "edu_plus" => "Edu Plus",
            "edu_pro" => "Edu Pro",
            _ => PlanName(raw),
        };
    }

    // MARK: - Claude

    // :132-139. Claude Code's own OAuth usage endpoint first, then the CLI get_usage control request.
    // PR-PROVREG-176. `homeDirectory` and `usageUrl` are PORT-ONLY test seams (defaulting to production values,
    // the MetaAPI.cs convention): InternalsVisibleTo above grants tests/Marrow.Services.Tests access.
    internal static async Task<PlanLimits?> ReadClaude(SwiftURL executable, IReadOnlyDictionary<string, string> environment,
        string? homeDirectory = null, Uri? usageUrl = null, bool allowHomeFallback = true)
    {
        if (await ReadClaudeUsageEndpoint(executable, environment, homeDirectory, usageUrl, allowHomeFallback) is { } limits) return limits;
        return await ReadClaudeFromCLI(executable, environment);
    }

    // :141-155. The nested type is named like its Swift owner but the file has no ClaudeLogin member on
    // PlanLimitsReader (R-NAME-3 applies only to the lookup function below, renamed to avoid the clash).
    internal sealed record ClaudeLogin
    {
        public required string AccessToken { get; init; }   // :142
        public DateTimeOffset? ExpiresAt { get; init; }      // :143
        public string? SubscriptionType { get; init; }       // :144
        public string? RateLimitTier { get; init; }          // :145

        // :147-153. "Max (20x)" from `max` and `default_claude_max_20x`.
        public string? PlanName
        {
            get
            {
                if (PlanLimitsReader.PlanName(SubscriptionType) is not { } name) return null;
                if (RateLimitTier is { } tier && TierPattern().Match(tier) is { Success: true } match)
                {
                    name += " (" + match.Value + ")";
                }
                return name;
            }
        }
    }

    // :157
    internal static readonly Uri UsageURL = new("https://api.anthropic.com/api/oauth/usage");

    // :158
    internal const string KeychainService = "Claude Code-credentials";

    // :160-179. PR-PROVREG-180, PR-PROVREG-181, PR-PROVREG-182.
    internal static async Task<PlanLimits?> ReadClaudeUsageEndpoint(SwiftURL executable, IReadOnlyDictionary<string, string> environment,
        string? homeDirectory = null, Uri? usageUrl = null, bool allowHomeFallback = true)
    {
        if (await ReaderClaudeLogin(environment, homeDirectory, allowHomeFallback) is not { } login) return null;
        // :162-167. A token about to expire is refreshed by the CLI, which owns the single-use refresh token.
        if (login.ExpiresAt is { } expiresAt && expiresAt < SwiftDate.AddingTimeInterval(SwiftDate.Now, 300))
        {
            await RefreshClaudeLogin(executable, environment);
            if (await ReaderClaudeLogin(environment, homeDirectory, allowHomeFallback) is not { } refreshed) return null;
            login = refreshed;
        }
        var response = await FetchClaudeUsage(login.AccessToken, usageUrl, reportThrottling: !allowHomeFallback);
        if (response?.Status == 401 || response?.Status == 403)
        {
            await RefreshClaudeLogin(executable, environment);
            if (await ReaderClaudeLogin(environment, homeDirectory, allowHomeFallback) is not { } refreshed || refreshed.AccessToken == login.AccessToken)
            {
                return null;
            }
            login = refreshed;
            response = await FetchClaudeUsage(login.AccessToken, usageUrl, reportThrottling: !allowHomeFallback);
        }
        if (response is not { } result || result.Status != 200 || JSONValue.Parse(result.Data) is not { } json) return null;
        return ParseClaudeUsage(json, json["limits"]?.JSONValueArray ?? [], login.PlanName);
    }

    // :181-211. The stored Claude Code login: `.credentials.json`, or (on macOS) the Keychain item the CLI
    // writes. Swift: claudeLogin (R-NAME-3: renamed to avoid clashing with the nested ClaudeLogin type).
    // windows_path (PR-PROVREG-177): reproduced under OD-0008 A7 (BLK-0007). File reads are kept in exactly
    // this order: CLAUDE_CONFIG_DIR when non-empty, then <home>\.claude, each looking for ".credentials.json".
    // /usr/bin/security has no Windows analogue: when every file read fails the port returns nil directly,
    // spawning nothing and taking no Keychain or Credential Manager branch in its place.
    internal static Task<ClaudeLogin?> ReaderClaudeLogin(IReadOnlyDictionary<string, string> environment, string? homeDirectory = null, bool allowHomeFallback = true)
    {
        // PORT-ONLY: OD-0114 / ED-404. Explicit new accounts use the CLI-owned usage path;
        // do not collect their OAuth tokens or fall back to another account's global credential file.
        if (environment.ContainsKey("MARROW_ACCOUNT_ID")) return Task.FromResult<ClaudeLogin?>(null);
        var directories = new List<string>();
        if (environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configured) && configured.Length > 0)
        {
            directories.Add(configured);
        }
        // DEVIATION OD-0114 / ED-406: explicit accounts fail closed; legacy callers keep the original fallback.
        if (allowHomeFallback) directories.Add(SwiftPath.AppendingPathComponent(homeDirectory ?? LoginEnvironment.HomeDirectory, ".claude"));
        foreach (var directory in directories)
        {
            var file = SwiftPath.AppendingPathComponent(directory, ".credentials.json");
            byte[]? data;
            try { data = File.Exists(file) ? File.ReadAllBytes(file) : null; }
            catch { data = null; }
            if (data is not null && ParseLogin(data) is { } login) return Task.FromResult<ClaudeLogin?>(login);
        }
        return Task.FromResult<ClaudeLogin?>(null);
    }

    // :213-222. PR-PROVREG-178.
    internal static ClaudeLogin? ParseLogin(byte[] data)
    {
        if (JSONValue.Parse(data)?["claudeAiOauth"] is not { } oauth ||
            oauth["accessToken"]?.JSONValueString is not { Length: > 0 } token) return null;
        return new ClaudeLogin
        {
            AccessToken = token,
            ExpiresAt = oauth["expiresAt"]?.JSONValueDouble is { } ms ? SwiftDate.From1970Seconds(ms / 1_000) : null,
            SubscriptionType = oauth["subscriptionType"]?.JSONValueString,
            RateLimitTier = oauth["rateLimitTier"]?.JSONValueString,
        };
    }

    // :224-236. PR-PROVREG-179. No Windows caller (ReaderClaudeLogin above never reaches the Keychain branch
    // this decodes for); ported so the branch it exists for is fully accounted for.
    internal static byte[]? HexDecoded(string text)
    {
        if (SwiftString.Count(text) % 2 != 0) return null;
        foreach (var character in SwiftString.Characters(text))
        {
            if (character.Length != 1 || !Uri.IsHexDigit(character[0])) return null;
        }
        var bytes = new byte[text.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!byte.TryParse(text.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[index]))
            {
                return null;
            }
        }
        return bytes;
    }

    // :238-240. PR-PROVREG-180.
    internal static async Task RefreshClaudeLogin(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        _ = await SwiftTry.OptionalAsync(async () => (ShellResult?)await Shell.Run(executable.Path, ["auth", "status"], environment: environment, timeout: 20));

    // :242-250. PR-PROVREG-181. `usageUrl` is a PORT-ONLY test seam (see ReadClaude above).
    internal static async Task<(int Status, byte[] Data)?> FetchClaudeUsage(string token, Uri? usageUrl = null, bool reportThrottling = false)
    {
        var request = new URLRequest(usageUrl ?? UsageURL, timeoutInterval: 15);
        request = request.SetValue("Bearer " + token, forHTTPHeaderField: "Authorization");
        request = request.SetValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta");
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept");
        if (await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request)) is not { } exchange ||
            exchange.Response is not HTTPURLResponse http) return null;
        if (reportThrottling) LimitsPollException.ThrowIfThrottled(http);
        return (http.StatusCode, exchange.Data);
    }

    // :252-290. Claude reports limits through the `get_usage` control request, so a short-lived session asks
    // for them. PR-PROVREG-183, PR-PROVREG-184.
    internal static async Task<PlanLimits?> ReadClaudeFromCLI(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var process = new StdioProcess(
            executable.Path,
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose"],
            TemporaryDirectory,
            environment);
        try { process.Start(); } // :260-264
        catch { return null; }
        // :265-269. Ending the process finishes the message stream, which is what bounds a request that never answers.
        var watchdog = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(SwiftTask.Seconds(20));
            process.Terminate();
        });
        try // :270-273 defer
        {
            // :274. DEVIATION OD-0012 F1: upstream "droppy-code-init"; the port sends "marrow-init".
            process.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["type"] = "control_request",
                ["request_id"] = "marrow-init",
                ["request"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["subtype"] = "initialize",
                    ["hooks"] = new JSONValue.Null(),
                }, SwiftStringComparer.Instance)),
            }, SwiftStringComparer.Instance)));

            await foreach (var message in process.Messages) // :275
            {
                if (message["type"]?.JSONValueString != "control_response") continue; // :276
                var response = message["response"] ?? new JSONValue.Null(); // :277
                switch (response["request_id"]?.JSONValueString) // :278
                {
                    // :279-280. DEVIATION OD-0012 F1: upstream "droppy-code-init" / "droppy-code-usage"; the
                    // port's pair is "marrow-init" / "marrow-usage".
                    case "marrow-init":
                        process.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                        {
                            ["type"] = "control_request",
                            ["request_id"] = "marrow-usage",
                            ["request"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                            {
                                ["subtype"] = "get_usage",
                            }, SwiftStringComparer.Instance)),
                        }, SwiftStringComparer.Instance)));
                        break;
                    case "marrow-usage": // :281-284
                        var usage = response["response"] ?? new JSONValue.Null();
                        var rateLimits = usage["rate_limits"] ?? new JSONValue.Null();
                        return ParseClaudeUsage(rateLimits, rateLimits["limits"]?.JSONValueArray ?? [], PlanName(usage["subscription_type"]?.JSONValueString));
                    default: // :285-286
                        continue;
                }
            }
            return null; // :289
        }
        finally // :270-273 defer
        {
            watchdog.Cancel();
            process.Terminate();
        }
    }

    // :292-329. The `limits` rows (session, weekly, weekly per model), falling back to the top-level
    // `five_hour` and `seven_day` windows, plus extra usage when it is turned on. PR-PROVREG-185, 186, 187, 188,
    // PR-USAGE-079 (USAGE.json duplicates PROVREG.json's record for these lines).
    public static PlanLimits? ParseClaudeUsage(JSONValue root, IReadOnlyList<JSONValue> limits, string? planName, string? titlePrefix = null)
    {
        var windows = new List<PlanLimits.Window>();
        for (var index = 0; index < limits.Count; index++) // :295
        {
            var limit = limits[index];
            if (limit["percent"]?.JSONValueDouble is not { } percent) continue; // :296
            var kind = limit["kind"]?.JSONValueString ?? ""; // :297
            var group = PlanName(limit["group"]?.JSONValueString) ?? "Usage"; // :298
            var scope = limit["scope"]?["model"]?["display_name"]?.JSONValueString; // :299
            var baseTitle = kind switch // :300-303
            {
                "session" => "5-hour limit",
                "weekly_all" => $"Weekly {Dot} all models",
                _ => scope is { } scopeName ? $"{group} {Dot} {scopeName}" : group,
            };
            var title = titlePrefix is { } prefix1 ? $"{prefix1} {Dot} {baseTitle}" : baseTitle; // :305
            var idPrefix = titlePrefix is { } prefix2 ? SwiftString.Lowercased(prefix2) + "-" : ""; // :306
            windows.Add(new PlanLimits.Window // :307-312
            {
                Id = $"{idPrefix}{kind}-{index}",
                DurationMinutes = kind == "session" ? 300 : kind.StartsWith("weekly", StringComparison.Ordinal) ? 10_080 : null,
                Title = title,
                Percent = percent,
                ResetsAt = limit["resets_at"]?.JSONValueString is { } resetText ? ParseDate(resetText) : null,
            });
        }
        if (windows.Count == 0) // :314
        {
            foreach (var (key, baseTitle) in new[] { ("five_hour", "5-hour limit"), ("seven_day", $"Weekly {Dot} all models") }) // :315
            {
                if (root[key] is not { } window || window["utilization"]?.JSONValueDouble is not { } percent) continue; // :316
                var fullTitle = titlePrefix is { } prefix3 ? $"{prefix3} {Dot} {baseTitle}" : baseTitle; // :317
                var idPrefix = titlePrefix is { } prefix4 ? SwiftString.Lowercased(prefix4) + "-" : ""; // :318
                windows.Add(new PlanLimits.Window // :319
                {
                    Id = $"{idPrefix}{key}",
                    DurationMinutes = key == "five_hour" ? 300 : 10_080,
                    Title = fullTitle,
                    Percent = percent,
                    ResetsAt = window["resets_at"]?.JSONValueString is { } resetText ? ParseDate(resetText) : null,
                });
            }
        }
        if (root["extra_usage"] is { } extra && extra["is_enabled"]?.JSONValueBool == true && extra["utilization"]?.JSONValueDouble is { } extraPercent) // :322
        {
            var title = titlePrefix is { } prefix5 ? $"{prefix5} {Dot} Extra usage" : "Extra usage"; // :323
            var idPrefix = titlePrefix is { } prefix6 ? SwiftString.Lowercased(prefix6) + "-" : ""; // :324
            windows.Add(new PlanLimits.Window { Id = $"{idPrefix}extra-usage", Title = title, Percent = extraPercent, ResetsAt = null }); // :325
        }
        if (windows.Count == 0) return null; // :327
        return new PlanLimits { PlanName = planName, Windows = new ValueList<PlanLimits.Window>(windows) }; // :328
    }

    // :331-335. Claude writes microsecond timestamps, which the ISO 8601 parser does not take, so the
    // fraction is dropped. PR-PROVREG-189.
    public static DateTimeOffset? ParseDate(string text)
    {
        var trimmed = FractionalSecondsPattern().Replace(text, "");
        return ISO8601.Date(trimmed);
    }
}
