// PORT-ONLY test fixture (web surfaces W18): a scripted stand-in for a local MCP server that the bridge's connectStdio
// spawns, with the richer answers the upgraded client understands. It lists four tools and answers tools/call by name:
//   shown      listed to the model; answers a text block and an image block with data.
//   hidden     _meta.ui.visibility is ["app"]: the bridge must not offer it to the model.
//   structure  answers only structuredContent (no content blocks).
//   links      answers a resource_link, an embedded text resource and an embedded ui:// page.
//   failing    answers isError with a text block.
import readline from "node:readline";

function send(message) {
  process.stdout.write(JSON.stringify(message) + "\n");
}

const rl = readline.createInterface({ input: process.stdin, terminal: false });
rl.on("line", (raw) => {
  const line = raw.trim();
  if (!line) return;
  const message = JSON.parse(line);
  if (message.method === "initialize") {
    send({ jsonrpc: "2.0", id: message.id, result: { protocolVersion: "2025-06-18", capabilities: {} } });
    return;
  }
  if (message.method === "notifications/initialized") return;
  if (message.method === "tools/list") {
    send({
      jsonrpc: "2.0",
      id: message.id,
      result: {
        tools: [
          { name: "shown", description: "shows a chart" },
          { name: "hidden", description: "app only", _meta: { ui: { visibility: ["app"] } } },
          { name: "structure" },
          { name: "links" },
          { name: "failing" },
        ],
      },
    });
    return;
  }
  if (message.method === "tools/call") {
    const name = message.params.name;
    let result = { content: [{ type: "text", text: "unknown" }] };
    if (name === "shown") {
      result = { content: [{ type: "text", text: "the chart" }, { type: "image", data: "iVBORw0KGgo=", mimeType: "image/png" }, { type: "image" }] };
    } else if (name === "structure") {
      result = { content: [], structuredContent: { answer: 42 } };
    } else if (name === "links") {
      result = {
        content: [
          { type: "resource_link", uri: "file:///report.pdf", name: "report" },
          { type: "resource", resource: { uri: "file:///inline.txt", text: "inline words" } },
          { type: "resource", resource: { uri: "ui://board/view.html", text: "<html>page</html>" } },
          { type: "audio", data: "AAAA", mimeType: "audio/wav" },
        ],
      };
    } else if (name === "failing") {
      result = { isError: true, content: [{ type: "text", text: "boom" }, { type: "image", data: "AAAA", mimeType: "image/png" }] };
    }
    send({ jsonrpc: "2.0", id: message.id, result });
    return;
  }
  send({ jsonrpc: "2.0", id: message.id, result: {} });
});
