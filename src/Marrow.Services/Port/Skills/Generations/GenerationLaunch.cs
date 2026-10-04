// PORT-ONLY: SK-9 structured launch diagnostic; contains no skill text, user files or credentials.
namespace Marrow.Services.Port.Skills.Generations;

public sealed record GenerationLaunch(string Provider, string ChatId, string Key, GenerationLaunchReason Reason);
