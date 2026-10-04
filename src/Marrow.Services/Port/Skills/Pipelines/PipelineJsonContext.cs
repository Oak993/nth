using System.Text.Json.Serialization;
namespace Marrow.Services.Port.Skills.Pipelines;
[JsonSerializable(typeof(PipelineRoute))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext;
