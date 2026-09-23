// PORT-ONLY test fixture (R-TEST-8): a scripted stand-in for a local MCP server that the bridge's
// connectStdio (MCPBridgeSource.swift:76-152) spawns and talks JSON-RPC-over-stdio to. Behaviour is driven
// entirely by environment variables so one script serves every stdio scenario the bridge test needs:
//
//   MCP_FAKE_MODE     "normal" (default) or "crash" (exit(1) before reading anything, for the "process
//                      exited" fail path).
//   MCP_FAKE_TOOL      base tool name (default "tool"). tools/list's final page returns "<name>" (full
//                      description and inputSchema), "<name>-bare" (no description, no inputSchema: proves
//                      describe()'s and schemaOf()'s fallbacks) and the "-error"/"-emptyerror"/"-image"/
//                      "-mixed"/"-throw" variants tools/call dispatches on below.
//   MCP_FAKE_PAGES     "2" pages tools/list: the first page returns only "<name>-page1" with a nextCursor;
//                      the real tools above are the second page. Any other value (or unset) is one page.
//   MCP_FAKE_LOG       a file path; every received line is appended as one JSON record
//                      {id, tag, method, params, raw}, tag from MCP_FAKE_TAG, for dialect assertions.
//   MCP_FAKE_TAG       tags this server's log lines so a shared log file can be read back per server.
//
// tools/call dispatches on a suffix of the tool name so one server proves every wrapTools() content shape:
//   -error      isError: true, content: [{type:"text", text:"boom"}]              -> "Error: boom"
//   -emptyerror isError: true, content: []                                        -> "Error: tool failed"
//   -image      content: [{type:"image"}]                                          -> "[image]"
//   -mixed      content: [{type:"text", text:"part1"}, {type:"image"}]             -> "part1\n[image]"
//   -throw      a top-level JSON-RPC error {message:"tool threw"}                  -> "Error: tool threw"
//   (anything else) content: [{type:"text", text: "called:" + JSON.stringify(arguments)}]

import fs from "node:fs";
import readline from "node:readline";

if ((process.env.MCP_FAKE_MODE || "normal") === "crash") {
  process.exit(1);
}

const toolName = process.env.MCP_FAKE_TOOL || "tool";
const twoPages = process.env.MCP_FAKE_PAGES === "2";
const logPath = process.env.MCP_FAKE_LOG || "";
const tag = process.env.MCP_FAKE_TAG || "";

function log(record) {
  if (!logPath) return;
  fs.appendFileSync(logPath, JSON.stringify({ tag, ...record }) + "\n");
}

function send(message) {
  process.stdout.write(JSON.stringify(message) + "\n");
}

const rl = readline.createInterface({ input: process.stdin, terminal: false });
rl.on("line", (raw) => {
  const line = raw.trim();
  if (!line) return;
  const message = JSON.parse(line);
  log({ id: message.id, method: message.method, params: message.params, raw: line });
  if (message.method === "initialize") {
    send({ jsonrpc: "2.0", id: message.id, result: { protocolVersion: "2025-06-18", capabilities: {} } });
    return;
  }
  if (message.method === "notifications/initialized") return; // a notification: no reply.
  if (message.method === "tools/list") {
    const cursor = message.params ? message.params.cursor : undefined;
    if (twoPages && cursor === undefined) {
      send({ jsonrpc: "2.0", id: message.id, result: { tools: [{ name: toolName + "-page1" }], nextCursor: "page2" } });
      return;
    }
    send({
      jsonrpc: "2.0",
      id: message.id,
      result: {
        tools: [
          { name: toolName, description: "does the thing", inputSchema: { type: "object", properties: { x: { type: "string" } } } },
          { name: toolName + "-bare" },
          { name: toolName + "-error" },
          { name: toolName + "-emptyerror" },
          { name: toolName + "-image" },
          { name: toolName + "-mixed" },
          { name: toolName + "-throw" },
        ],
        nextCursor: null,
      },
    });
    return;
  }
  if (message.method === "tools/call") {
    const name = message.params.name;
    if (name.endsWith("-error")) {
      send({ jsonrpc: "2.0", id: message.id, result: { isError: true, content: [{ type: "text", text: "boom" }] } });
    } else if (name.endsWith("-emptyerror")) {
      send({ jsonrpc: "2.0", id: message.id, result: { isError: true, content: [] } });
    } else if (name.endsWith("-image")) {
      send({ jsonrpc: "2.0", id: message.id, result: { content: [{ type: "image" }] } });
    } else if (name.endsWith("-mixed")) {
      send({ jsonrpc: "2.0", id: message.id, result: { content: [{ type: "text", text: "part1" }, { type: "image" }] } });
    } else if (name.endsWith("-throw")) {
      send({ jsonrpc: "2.0", id: message.id, error: { message: "tool threw" } });
    } else {
      send({ jsonrpc: "2.0", id: message.id, result: { content: [{ type: "text", text: "called:" + JSON.stringify(message.params.arguments) }] } });
    }
    return;
  }
  send({ jsonrpc: "2.0", id: message.id, result: {} });
});
