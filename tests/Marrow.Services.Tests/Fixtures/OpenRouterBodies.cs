using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture (ED-234): the answers a stand-in https://openrouter.ai gives, and a session configuration
// for the native OpenRouter agent. Nothing here is a real key, a real model price or a real host answer: every value
// is made up, shaped like the models endpoint's documented fields (id, name, context_length, pricing as strings of
// USD per token, architecture.input_modalities, supported_parameters).
public static class OpenRouterBodies
{
    /// <summary>A made-up key. Its bearer form is what every authenticated request must carry.</summary>
    public const string Key = "sk-or-test-0000";

    public const string Bearer = "Bearer " + Key;

    /// <summary>Four usable rows and one without an id, in an order the sort has to undo.</summary>
    public const string ModelsJson = """
        {"data":[
          {"id":"meta-llama/llama-3.3-70b-instruct:free","name":"Meta: Llama 3.3 70B Instruct (free)","context_length":65536,
           "pricing":{"prompt":"0","completion":"0"},"architecture":{"input_modalities":["text"]},"supported_parameters":["tools","max_tokens"]},
          {"name":"A row with no id"},
          {"id":"anthropic/claude-sonnet-4.5","name":"Anthropic: Claude Sonnet 4.5","context_length":1000000,
           "pricing":{"prompt":"0.000003","completion":"0.000015"},"architecture":{"input_modalities":["text","image"]},
           "supported_parameters":["tools","reasoning","max_tokens"]},
          {"id":"openrouter/auto","name":"Auto Router","context_length":2000000,
           "pricing":{"prompt":"-1","completion":"-1"}},
          {"id":"deepseek/deepseek-r1","name":"DeepSeek: R1","context_length":163840,
           "pricing":{"prompt":"0.0000005","completion":"0.00000218"},"supported_parameters":["include_reasoning"]}
        ]}
        """;

    /// <summary>The body of a streamed chat answer: each data line, then the end marker.</summary>
    public static string Sse(params string[] dataLines) =>
        string.Join("", dataLines.Select(line => $"data: {line}\n\n")) + "data: [DONE]\n\n";

    /// <summary>The configuration of a session over a working directory, with the made-up key unless another is given.</summary>
    public static SessionConfiguration Configuration(string root, string? apiKey = Key, RuntimeMode runtimeMode = RuntimeMode.FullAccess) => new()
    {
        Provider = ProviderKind.OpenRouter,
        WorkingDirectory = SwiftURL.Create("file:" + root.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = InteractionMode.Build,
        ApiKey = apiKey,
        Transcript = ValueList<TranscriptMessage>.Empty,
    };

    /// <summary>One user turn with no attachments.</summary>
    public static TurnInput Turn(string text, string? model = null, string? effort = null) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        Model = model,
        Effort = effort,
    };
}
