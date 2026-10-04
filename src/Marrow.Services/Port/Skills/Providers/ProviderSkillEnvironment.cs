// PORT-ONLY: SK-11e / DL-6 PATH rule. With a launch the child gets addons\bin on PATH and nothing else changes; without one the very same map is returned.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderSkillEnvironment
{
    public static ValueMap<string, string> Apply(ValueMap<string, string> environment, ProviderSkillLaunch? launch)
    {
        if (launch is null) return environment;
        var child = ShimPath.ForChild(environment, launch.DataRoot);
        return new ValueMap<string, string>(child, StringComparer.OrdinalIgnoreCase);
    }
}
