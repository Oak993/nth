// PORT-ONLY: DESIGN.md 4.3; ED-NEW-dextra-open-7. Private delegate process; never a working conversation.
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Core.Port;
using System.Text;
namespace Marrow.Services.Providers;

internal interface ICommandCodeTextProcess
{
    void Start();
    void Write(string text);
    void CloseInput();
    IAsyncEnumerable<JSONValue> Messages { get; }
    Task<int> Exit();
    Task Stop();
}

public static class CommandCodeTextHead
{
    internal const string NonceKey = "NTH_COMMANDCODE_TEXT_NONCE";
    // The same documented cmd.hooks/beforeToolCall blocking contract as CommandCodeAPI.ApprovalMod,
    // with no read-only exceptions, approval files, MCP bridge or asynchronous tool registration.
    internal const string Policy = """
        export default function(cmd) {
          const nonce = process.env.NTH_COMMANDCODE_TEXT_NONCE;
          if (!nonce || typeof cmd?.hooks !== 'function') throw new Error('No text-only policy gate');
          cmd.hooks({ beforeToolCall: async () => ({block:true, additionalContext:'This private turn returns text only.'}) });
          process.stdout.write(JSON.stringify({type:'nth_text_policy',nonce,tools:[]})+'\n');
        }
        """;
    // ED-NEW-secfinal-23: where the private answer folders are made (a test's data folder, else the temp folder). ED-NEW-secfinal-27: a seam, so a
    // test can give its own async flow a folder of its own; every answer and every retry reads leftovers from the one folder it is given, so tests that
    // share the data folder can retry (and clean) each other's leftovers.
    private static readonly Ambient<string> privateRoot = new(() => Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? Path.GetTempPath());

    private static string PrivateRoot() => privateRoot.Current;

    /// <summary>For tests: answers and retries of the calling async flow use <paramref name="root"/> until the result is disposed.</summary>
    internal static IDisposable OverridePrivateRoot(string root) => privateRoot.Override(root);

    /// <summary>
    /// ED-NEW-secfinal-23: removes the private answer folders an earlier run could not (the retry that otherwise waits for the next Command
    /// Code answer). Called at Dextra's start, from a worker thread.
    /// </summary>
    public static void RetryLeftoverCleanup() => RetryLeftoverCleanup(null);

    /// <summary>The same for <paramref name="root"/>, the folder the answers are made in (a test's); null is the usual one.</summary>
    internal static void RetryLeftoverCleanup(string? root) => CommandCodeTextFiles.RetryPending(root ?? PrivateRoot());

    /// <summary>The same, on a worker thread: the caller (a start on the main thread) does no file work.</summary>
    internal static Task RetryLeftoverCleanupAsync(string? root = null) => Task.Run(() => RetryLeftoverCleanup(root));

    public static Task<string> RunAsync(SessionConfiguration configuration, string prompt, Func<bool> permitted,
        CancellationToken cancel, Action<TokenSpend>? spend = null) => RunAsync(configuration, prompt, permitted, cancel, spend, null);

    internal static async Task<string> RunAsync(SessionConfiguration configuration, string prompt, Func<bool> permitted,
        CancellationToken cancel, Action<TokenSpend>? spend,
        Func<SessionConfiguration, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, ICommandCodeTextProcess>? factory)
    {
        cancel.ThrowIfCancellationRequested();
        if (!permitted()) throw new InvalidOperationException("Permission to use this account changed.");
        if (configuration.Provider != ProviderKind.Commandcode || configuration.Executable is null || string.IsNullOrWhiteSpace(configuration.Model))
            throw new ArgumentException("The private Command Code turn requires its executable and selected model.");
        if (configuration.ResumeID is not null || configuration.ResumeAt is not null || configuration.ResumePath is not null
            || configuration.Transcript.Count != 0 || configuration.Hydra is not null || configuration.IsHydraHead
            || configuration.SkillGeneration is not null || configuration.SkillMcpExecutable is not null)
            throw new ArgumentException("A delegate requires a fresh private configuration.");
        var folder = Path.Combine(PrivateRoot(), "commandcode-text-" + Guid.NewGuid().ToString("N"));
        var policy = Path.Combine(folder, "text-only.mjs");
        ICommandCodeTextProcess? process = null;
        FileStream? ownedPolicy = null;
        CommandCodeTextFiles? ownedFiles = null;
        try
        {
            var nonce = Guid.NewGuid().ToString("N");
            var environment = await Task.Run(() =>
            {
                // Resolve this chosen account's credential before isolating HOME. Never copy its config, MCP, skills or mods.
                var key = CommandCodeAPI.ApiKey(configuration.Environment);
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Command Code has no credential for this account.");
                ownedFiles = CommandCodeTextFiles.Begin(Path.GetDirectoryName(folder)!, Path.GetFileName(folder)["commandcode-text-".Length..]);
                // Keep ownership of the opened file, even if the child renames its directory or substitutes a junction.
                ownedPolicy = new FileStream(policy, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete,
                    4096, FileOptions.DeleteOnClose);
                ownedPolicy.Write(Encoding.UTF8.GetBytes(Policy));
                ownedPolicy.Flush();
                return PrivateEnvironment(configuration.Environment, folder, key, nonce);
            }, cancel);
            cancel.ThrowIfCancellationRequested();
            var arguments = new List<string> { "-p", "--output-format", "json", "--skip-onboarding", "--no-auto-update", "--trust",
                "--permission-mode", "plan", "--max-turns", "1", "--model", configuration.Model!, "--mod", policy };
            process = factory is null ? new TextProcess(configuration.Executable.Path, arguments, folder, environment) : factory(configuration, arguments, environment);
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await using var messages = process.Messages.GetAsyncEnumerator(timeout.Token);
            // No stdin question, or even an empty model turn, until this run's private gate acknowledges registration.
            if (!await messages.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                || messages.Current["type"]?.JSONValueString != "nth_text_policy"
                || messages.Current["nonce"]?.JSONValueString != nonce
                || messages.Current["tools"] is not JSONValue.Array { Value.Count: 0 })
                throw new InvalidOperationException("Command Code did not prove its text-only tool gate; no question was sent.");
            cancel.ThrowIfCancellationRequested();
            if (!permitted()) throw new InvalidOperationException("Permission to use this account changed.");
            process.Write(prompt);
            process.CloseInput();
            var answer = new StringBuilder();
            var succeeded = false;
            var events = 0;
            while (await messages.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
            {
                cancel.ThrowIfCancellationRequested();
                if (!permitted()) throw new InvalidOperationException("Permission to use this account changed.");
                var message = messages.Current;
                if (++events > 4096 || message.CompactString.Length > 64_000) throw new InvalidOperationException("Command Code returned too much data.");
                if (succeeded) throw new InvalidOperationException("Command Code returned data after its result.");
                if (message["type"]?.JSONValueString == "result")
                {
                    if (message["subtype"]?.JSONValueString != "success" || message["error"] is { IsNull: false })
                        throw new InvalidOperationException("Command Code did not finish the text answer.");
                    succeeded = true;
                    continue;
                }
                if (message["type"]?.JSONValueString != "event") throw new InvalidOperationException("Unexpected Command Code reply.");
                var entry = message["event"];
                switch (entry?["type"]?.JSONValueString)
                {
                    case "text_delta":
                        answer.Append(entry?["delta"]?.JSONValueString);
                        if (answer.Length > 32_000) throw new InvalidOperationException("The answer was too long.");
                        break;
                    case "run_end":
                        if (answer.Length == 0) answer.Append(entry?["result"]?["finalText"]?.JSONValueString);
                        if (answer.Length > 32_000) throw new InvalidOperationException("The answer was too long.");
                        break;
                    case "message_end":
                        var appendContent = answer.Length == 0;
                        if (entry?["content"] is JSONValue.Array content)
                            foreach (var part in content.Value)
                            {
                                if (part["type"]?.JSONValueString != "text") throw new InvalidOperationException("The answer contains an action.");
                                if (appendContent) answer.Append(part["text"]?.JSONValueString);
                            }
                        if (answer.Length > 32_000) throw new InvalidOperationException("The answer was too long.");
                        break;
                    case "model_request_end":
                        var input = entry?["usage"]?["inputTokens"]?.JSONValueInt ?? 0;
                        var output = entry?["usage"]?["outputTokens"]?.JSONValueInt ?? 0;
                        if (input < 0 || output < 0 || input > long.MaxValue - output) throw new InvalidOperationException("Invalid usage receipt.");
                        if (input + output > 0) spend?.Invoke(new TokenSpend(provider: ProviderKind.Commandcode, totalTokens: input + output,
                            inputTokens: input, outputTokens: output, model: entry?["model"]?.JSONValueString ?? configuration.Model));
                        break;
                    case "run_start": case "message_start": case "model_request_start":
                    case "thinking_start": case "thinking_delta": case "thinking_end":
                        break;
                    default:
                        throw new InvalidOperationException("Command Code requested an action or returned an unsupported event.");
                }
            }
            if (!succeeded || await process.Exit().WaitAsync(timeout.Token) != 0 || string.IsNullOrWhiteSpace(answer.ToString()) || !permitted())
                throw new InvalidOperationException("Command Code did not return an authorized text answer.");
            cancel.ThrowIfCancellationRequested();
            timeout.Token.ThrowIfCancellationRequested();
            return answer.ToString();
        }
        finally
        {
            try { if (process is not null) await process.Stop(); }
            finally
            {
                // Delete the opened object by handle, never a policy name in a child-replaced directory.
                await Task.Run(() =>
                {
                    try { ownedPolicy?.Dispose(); }
                    finally { ownedFiles?.Dispose(); }
                });
            }
        }
    }

    internal static IReadOnlyDictionary<string, string> PrivateEnvironment(IReadOnlyDictionary<string, string> source, string folder, string key, string nonce)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PATH", "PATHEXT", "SystemRoot", "WINDIR", "ComSpec" })
            if (source.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)) is { Value: { } value }) environment[name] = value;
        foreach (var name in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "XDG_CONFIG_HOME", "TEMP", "TMP" }) environment[name] = folder;
        environment["COMMAND_CODE_API_KEY"] = key;
        environment[NonceKey] = nonce;
        return environment;
    }

    private sealed class TextProcess(string executable, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string> environment) : ICommandCodeTextProcess
    {
        private readonly StdioProcess child = new(executable, arguments, folder, environment);
        private bool started;
        public IAsyncEnumerable<JSONValue> Messages => child.Messages;
        public void Start() { child.Start(); started = true; }
        public void Write(string text) => child.Write(Encoding.UTF8.GetBytes(text));
        public void CloseInput() => child.CloseInput();
        public Task<int> Exit() => child.WaitForExit();
        public async Task Stop()
        {
            if (!started) return;
            child.ProcessControl.KillTree();
            await child.WaitForExit().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
