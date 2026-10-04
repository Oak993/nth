namespace Marrow.Services.Port.Skills.Pipelines;
// Internal control signal for SK-15's terms step; never a new user-facing message.
public sealed class PipelineTermsRequiredException(string id) : InvalidOperationException(id);
