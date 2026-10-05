// PORT-ONLY: SK-11e / WU-10. What one Pi, Command Code or Grok launch needs to wire a chat's locked generation.
namespace Marrow.Services.Port.Skills.Providers;

/// <summary>A null launch means "no Marrow skills": argv and environment stay exactly today's (the Mac's).</summary>
/// <param name="GenerationFolder">The chat's locked generation folder, absolute, under views\pi, views\commandcode or views\grok.</param>
/// <param name="DataRoot">The Marrow Code data root whose addons\bin goes on PATH (DL-6).</param>
/// <param name="PluginFolders">Grok only: the generation's further plugin folders, each passed as its own --plugin-dir (Generation.PluginFolders).</param>
public sealed record ProviderSkillLaunch(string GenerationFolder, string DataRoot, IReadOnlyList<string>? PluginFolders = null);
