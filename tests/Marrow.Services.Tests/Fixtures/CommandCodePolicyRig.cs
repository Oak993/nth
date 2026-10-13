using Marrow.Core.Support;
using Marrow.Services.Providers;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class CommandCodePolicyRig : IDisposable
{
    private readonly StorageScratchFolder folder = new();
    internal async Task<JSONValue> Run()
    {
        var policy = Path.Combine(folder.Root, "text-only.mjs");
        var script = Path.Combine(folder.Root, "policy-test.mjs");
        File.WriteAllText(policy, CommandCodeTextHead.Policy);
        File.WriteAllText(script, """
            import policy from './text-only.mjs';
            const proofs = [];
            const write = process.stdout.write.bind(process.stdout);
            process.stdout.write = x => { proofs.push(JSON.parse(x)); return true; };
            let hook, executions = 0;
            policy({hooks: x => { if(hook) throw Error('duplicate'); hook = x.beforeToolCall; }});
            const names = ['read_file','read_directory','glob','grep','run_command','agent','web_fetch','ask_user_question','mcp__server__write','future_unknown_tool'];
            const blocked = [];
            for (const toolName of names) {
              const decision = await hook({toolName, input:{}},{});
              if (!decision?.block) executions++;
              blocked.push(decision?.block === true);
            }
            let refused = false;
            try { policy({}); } catch { refused = true; }
            write(JSON.stringify({proofs, blocked, executions, refused}));
            """);
        var result = await Shell.Run(@"C:\Program Files\nodejs\node.exe", [script], @in: folder.Root,
            environment: new Dictionary<string,string> { [CommandCodeTextHead.NonceKey] = "fixture-nonce" }, timeout: 10);
        Assert.Equal(0, result.Status);
        return JSONValue.Parse(result.Output)!;
    }
    public void Dispose() => folder.Dispose();
}
