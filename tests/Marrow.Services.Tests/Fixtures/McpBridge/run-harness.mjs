// PORT-ONLY test fixture (R-TEST-8). Drives the actual installed bridge file (the bytes MCPBridge.InstallBridge
// wrote) end to end: imports it as MCPBridgeSource.swift's own consumers would (dynamic import of a file:// URL,
// :250) and calls loadMCPTools(). Remote (HTTP) scenarios are served by a tiny in-process fake MCP server so the
// whole exchange - request headers, session id, SSE and plain JSON bodies - is deterministic and local (R-TEST-10:
// no network beyond loopback). Every fact the C# test asserts on is a request the fake server actually received,
// or a value the bridge module actually returned or printed, never a guess about what it must have done.
//
// Configuration, all via environment variables the C# test sets before spawning this script:
//   MCP_HARNESS_BRIDGE_PATH      the installed bridge .mjs file (MCPBridge.InstallBridge()'s return value).
//   DROPPY_CODE_MCP_CONFIG       where to write the filled-in mcpServers config (the bridge itself reads this
//                                 var too, :19-29 configServers).
//   MCP_HARNESS_CONFIG_TEMPLATE  the config JSON text, with "{{HTTP_URL}}" replaced by this harness's fake
//                                 server's base URL before it is written to DROPPY_CODE_MCP_CONFIG. Omitted
//                                 when the scenario needs no fake HTTP server (stdio-only scenarios write
//                                 their own config file directly and leave this unset).
//   MCP_HARNESS_CALLS            JSON array of {"tool": "<name>", "args": {...}} describing which returned
//                                 tools to additionally .call(args) and capture the result of.
//
// Prints one line of JSON to stdout: {"tools":[{server,name,description,inputSchema,callResult}, ...],
// "httpRequests":[{path,method,headers,body}, ...]}. httpRequests is empty when no fake HTTP server ran.

import fs from "node:fs";
import http from "node:http";
import { pathToFileURL } from "node:url";

const bridgePath = process.env.MCP_HARNESS_BRIDGE_PATH;
const configPath = process.env.DROPPY_CODE_MCP_CONFIG;
const template = process.env.MCP_HARNESS_CONFIG_TEMPLATE;
const callSpecs = JSON.parse(process.env.MCP_HARNESS_CALLS || "[]");

const httpRequests = [];
let server = null;
let baseUrl = "";

function sendJson(res, status, obj, extraHeaders = {}) {
  res.writeHead(status, { "Content-Type": "application/json", ...extraHeaders });
  res.end(JSON.stringify(obj));
}

function sendSse(res, obj, extraHeaders = {}) {
  res.writeHead(200, { "Content-Type": "text/event-stream", ...extraHeaders });
  // A [DONE] frame first, to prove postMessage skips it (:167), then the real frame.
  res.end("data: [DONE]\n\ndata: " + JSON.stringify(obj) + "\n\n");
}

// Per-path scripted fake MCP server. Every path keeps its own session id and per-path call sequence, so one
// server instance can host every remote scenario the test needs in a single loadMCPTools() run - which also
// proves the servers in a config run strictly in object order and a failure on one never stops the rest
// (loadMCPTools :218-232).
const sessions = new Map(); // path -> { sequence, session }

function stateFor(path) {
  if (!sessions.has(path)) sessions.set(path, { sequence: 0, session: "sess-" + path.replace(/\W+/g, "") });
  return sessions.get(path);
}

if (template) {
  server = http.createServer((req, res) => {
    const url = new URL(req.url, "http://127.0.0.1");
    const path = url.pathname;
    let raw = "";
    req.on("data", (chunk) => { raw += chunk; });
    req.on("end", () => {
      const message = raw ? JSON.parse(raw) : null;
      httpRequests.push({ path, method: req.method, headers: req.headers, body: message });
      const state = stateFor(path);
      state.sequence += 1;
      handle(path, state, message, res);
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  baseUrl = "http://127.0.0.1:" + server.address().port;
}

function handle(path, state, message, res) {
  if (path === "/empty" && message.method === "initialize") {
    res.writeHead(204);
    res.end();
    return;
  }
  if (message.method === "initialize") {
    // The first response carries the session id; :158 the bridge adopts it for every later request.
    sendJson(res, 200, { jsonrpc: "2.0", id: message.id, result: {} }, { "mcp-session-id": state.session });
    return;
  }
  if (message.method === "notifications/initialized") {
    if (path === "/badnotify") {
      res.writeHead(500, { "Content-Type": "text/plain" });
      res.end("nope"); // not JSON: postMessage's message stays null -> "empty response" -> notify's catch{} swallows it.
      return;
    }
    // No mcp-session-id header on this response either: :158 must keep the one initialize gave.
    sendJson(res, 200, { jsonrpc: "2.0", id: message.id, result: {} });
    return;
  }
  if (message.method === "tools/list") {
    const cursor = message.params ? message.params.cursor : undefined;
    if (path === "/noerrormsg") {
      sendJson(res, 200, { jsonrpc: "2.0", id: message.id, error: {} }); // no "message" field: "request failed" fallback.
      return;
    }
    const isSse = path === "/sse";
    if (!cursor) {
      const payload = { jsonrpc: "2.0", id: message.id, result: { tools: [{ name: "remote-1" }], nextCursor: "page2" } };
      // No mcp-session-id header here either: still proves retention across a header-less response.
      if (isSse) sendSse(res, payload);
      else sendJson(res, 200, payload);
      return;
    }
    const payload = { jsonrpc: "2.0", id: message.id, result: { tools: [{ name: "remote-2", description: "second page" }, { name: "remote-error" }], nextCursor: null } };
    if (isSse) sendSse(res, payload);
    else sendJson(res, 200, payload);
    return;
  }
  if (message.method === "tools/call") {
    const name = message.params.name;
    if (name.endsWith("-error")) {
      sendJson(res, 200, { jsonrpc: "2.0", id: message.id, error: { message: "server refused" } });
      return;
    }
    sendJson(res, 200, { jsonrpc: "2.0", id: message.id, result: { content: [{ type: "text", text: "ok:" + name }] } });
    return;
  }
  sendJson(res, 200, { jsonrpc: "2.0", id: message.id, result: {} });
}

if (template) {
  fs.writeFileSync(configPath, template.split("{{HTTP_URL}}").join(baseUrl));
}

const { loadMCPTools } = await import(pathToFileURL(bridgePath).href);
const tools = await loadMCPTools();

const summary = [];
for (const tool of tools) {
  const spec = callSpecs.find((c) => c.tool === tool.name);
  const callResult = spec ? await tool.call(spec.args) : null;
  // W18: the same call answered as content blocks (text and images), when the bridge offers it.
  const callContent = spec && typeof tool.callContent === "function" ? await tool.callContent(spec.args) : null;
  summary.push({ server: tool.server, name: tool.name, description: tool.description, inputSchema: tool.inputSchema, callResult, callContent });
}

if (server) await new Promise((resolve) => server.close(resolve));

console.log(JSON.stringify({ tools: summary, httpRequests }));

// The bridge kills nothing it spawns (upstream defect 14, PR-MCP-195, reproduced deliberately): a stdio
// scenario's child stays alive with its stdio pipes open, which would otherwise keep this process's event
// loop alive forever. Everything this script needs is already captured and printed above.
process.exit(0);
