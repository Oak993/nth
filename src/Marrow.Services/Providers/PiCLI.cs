using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/PiCLI.swift (B2 1fbf81e), :1-217.

namespace Marrow.Services.Providers;

/// <summary>The small surface the app needs of the Pi coding harness (`pi`, the npm package
/// `@earendil-works/pi-coding-agent`): where its login lives, its model catalog over RPC, and the gate extension that
/// routes tool approvals to the app.
///
/// Pi signs in per provider under `~/.pi/agent/auth.json` (or `PI_CODING_AGENT_DIR` when set); a bare provider API key
/// in the environment also counts as signed in.</summary>
// :9-217. A caseless enum -> static class.
public static class PiCLI
{
    /// <summary>Pi's `--thinking` levels, which the app's effort strings map to 1:1.</summary>
    // :11
    public static readonly ValueList<string> ThinkingLevels =
        new(new[] { "off", "minimal", "low", "medium", "high", "xhigh", "max" });

    /// <summary>Where the gate extension and each session's gate file live.</summary>
    // :14-18. DEVIATION OD-0008 C7: Application Support is %LOCALAPPDATA% and the data identity is "Marrow Code", so the
    // directory is %LOCALAPPDATA%\Marrow Code\pi (upstream "<Application Support>/Droppy Code/pi"; ED-023 puts it under
    // the same root as CommandCodeAPI's). There is no macOS Library/Application Support fallback (:16, no Windows
    // counterpart).
    public static SwiftURL SupportDirectory => SupportDirectoryUnder(LocalApplicationData());

    /// <summary>PORT-ONLY test seam: <see cref="SupportDirectory"/> under an explicit OS app-data folder.</summary>
    internal static SwiftURL SupportDirectoryUnder(string localApplicationData) =>
        FileURL(SupportDirectoryPath(localApplicationData), isDirectory: true);

    // :17 base.appendingPathComponent("Droppy Code/pi", isDirectory: true), under OD-0008 C7.
    private static string SupportDirectoryPath(string localApplicationData) =>
        SwiftPath.AppendingPathComponent(localApplicationData, AppInfo.DataFolderName + "/pi"); // OD-0135

    private static string LocalApplicationData() => DevDataRoot.LocalApplicationData(); // ED-212

    /// <summary>`PI_CODING_AGENT_DIR` when set, else `&lt;home&gt;/.pi/agent`.</summary>
    // :20-27
    public static SwiftURL ConfigDirectory(IReadOnlyDictionary<string, string> environment) =>
        FileURL(ConfigDirectoryPath(environment), isDirectory: true);

    /// <summary>PORT-ONLY: the path of <see cref="ConfigDirectory"/>, which the auth reader opens directly.</summary>
    internal static string ConfigDirectoryPath(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("PI_CODING_AGENT_DIR", out var directory) && directory.Length != 0) // :22-24
        {
            // :23 URL(fileURLWithPath:) resolves a relative path against the current directory.
            return Path.IsPathRooted(directory) ? directory : SwiftPath.AppendingPathComponent(Environment.CurrentDirectory, directory);
        }
        return SwiftPath.AppendingPathComponent(Home(environment), ".pi/agent"); // :25-26
    }

    // :25 and :48. `environment["HOME"].flatMap { $0.isEmpty ? nil : $0 } ?? LoginEnvironment.homeDirectory`
    private static string Home(IReadOnlyDictionary<string, string> environment) =>
        environment.TryGetValue("HOME", out var home) && home.Length != 0 ? home : LoginEnvironment.HomeDirectory;

    /// <summary>`auth.json` as an object with at least one key (the keys are provider names such as anthropic, openai),
    /// else any provider API key in the environment.</summary>
    // :31-44. Nothing here awaits; the Task keeps the Swift async signature.
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var file = SwiftPath.AppendingPathComponent(ConfigDirectoryPath(environment), "auth.json"); // :32
        if (SwiftTry.Optional(() => File.ReadAllBytes(file)) is { } data // :33-35
            && JSONValue.Parse(data) is { } json
            && json.JSONValueObject is { Count: > 0 } @object)
        {
            // :36. `keys.sorted()`: Swift String order (R-STR-2).
            return Task.FromResult<ProviderStatus.Auth>(new ProviderStatus.Auth.SignedIn(
                string.Join(", ", @object.Keys.OrderBy(key => key, SwiftStringComparer.Instance))));
        }
        foreach (var key in (string[])["ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", "OPENROUTER_API_KEY", "GROQ_API_KEY", "MISTRAL_API_KEY", "DEEPSEEK_API_KEY", "XAI_API_KEY"]) // :38
        {
            if (environment.TryGetValue(key, out var value) && value.Length != 0) // :39-41
            {
                return Task.FromResult<ProviderStatus.Auth>(new ProviderStatus.Auth.SignedIn(null));
            }
        }
        return Task.FromResult<ProviderStatus.Auth>(new ProviderStatus.Auth.SignedOut()); // :43
    }

    /// <summary>Asks a throwaway `pi --mode rpc` process for its state and model catalog.</summary>
    // :46-112
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var home = Home(environment); // :48
        var launchEnvironment = new Dictionary<string, string>(environment) { ["PI_SKIP_VERSION_CHECK"] = "1" }; // :49-50
        var process = new StdioProcess( // :51-57
            executable.Path,
            ["--mode", "rpc", "--no-session", "--no-extensions", "--no-skills", "--no-prompt-templates", "--no-themes", "--no-context-files"],
            home,
            launchEnvironment,
            StdioFramer.Framing.Lines);
        try // :58-62
        {
            process.Start();
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new ProviderError(new ProviderError.Case.Failed("Pi could not start: " + StartFailureDescription(error)));
        }
        process.Send(JSONValue.FromDictionaryLiteral(("type", "get_state"), ("id", "state"))); // :63
        process.Send(JSONValue.FromDictionaryLiteral(("type", "get_available_models"), ("id", "models"))); // :64
        JSONValue? stateResponse = null; // :65
        JSONValue? modelsResponse = null; // :66
        // :67-70. A Task in a nonisolated function (R-TASK-1). It terminates after the sleep whether or not it was
        // cancelled: the Swift body checks nothing after `try?`.
        var timeout = TaskHandle.Detached(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(30));
            process.Terminate();
        });
        await foreach (var message in process.Messages) // :71
        {
            switch (message["id"]?.JSONValueString) // :72
            {
                case "state": // :73-74
                    stateResponse = message;
                    break;
                case "models": // :75-76
                    modelsResponse = message;
                    break;
                default: // :77-78
                    break;
            }
            if (stateResponse is not null && modelsResponse is not null) break; // :80
        }
        timeout.Cancel(); // :82
        process.Terminate(); // :83
        return Catalog(stateResponse, modelsResponse);
    }

    // Per-site decision (R-ERR-2, O-14): on macOS, `error.localizedDescription` for a failed
    // `Process.run()` is Foundation's text for the POSIX/Cocoa launch error, which
    // SwiftErrors.LocalizedDescription reproduces only for a recognized SwiftError. On Windows the
    // process unit's launcher signals a failed start with a plain BCL Win32Exception (ADR-0002,
    // R-PROC-1), which is neither an ILocalizedError nor an ISwiftErrorIdentity, so
    // SwiftErrors.LocalizedDescription throws NotSupportedException for it by design. Since
    // PR-PROVCLI-310 requires this site to reach ProviderError.Failed with a message built from the
    // failure's own description rather than let that NotSupportedException replace it, this site
    // falls back to the exception's own Message (Win32Exception's native Windows error text, the
    // closest Windows equivalent of the launch failure's description) when SwiftErrors has no
    // mapping.
    private static string StartFailureDescription(Exception error) =>
        error is ILocalizedError or ISwiftErrorIdentity ? SwiftErrors.LocalizedDescription(error) : error.Message;

    /// <summary>PORT-ONLY test seam: the catalog :85-111 builds from the two replies, unchanged, factored out of
    /// <see cref="ListModels"/> so it can be proven without a process.</summary>
    // :85-111
    internal static ValueList<ModelOption> Catalog(JSONValue? stateResponse, JSONValue? modelsResponse)
    {
        var stateData = stateResponse?["data"] ?? stateResponse ?? new JSONValue.Null(); // :85
        var currentProvider = stateData["model"]?["provider"]?.JSONValueString; // :86
        var currentID = stateData["model"]?["id"]?.JSONValueString; // :87
        var items = modelsResponse?["data"]?["models"]?.JSONValueArray ?? modelsResponse?["models"]?.JSONValueArray ?? []; // :88
        var options = new List<ModelOption>(); // :89
        foreach (var item in items) // :90
        {
            if (item["provider"]?.JSONValueString is not { } provider || item["id"]?.JSONValueString is not { } id) continue; // :91
            var reasoning = item["reasoning"]?.JSONValueBool == true; // :92
            var fullID = provider + "/" + id; // :93
            bool isCurrent; // :94-98
            if (currentProvider is null || currentID is null)
            {
                isCurrent = false;
            }
            else if (currentID.Contains('/', StringComparison.Ordinal))
            {
                isCurrent = SwiftString.Equal(fullID, currentID);
            }
            else
            {
                isCurrent = SwiftString.Equal(provider, currentProvider) && SwiftString.Equal(id, currentID);
            }
            options.Add(new ModelOption( // :99-106
                fullID,
                item["name"]?.JSONValueString ?? id,
                detail: provider,
                efforts: reasoning ? ThinkingLevels : ValueList<string>.Empty,
                defaultEffort: reasoning ? "medium" : null,
                isDefault: isCurrent));
        }
        if (options.Count == 0) // :108-110
        {
            throw new ProviderError(new ProviderError.Case.Failed("Pi listed no models."));
        }
        // :111. `sorted { ($0.detail ?? "", $0.name) < ($1.detail ?? "", $1.name) }`: stable (R-COLL-1), Swift String order.
        return new ValueList<ModelOption>(options
            .OrderBy(option => option.Detail ?? "", SwiftStringComparer.Instance)
            .ThenBy(option => option.Name, SwiftStringComparer.Instance));
    }

    /// <summary>Writes the gate extension where `pi --mode rpc` can load it and returns its path. Rewritten only when its
    /// text changed, so a new build's extension replaces the old one.</summary>
    // :114-126
    public static SwiftURL InstallExtension() => InstallExtension(LocalApplicationData());

    /// <summary>PORT-ONLY test seam: <see cref="InstallExtension()"/> under an explicit OS app-data folder.</summary>
    internal static SwiftURL InstallExtension(string localApplicationData)
    {
        var directory = SupportDirectoryPath(localApplicationData); // :118
        Directory.CreateDirectory(directory); // :119, withIntermediateDirectories: true; a failure propagates.
        var file = SwiftPath.AppendingPathComponent(directory, "droppy-gate.ts"); // :120. The name is unchanged (OD-0008 C7 record).
        var data = Encoding.UTF8.GetBytes(ExtensionSource); // :121
        // :122-124. `(try? Data(contentsOf: file)) != data`: a missing or unreadable file is never equal.
        var existing = SwiftTry.Optional(() => File.ReadAllBytes(file));
        if (existing is null || !existing.AsSpan().SequenceEqual(data))
        {
            FileOps.WriteAtomic(file, data);
        }
        return FileURL(file, isDirectory: false); // :125
    }

    /// <summary>
    /// PORT-ONLY: OD-0116, OD-0122, ED-439. The app's own second extension, loaded only into a waiting standby: `/droppy-load <file>` stores the file's text in the session
    /// history as a custom message with `triggerTurn: false`, so no model turn runs. It is separate from the gate extension, whose source is pinned.
    /// </summary>
    public static SwiftURL InstallLoadExtension() => InstallLoadExtension(LocalApplicationData());

    // PORT-ONLY RR-49: only this extension is loaded into a fresh checking/opinion head.
    internal static SwiftURL InstallTextOnlyExtension(string? localApplicationData)
    {
        var directory = SupportDirectoryPath(localApplicationData ?? LocalApplicationData());
        Directory.CreateDirectory(directory);
        var file = SwiftPath.AppendingPathComponent(directory, "marrow-text-only.mjs");
        FileOps.WriteAtomic(file, Encoding.UTF8.GetBytes(PiTextOnlyHeadPolicy.Source));
        return FileURL(file, isDirectory: false);
    }

    internal static SwiftURL InstallLoadExtension(string localApplicationData)
    {
        var directory = SupportDirectoryPath(localApplicationData);
        Directory.CreateDirectory(directory);
        var file = SwiftPath.AppendingPathComponent(directory, "droppy-load.ts");
        var data = Encoding.UTF8.GetBytes(LoadExtensionSource);
        var existing = SwiftTry.Optional(() => File.ReadAllBytes(file));
        if (existing is null || !existing.AsSpan().SequenceEqual(data)) FileOps.WriteAtomic(file, data);
        return FileURL(file, isDirectory: false);
    }

    public static string LoadExtensionSource => string.Join("\n",
        "import fs from \"node:fs\";",
        "",
        "export default async function (pi) {",
        "  pi.registerCommand(\"droppy-load\", {",
        "    description: \"nth: load a recorded context file into the history without starting a turn\",",
        "    handler: async (args) => {",
        "      const text = fs.readFileSync(String(args ?? \"\").trim(), \"utf8\");",
        "      pi.sendMessage({ customType: \"droppy-handoff\", content: text, display: false }, { triggerTurn: false });",
        "    },",
        "  });",
        "}");

    /// <summary>The gate the app loads into every RPC session with `-e`. Pi itself keeps approving everything
    /// (`--approve`) or nothing (`--no-approve`); this extension gates the tools instead: every call that could change
    /// something is asked over `ctx.ui.select`, which in RPC mode surfaces on stdout as an `extension_ui_request` the app
    /// answers. Read-only tools pass straight through. Supervised asks for all of them, Auto-accept edits waves file
    /// changes through, Auto also passes shell commands that only read, and Full access returns before asking anything.
    /// Plan mode and the final report block the tools outright.</summary>
    // :128-216. A static let string: the #"""..."""# literal line by line, its four-space indentation removed and no
    // final newline, joined with "\n" so the bytes never depend on the checkout's line endings (R-STR-9). The names
    // droppy-gate, DROPPY_CODE_PI_GATE and DROPPY_CODE_MCP_BRIDGE are machine contracts, not the product name, and stay
    // as upstream (OD-0008 C7 record: "the file names droppy-gate.ts and gates\<UUID>.json are unchanged").
    public static string ExtensionSource => extensionSource;

    private static readonly string extensionSource = string.Join("\n",
        "import fs from \"node:fs\";",
        "import { Type } from \"typebox\";",
        "",
        "const readOnly = new Set([\"read\", \"grep\", \"find\", \"ls\"]);",
        "",
        "const readOnlyBinaries = new Set([",
        "  \"ls\", \"cat\", \"head\", \"tail\", \"grep\", \"rg\", \"find\", \"wc\", \"pwd\", \"echo\",",
        "  \"which\", \"stat\", \"file\", \"du\", \"tree\", \"sort\", \"uniq\", \"diff\",",
        "]);",
        "const readOnlyGit = new Set([\"status\", \"diff\", \"log\", \"show\", \"branch\", \"blame\"]);",
        "",
        "function readGate() {",
        "  try {",
        "    const file = process.env.DROPPY_CODE_PI_GATE || \"\";",
        "    return JSON.parse(fs.readFileSync(file, \"utf8\"));",
        "  } catch {",
        "    return {};",
        "  }",
        "}",
        "",
        "function isReadOnlyCommand(command) {",
        "  const segments = String(command ?? \"\").split(/&&|\\|\\||;|\\|/);",
        "  return segments.every((segment) => {",
        "    const words = segment.trim().split(/\\s+/).filter(Boolean);",
        "    if (words.length === 0) return true;",
        "    if (words[0] === \"git\") return words.length > 1 && readOnlyGit.has(words[1]);",
        "    return readOnlyBinaries.has(words[0]);",
        "  });",
        "}",
        "",
        "export default async function (pi) {",
        "  try {",
        "    const bridge = process.env.DROPPY_CODE_MCP_BRIDGE ? await import(process.env.DROPPY_CODE_MCP_BRIDGE) : null;",
        "    if (bridge) {",
        "      for (const tool of await bridge.loadMCPTools()) {",
        "        pi.registerTool({",
        "          name: \"mcp__\" + tool.server + \"__\" + tool.name,",
        "          label: tool.server + \" \U000000B7 \" + tool.name,", // U+00B7 MIDDLE DOT
        "          description: tool.description,",
        "          parameters: Type.Unsafe(tool.inputSchema),",
        "          async execute(toolCallId, params) {",
        // PORT-ONLY (web surfaces W18, ED-NEW-web-w18-3): the bridge answers as content blocks, so an MCP image reaches Pi as an image.
        "            const content = tool.callContent ? await tool.callContent(params) : [{ type: \"text\", text: await tool.call(params) }];",
        "            return { content, details: {} };",
        "          },",
        "        });",
        "      }",
        "    }",
        "  } catch (error) {",
        "    console.error(\"droppy mcp: \" + (error instanceof Error ? error.message : String(error)));",
        "  }",
        "  pi.on(\"before_agent_start\", async (event, ctx) => {",
        "    const gate = readGate();",
        "    const extra = [];",
        "    if (gate.finalReport) extra.push(\"Reply with your final report now: no tools, one message.\");",
        "    if (gate.interactionMode === \"plan\") extra.push(\"You are in plan mode: read-only. Do not edit files or run commands that change anything; investigate, then end with the plan as markdown.\");",
        "    if (typeof gate.systemPrompt === \"string\" && gate.systemPrompt.length > 0) extra.push(gate.systemPrompt);",
        "    if (extra.length === 0) return;",
        "    return { systemPrompt: event.systemPrompt + \"\\n\\n\" + extra.join(\"\\n\\n\") };",
        "  });",
        "",
        "  pi.on(\"tool_call\", async (event, ctx) => {",
        "    const gate = readGate();",
        "    if (gate.finalReport) return { block: true, reason: \"Final report: no tools\" };",
        "    if (readOnly.has(event.toolName)) return;",
        "    if (gate.interactionMode === \"plan\") return { block: true, reason: \"Plan mode is read-only\" };",
        "    if (gate.runtimeMode === \"fullAccess\") return;",
        "    const kind = event.toolName === \"bash\" ? \"command\" : (event.toolName === \"edit\" || event.toolName === \"write\") ? \"fileChange\" : \"tool\";",
        "    if (kind === \"fileChange\" && (gate.runtimeMode === \"autoAcceptEdits\" || gate.runtimeMode === \"auto\")) return;",
        "    if (kind === \"command\" && gate.runtimeMode === \"auto\" && isReadOnlyCommand(event.input && event.input.command)) return;",
        "    const input = {};",
        "    for (const [key, value] of Object.entries(event.input || {})) {",
        "      input[key] = typeof value === \"string\" && value.length > 20000 ? value.slice(0, 20000) : value;",
        "    }",
        "    const payload = { kind, toolName: event.toolName, toolCallId: event.toolCallId, input };",
        "    const answer = await ctx.ui.select(\"droppy-gate \" + JSON.stringify(payload), [\"Allow\", \"Allow for session\", \"Deny\"]);",
        "    if (answer === \"Allow\" || answer === \"Allow for session\") return undefined;",
        "    return { block: true, reason: \"The user declined this \" + (kind === \"command\" ? \"command\" : kind === \"fileChange\" ? \"file change\" : \"tool call\") };",
        "  });",
        "}");

    // URL(fileURLWithPath:) for a path this file builds. ENGINEERING DECISION mirroring Core/Models/Library.cs Url,
    // Core/Models/Timeline.cs Attachment.Url and ACPSession.DirectoryFileUrl: F.URL (SwiftURL) only builds URL(string:),
    // so the path is percent-encoded into a file:// URL by hand (the ASCII unreserved set and '/' and ':' kept
    // literal); isDirectory adds the trailing "/" a directory URL carries.
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
        return SwiftURL.Create(builder.ToString()) ?? throw SwiftTraps.Fatal("Invalid Pi support path.");
    }
}
