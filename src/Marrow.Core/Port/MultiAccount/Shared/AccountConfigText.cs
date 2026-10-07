// PORT-ONLY: multi-account steps 3b-5, job 0. The config line of an account as multi-account.html writes it (cfgLine): "<model> · <effort>", and Fast as the yellow bolt only, never the word. The bolt shows when the account's
// model has a fast tier (the composer's model chip draws it the same way, EffortSlider.swift:77-80), or, for a model the service no longer lists, when the flag is set.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;

namespace Marrow.Core.Port.MultiAccount.Shared;

public sealed record AccountConfigText(string Text, bool Bolt)
{
    public static AccountConfigText Of(string model, string? effort, bool fast)
    {
        var line = new StringBuilder(model);
        if (effort is { Length: > 0 })
        {
            if (line.Length > 0) line.Append(" · ");
            line.Append(ModelOption.EffortTitle(effort));
        }
        return new AccountConfigText(line.ToString(), fast);
    }

    /// <summary>The account's own defaults, named from the models its service offers. Defaults that say nothing take the service's default model and that model's default effort.</summary>
    public static AccountConfigText Of(AccountDefaults defaults, IReadOnlyList<ModelOption> models)
    {
        var option = defaults.Model is { } id
            ? models.FirstOrDefault(m => m.Id == id)
            : models.FirstOrDefault(m => m.IsDefault) ?? models.FirstOrDefault();
        var name = option?.Name ?? defaults.Model ?? "";
        var effort = option is { Efforts.Count: 0 } ? null : defaults.Effort ?? option?.DefaultEffort;
        var fast = defaults.FastMode && (option?.SupportsFast ?? defaults.Model is not null);
        return Of(name, effort, fast);
    }
}
