// PORT-ONLY: SK-11e / DL-6 PATH rule. The child gets addons\bin from the launch or bound data root on PATH; an unbound test fails closed with the same map.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderSkillEnvironment
{
    public static ValueMap<string, string> Apply(ValueMap<string, string> environment, ProviderSkillLaunch? launch)
    {
        if (launch is null)
        {
            var bound = ProviderChildEnvironment.Apply(environment);
            return ReferenceEquals(bound, environment) ? environment
                : new ValueMap<string, string>(bound, StringComparer.OrdinalIgnoreCase);
        }
        var child = ProviderCustomCommands.ForChild(environment, launch.DataRoot);
        return new ValueMap<string, string>(child, StringComparer.OrdinalIgnoreCase);
    }
}
