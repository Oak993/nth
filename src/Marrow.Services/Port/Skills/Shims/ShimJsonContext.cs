using System.Text.Json.Serialization;
namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: source-generated JSON keeps the standalone shim NativeAOT-compatible.
[JsonSerializable(typeof(ShimDefinition))]
internal sealed partial class ShimJsonContext : JsonSerializerContext;
