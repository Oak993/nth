using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-4 (ED-NEW-dextra-as4-2): the smallest arguments each nth-dextra tool takes.</summary>
public static class AssistantToolCatalogFixture
{
    /// <summary>Each required argument of the named tool with a fitting value.</summary>
    public static JSONValue Minimal(string name) => Minimal(AssistantToolCatalog.Find(name) ?? throw new ArgumentException("no such tool: " + name));

    public static JSONValue Minimal(AssistantToolDefinition tool) => JSONValue.FromDictionaryLiteral([.. tool.Parameters.Where(p => p.Required).Select(p =>
        (p.Name, p.Type switch
        {
            AssistantParameterType.String => (JSONValue)(p.Choices.Count > 0 ? p.Choices[0] : "x"),
            AssistantParameterType.StringList => JSONValue.FromArrayLiteral("n-1"),
            AssistantParameterType.Boolean => (JSONValue)true,
            _ => (JSONValue)(long)(p.Minimum ?? 1),
        }))]);
}
