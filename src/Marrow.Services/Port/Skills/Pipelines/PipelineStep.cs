// PORT-ONLY: SK-8 / DL-8, ONDEMAND 3.7. BeforeCommit is the last fallible checkpoint.
namespace Marrow.Services.Port.Skills.Pipelines;
public enum PipelineStep { Downloaded, Installed, RoutesPrepared, BeforeCommit }
