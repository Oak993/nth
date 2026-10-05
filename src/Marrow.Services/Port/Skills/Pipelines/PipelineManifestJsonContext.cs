using System.Text.Json.Serialization;
namespace Marrow.Services.Port.Skills.Pipelines;
// Its own context: PipelineJsonContext is also compiled into the NativeAOT shim, which has no use for manifests.
[JsonSerializable(typeof(PipelineManifest))]
internal sealed partial class PipelineManifestJsonContext : JsonSerializerContext;
