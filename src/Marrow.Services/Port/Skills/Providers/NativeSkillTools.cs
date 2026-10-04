// PORT-ONLY: SK-11f / WU-12, DL-14. Not implemented yet.
#pragma warning disable CS9113 // stub, replaced by the implementation
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class NativeSkillTools(Generation? generation, NativeUserSkills? ownSkills = null, IToolRequestOffer? offer = null)
{
    public const string UseSkillName = "use_skill";
    public const string RequestToolName = "request_tool";
    public string? CatalogueBlock() => throw new NotImplementedException();
    public IReadOnlyList<JSONValue> Definitions => throw new NotImplementedException();
    public bool Owns(string toolName) => throw new NotImplementedException();
    public Task<NativeToolResult> Execute(string toolName, JSONValue arguments) => throw new NotImplementedException();
}
