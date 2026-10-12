using System.Text.Json.Nodes;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): a small helper for tests that plant a field a newer build would have written.</summary>
public static class JsonObjectExtensions
{
    public static JsonObject WithField(this JsonObject node, string name, string value)
    {
        node[name] = value;
        return node;
    }
}
