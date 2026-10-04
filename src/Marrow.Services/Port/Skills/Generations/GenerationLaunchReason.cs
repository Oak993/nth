// PORT-ONLY: SK-9 PLAN 2.1 requires a spare miss and its reason to be logged; internal diagnostics, no UI labels.
namespace Marrow.Services.Port.Skills.Generations;

public enum GenerationLaunchReason { ExistingChat, ReadySpare, MissingSpare, DamagedSpare }
