// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2). A Hydra head that is stuck asks its lead's model
// (any provider) through the ask_advisor tool nth hosts. The tool's name, description and input schema, one place for the
// MCP server and the API providers' own tool loop.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Advisor;

public static class AdvisorTool
{
    public const string Name = "ask_advisor";

    /// <summary>The timeline row's title.</summary>
    public const string Title = "Advisor";

    public const string Description =
        "Ask your Hydra lead's model for advice. Call it when you are stuck (the same error twice, a fix that keeps failing), "
        + "unsure between approaches, or before a risky change (deleting data, a wide refactor, a migration), rather than guessing. "
        + "Do not call it for routine steps you can do yourself. Put what you tried and the relevant code or errors in context.";

    /// <summary>The input schema: question (required) and context (optional).</summary>
    public static JSONValue InputSchema() => JSONValue.FromDictionaryLiteral(
        ("type", "object"),
        ("properties", JSONValue.FromDictionaryLiteral(
            ("question", JSONValue.FromDictionaryLiteral(("type", "string"), ("description", "What you need advice on, in one or two sentences."))),
            ("context", JSONValue.FromDictionaryLiteral(("type", "string"), ("description", "What you have tried so far and the relevant code, errors or output."))))),
        ("required", JSONValue.FromArrayLiteral("question")),
        ("additionalProperties", false));

    /// <summary>The OpenAI-style function definition the API providers' tool loop sends.</summary>
    public static JSONValue FunctionDefinition() => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", Name), ("description", Description), ("parameters", InputSchema()))));

    /// <summary>
    /// The advisor for the session's chat, or null when the chat has none (the tool is then not offered). A chat that is
    /// not known (no thread) never has one. A Choice seam that throws counts as none.
    /// </summary>
    public static AdvisorChoice? For(SessionConfiguration configuration) =>
        configuration.IgnisThreadID is { } thread ? For(thread, configuration.Provider, configuration.Model) : null;

    /// <summary>The session's registration (its chat, worker and advisor), or null when its chat has no advisor.</summary>
    public static AdvisorRegistration? Registration(SessionConfiguration configuration) =>
        For(configuration) is { } choice ? new AdvisorRegistration(configuration.IgnisThreadID, configuration.Provider, configuration.Model, choice) : null;

    public static AdvisorChoice? For(Guid thread, ProviderKind provider, string? model)
    {
        try { return AdvisorSeams.Choice(thread, provider, model ?? ""); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return null; }
    }
}
