// PORT-ONLY: RR-49 / ED-460. Pi's CLI disables tools and resource discovery; an app-owned extension verifies the actual active set and blocks every call.
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

internal static class PiTextOnlyHeadPolicy
{
    internal const string Command = "marrow-text-only-proof";

    internal static List<string> Arguments(string extension, string? model, string? effort)
    {
        var args = new List<string> { "--mode", "rpc", "--no-tools", "--no-extensions", "--no-skills", "--no-prompt-templates", "--no-themes", "--no-context-files", "-e", extension };
        if (model is { Length: > 0 } selected && selected.IndexOf('/', StringComparison.Ordinal) is var slash && slash > 0)
            args.AddRange(["--provider", selected[..slash], "--model", selected[(slash + 1)..]]);
        if (effort is { } thinking && PiCLI.ThinkingLevels.Contains(thinking)) args.AddRange(["--thinking", thinking]);
        return args;
    }

    internal static bool Verify(JSONValue reply, string nonce)
    {
        var messages = (reply["data"] ?? reply)["messages"]?.JSONValueArray;
        return messages is { Count: 1 } && messages[0]["role"]?.JSONValueString == "custom"
            && messages[0]["customType"]?.JSONValueString == Command && ToolText.Blocks(messages[0]["content"]) == nonce
            && messages[0]["details"]?["tools"]?.JSONValueArray is { Count: 0 };
    }

    // Plain JS is accepted as an explicit Pi extension and can be executed with a fake ExtensionAPI in tests.
    internal static string Source => """
        export default function (pi) {
          const lock = () => {
            pi.setActiveTools([]);
            if (pi.getActiveTools().length !== 0) throw new Error("Text-only tool policy was not applied");
          };
          pi.on("session_start", async () => lock());
          pi.on("before_agent_start", async () => lock());
          pi.on("tool_call", async () => ({ block: true, reason: "Text-only head: no tools" }));
          pi.registerCommand("marrow-text-only-proof", {
            description: "Marrow private text-only runtime proof",
            handler: async (nonce) => {
              lock();
              pi.sendMessage({ customType: "marrow-text-only-proof", content: nonce, details: { tools: pi.getActiveTools() }, display: false }, { triggerTurn: false });
            },
          });
        }
        """;
}
