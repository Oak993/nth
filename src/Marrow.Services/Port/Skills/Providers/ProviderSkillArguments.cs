// PORT-ONLY: SK-11e / WU-10, skills-PLAN.md 2.2. Pure argv rewriting: each method returns a new list and never touches its input.
namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderSkillArguments
{
    /// <summary>Pi live sessions: --skill "&lt;views\pi\gen&gt;" appended. The model-list probe never goes through here.</summary>
    public static List<string> Pi(IReadOnlyList<string> arguments, ProviderSkillLaunch launch) =>
        [.. arguments, "--skill", RequireView(launch.GenerationFolder, "pi")];

    /// <summary>Command Code: --skill "&lt;views\commandcode\gen&gt;" appended, every turn the caller's probe gate allows.</summary>
    public static List<string> CommandCode(IReadOnlyList<string> arguments, ProviderSkillLaunch launch) =>
        [.. arguments, "--skill", RequireView(launch.GenerationFolder, "commandcode")];

    /// <summary>Grok: --plugin-dir "&lt;views\grok\gen&gt;" (then each plugin folder) before the "agent" subcommand.</summary>
    public static List<string> Grok(IReadOnlyList<string> arguments, ProviderSkillLaunch launch)
    {
        var folder = RequireView(launch.GenerationFolder, "grok");
        var at = arguments.ToList().IndexOf("agent");
        if (at < 0) throw new ArgumentException("The Grok argv has no agent subcommand.", nameof(arguments));
        var plugin = new List<string> { "--plugin-dir", folder };
        foreach (var extra in launch.PluginFolders ?? []) { plugin.Add("--plugin-dir"); plugin.Add(RequireView(extra, "grok")); }
        return [.. arguments.Take(at), .. plugin, .. arguments.Skip(at)];
    }

    // The folder must sit under views\<provider>\ (so a Grok launch can never carry views\claude\); anything else fails closed.
    internal static string RequireView(string folder, string provider)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) throw new ArgumentException("An absolute generation folder is required.");
        var parts = folder.Split('\\', '/');
        for (var i = 0; i + 1 < parts.Length; i++)
            if (parts[i].Equals("views", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Equals(provider, StringComparison.OrdinalIgnoreCase) && i + 2 < parts.Length)
                return folder;
        throw new ArgumentException("The generation folder is not under views\\" + provider + ".");
    }
}
