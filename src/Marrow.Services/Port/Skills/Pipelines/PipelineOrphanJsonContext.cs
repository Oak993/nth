using System.Text.Json.Serialization;
namespace Marrow.Services.Port.Skills.Pipelines;
// Its own context: PipelineJsonContext is also compiled into the NativeAOT shim, which has no use for orphans.
[JsonSerializable(typeof(PipelineOrphan[]))]
internal sealed partial class PipelineOrphanJsonContext : JsonSerializerContext;
