using System.Reflection;
using System.Text.Json.Nodes;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Fixtures;

public sealed class SkillsMcpReflection : IDisposable
{
    private readonly object server;
    private readonly MethodInfo handle;
    public SkillsMcpReflection(Generation generation)
    {
        var type = typeof(Generation).Assembly.GetType("Marrow.Services.Port.Skills.Providers.SkillsMcpServer");
        Assert.NotNull(type);
        server = Activator.CreateInstance(type, generation)!;
        handle = type.GetMethod("Handle")!;
    }
    public JsonNode Call(string method, object? parameters = null)
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        return JsonNode.Parse((string)handle.Invoke(server, [line])!)!;
    }
    public JsonNode Tool(string name, object? arguments = null) => Call("tools/call", new { name, arguments = arguments ?? new { } });
    public void Dispose() => ((IDisposable)server).Dispose();
}
