// Source: DroppyCode/Services/MCP/MCPBridgeSource.swift (B2 1fbf81e), :1-253.
//
// DEVIATION OD-0012 F1: clientInfo.name is "Marrow" at both initialize sites (:138, :203), never
// "Droppy Code".
//
// DEVIATION ED-042: the shipped text is not byte-identical to the Swift string. :76-152 connectStdio's
// spawn(entry.command, ...) with no shell cannot launch a .cmd or .bat shim (npx and the like) on Windows
// since Node 20's CVE-2024-27980 fix, so the text below adds a resolveSpawnTarget helper (below
// connectStdio's :74-75) that resolves entry.command against PATH using PATHEXT: an .exe target spawns
// unchanged; a .cmd or .bat target spawns through %COMSPEC% /d /s /c with a hand-quoted command line and
// windowsVerbatimArguments so Node does not re-quote. shell: true is explicitly rejected there, since it
// reopens the argument-injection hole the Node fix closed (ED-042 Q111, decided; not an owner ruling).
// Stdio wiring (["pipe", "pipe", "ignore"]), the env merge and every other line are byte-identical.

using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.MCP;

/// <summary>
/// :3-10. The JavaScript bridge that exposes the connected MCP servers' tools to the providers that load a
/// file Marrow installs: Pi (an extension) and Command Code (a mod). Both dynamic-import it and call
/// <c>loadMCPTools()</c>.
/// <para>
/// Plain ESM JavaScript (Node 18+, no npm dependencies). The server list comes from the file named by
/// <c>DROPPY_CODE_MCP_CONFIG</c> (<c>{"mcpServers": { id: {command, args, env} | {type: "http", url,
/// headers} }}</c>); when the variable is unset or the file is missing, nothing is registered. (PR-MCP-194)
/// </para>
/// </summary>
public static class MCPBridge
{
    // :12-235 mcpBridgeSource. DEVIATION OD-0012 F1 and DEVIATION ED-042 as noted above; otherwise
    // byte-identical to the Swift raw string's content (its common 4-space indentation stripped, as Swift's
    // #""" ... """# does against the closing delimiter's own indentation).
    public const string McpBridgeSource =
        """
import fs from "node:fs";
import { spawn } from "node:child_process";
import path from "node:path";

const PROTOCOL_VERSION = "2025-06-18";
const CONNECT_TIMEOUT_MS = 15000;

function configServers() {
  try {
    const file = process.env.DROPPY_CODE_MCP_CONFIG || "";
    if (!file) return {};
    const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
    if (parsed && typeof parsed === "object" && parsed.mcpServers && typeof parsed.mcpServers === "object") return parsed.mcpServers;
  } catch (error) {
    console.error("droppy mcp: could not read config: " + errorText(error));
  }
  return {};
}

function errorText(error) {
  return error instanceof Error ? error.message : String(error);
}

function contentText(content) {
  const items = Array.isArray(content) ? content : [];
  return items.map((item) => (item && item.type === "text" && typeof item.text === "string" ? item.text : "[image]")).join("\n");
}

function describe(entry, fallback) {
  if (entry && typeof entry.description === "string" && entry.description) return entry.description;
  return fallback;
}

function schemaOf(entry) {
  if (entry && entry.inputSchema && typeof entry.inputSchema === "object") return entry.inputSchema;
  return { type: "object" };
}

function withTimeout(promise) {
  let timer = null;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("timed out")), CONNECT_TIMEOUT_MS);
  });
  return Promise.race([promise, timeout]).finally(() => { if (timer) clearTimeout(timer); });
}

function wrapTools(server, entries, callTool) {
  return entries.map((entry) => ({
    server,
    name: String(entry.name),
    description: describe(entry, String(entry.name)),
    inputSchema: schemaOf(entry),
    call: async (args) => {
      try {
        const result = await callTool(entry.name, args && typeof args === "object" ? args : {});
        if (result && result.isError) return "Error: " + (contentText(result.content) || "tool failed");
        return contentText(result ? result.content : []);
      } catch (error) {
        return "Error: " + errorText(error);
      }
    },
  }));
}

// ED-042: spawn(entry.command) with no shell cannot launch a .cmd/.bat shim (such as npx) on
// Windows since Node 20's CVE-2024-27980 fix. Resolve the command against PATH using PATHEXT;
// an .exe target spawns unchanged, a .cmd/.bat target spawns through %COMSPEC% /d /s /c with a
// hand-quoted command line and windowsVerbatimArguments so Node does not re-quote. shell: true is
// rejected: it reopens the argument-injection hole the Node fix closed.
function resolveSpawnTarget(command, args) {
  const exts = (process.env.PATHEXT || ".COM;.EXE;.BAT;.CMD").split(";").filter(Boolean);
  const dirs = (process.env.PATH || process.env.Path || "").split(path.delimiter).filter(Boolean);
  const hasSeparator = command.includes("/") || command.includes("\\");
  const bases = hasSeparator ? [command] : dirs.map((dir) => path.join(dir, command));
  let resolved = null;
  for (const base of bases) {
    if (path.extname(base)) {
      if (fs.existsSync(base)) { resolved = base; break; }
      continue;
    }
    for (const ext of exts) {
      const candidate = base + ext;
      if (fs.existsSync(candidate)) { resolved = candidate; break; }
    }
    if (resolved) break;
  }
  if (!resolved) resolved = command;
  const extension = path.extname(resolved).toLowerCase();
  if (extension === ".cmd" || extension === ".bat") {
    // cmd.exe toggles its quote state at every quote and knows no backslash escape, so an embedded quote is doubled
    // (the state is unchanged and & | < > ^ stay argument data, as spawn's no-shell argv keeps them, :79-84). A trailing
    // run of backslashes is doubled so it cannot escape the closing quote, and % is split so %VAR% never expands.
    const quote = (value) => "\"" + String(value).replace(/(\\*)"/g, '$1$1""').replace(/(\\+)$/, "$1$1").replace(/%/g, "%%cd:~,%") + "\"";
    // /s strips the first and last quote of the line, so the whole line is wrapped in one more pair.
    const commandLine = "\"" + [quote(resolved), ...args.map(quote)].join(" ") + "\"";
    return { file: process.env.COMSPEC || "cmd.exe", args: ["/d", "/s", "/c", commandLine], verbatim: true };
  }
  return { file: resolved, args, verbatim: false };
}

function connectStdio(server, entry) {
  return new Promise((resolve, reject) => {
    let child = null;
    try {
      const target = resolveSpawnTarget(entry.command, Array.isArray(entry.args) ? entry.args : []);
      child = spawn(target.file, target.args, {
        env: { ...process.env, ...((entry.env && typeof entry.env === "object") ? entry.env : {}) },
        stdio: ["pipe", "pipe", "ignore"],
        windowsVerbatimArguments: target.verbatim,
      });
    } catch (error) {
      reject(error);
      return;
    }
    let id = 0;
    const pending = new Map();
    let buffer = "";
    let settled = false;
    const fail = (error) => {
      for (const resolvePending of pending.values()) resolvePending({ error: { message: errorText(error) } });
      pending.clear();
      if (settled) return;
      settled = true;
      reject(error);
    };
    child.on("error", fail);
    child.on("exit", () => fail(new Error("process exited")));
    child.stdin.on("error", () => {});
    child.stdout.on("data", (data) => {
      buffer += data.toString("utf8");
      let index = -1;
      while ((index = buffer.indexOf("\n")) >= 0) {
        const line = buffer.slice(0, index).trim();
        buffer = buffer.slice(index + 1);
        if (!line) continue;
        let message = null;
        try { message = JSON.parse(line); } catch { continue; }
        if (message && message.id !== undefined && message.id !== null && pending.has(message.id)) {
          const resolvePending = pending.get(message.id);
          pending.delete(message.id);
          resolvePending(message);
        }
      }
    });
    const send = (method, params) => new Promise((resolveSend, rejectSend) => {
      const message = { jsonrpc: "2.0", id: id++, method };
      if (params !== undefined) message.params = params;
      pending.set(message.id, resolveSend);
      child.stdin.write(JSON.stringify(message) + "\n", (error) => {
        if (error) { pending.delete(message.id); rejectSend(error); }
      });
    });
    const request = async (method, params) => {
      const response = await send(method, params);
      if (response && response.error) throw new Error(String((response.error && response.error.message) || "request failed"));
      return response ? response.result : undefined;
    };
    const notify = (method, params) => {
      const message = { jsonrpc: "2.0", method };
      if (params !== undefined) message.params = params;
      child.stdin.write(JSON.stringify(message) + "\n");
    };
    const callTool = (name, args) => request("tools/call", { name, arguments: args });
    (async () => {
      await request("initialize", { protocolVersion: PROTOCOL_VERSION, capabilities: {}, clientInfo: { name: "nth" } });
      notify("notifications/initialized", {});
      let cursor = undefined;
      const tools = [];
      for (;;) {
        const result = await request("tools/list", cursor === undefined ? {} : { cursor });
        for (const tool of ((result && result.tools) || [])) tools.push(tool);
        cursor = result ? result.nextCursor : undefined;
        if (!cursor) break;
      }
      settled = true;
      resolve(wrapTools(server, tools, callTool));
    })().catch(fail);
  });
}

async function postMessage(url, headers, payload, sessionId) {
  const requestHeaders = { ...(headers || {}), "Content-Type": "application/json", Accept: "application/json, text/event-stream" };
  if (sessionId) requestHeaders["mcp-session-id"] = sessionId;
  const response = await fetch(url, { method: "POST", headers: requestHeaders, body: JSON.stringify(payload) });
  const nextSession = response.headers.get("mcp-session-id") || sessionId || null;
  const contentType = response.headers.get("content-type") || "";
  let message = null;
  if (contentType.includes("text/event-stream")) {
    const text = await response.text();
    for (const line of text.split("\n")) {
      const trimmed = line.trim();
      if (!trimmed.startsWith("data:")) continue;
      const data = trimmed.slice("data:".length).trim();
      if (!data || data === "[DONE]") continue;
      try {
        const parsed = JSON.parse(data);
        if (parsed && typeof parsed === "object" && ("result" in parsed || "error" in parsed)) { message = parsed; break; }
      } catch {}
    }
  } else {
    const text = await response.text();
    try { message = text ? JSON.parse(text) : null; } catch { message = null; }
  }
  if (!message) throw new Error("empty response (status " + response.status + ")");
  return { message, sessionId: nextSession };
}

function connectRemote(server, entry) {
  return (async () => {
    const url = entry.url;
    const headers = (entry.headers && typeof entry.headers === "object") ? entry.headers : {};
    let sessionId = null;
    let id = 0;
    const request = async (method, params) => {
      const payload = { jsonrpc: "2.0", id: id++, method };
      if (params !== undefined) payload.params = params;
      const outcome = await postMessage(url, headers, payload, sessionId);
      sessionId = outcome.sessionId;
      if (outcome.message.error) throw new Error(String((outcome.message.error && outcome.message.error.message) || "request failed"));
      return outcome.message.result;
    };
    const notify = async (method, params) => {
      const payload = { jsonrpc: "2.0", method };
      if (params !== undefined) payload.params = params;
      try {
        const outcome = await postMessage(url, headers, payload, sessionId);
        sessionId = outcome.sessionId;
      } catch {}
    };
    await request("initialize", { protocolVersion: PROTOCOL_VERSION, capabilities: {}, clientInfo: { name: "nth" } });
    await notify("notifications/initialized", {});
    let cursor = undefined;
    const tools = [];
    for (;;) {
      const result = await request("tools/list", cursor === undefined ? {} : { cursor });
      for (const tool of ((result && result.tools) || [])) tools.push(tool);
      cursor = result ? result.nextCursor : undefined;
      if (!cursor) break;
    }
    const callTool = (name, args) => request("tools/call", { name, arguments: args });
    return wrapTools(server, tools, callTool);
  })();
}

export async function loadMCPTools() {
  const servers = configServers();
  const tools = [];
  for (const server of Object.keys(servers)) {
    const entry = servers[server];
    try {
      if (entry && typeof entry === "object" && entry.type === "http" && typeof entry.url === "string") {
        tools.push(...await withTimeout(connectRemote(server, entry)));
      } else if (entry && typeof entry === "object" && typeof entry.command === "string") {
        tools.push(...await withTimeout(connectStdio(server, entry)));
      }
    } catch (error) {
      console.error("droppy mcp: skipping " + server + ": " + errorText(error));
    }
  }
  return tools;
}
""";

    /// <summary>
    /// Writes the bridge where the Pi extension and the Command Code mod can import it, and returns its
    /// URL. Rewritten only when its text changed, so a new build's bridge replaces the old one. (:237-239)
    /// </summary>
    // :240-251 installBridge. windows_path (PR-MCP-193): the base is the OS app-data root
    // (Environment.SpecialFolder.LocalApplicationData), never the capture-aware Storage.root; there is no
    // macOS-style Library/Application Support fallback (:241-242 not ported, no Windows counterpart). The
    // directory is "<base>/Marrow Code/mcp" - the port's data identity "Marrow Code" (OD-0008 C7, matching
    // SettingsStore.ProductionPath and HangWatchdog's log folder), lower-case "mcp" as the windows_path names
    // it, kept apart from the provider configs under Storage.root/mcp. The returned URL is a file:// URL
    // (windows_path): a bare drive-letter path fails a Node dynamic import.
    public static Uri InstallBridge() => InstallBridge(LocalApplicationData());

    private static string LocalApplicationData() => DevDataRoot.LocalApplicationData(); // ED-212

    /// <summary>
    /// <see cref="InstallBridge()"/> with the base directory given explicitly (R-TEST-9: nothing touches the
    /// real %LOCALAPPDATA%\Marrow Code in a test), following <c>HangWatchdog.ReportsFolder(localApplicationData)</c>'s
    /// same split of the OS-folder resolution from the testable directory logic.
    /// </summary>
    public static Uri InstallBridge(string localApplicationData)
    {
        // :243 directory. DEVIATION: "Marrow Code" (OD-0008 C7) replaces AppInfo.name interpolation;
        // windows_path names the leaf "mcp" (lower case).
        var directory = SwiftPath.AppendingPathComponent(SwiftPath.AppendingPathComponent(localApplicationData, AppInfo.DataFolderName), "mcp"); // OD-0135
        // :244 createDirectory(at:withIntermediateDirectories: true).
        Directory.CreateDirectory(directory);
        // :245 file.
        var file = SwiftPath.AppendingPathComponent(directory, "droppy-mcp-bridge.mjs");
        // :246 Data(mcpBridgeSource.utf8).
        var data = Encoding.UTF8.GetBytes(McpBridgeSource);
        // :247-249. Written atomically ONLY when the file's current contents differ; (try? Data(contentsOf:))
        // is nil for a missing file, which is never equal, so a first install always writes.
        var existing = SwiftTry.Optional(() => File.ReadAllBytes(file));
        if (existing is null || !existing.AsSpan().SequenceEqual(data))
        {
            FileOps.WriteAtomic(file, data);
        }
        // :250 return file. A file:// URL, not a bare path (windows_path).
        return new Uri(file);
    }
}
