using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class AppInfoBuildChannelTests
{
    [Fact] // Exercised in both Debug and Release test runs.
    [Trait("Rule", "R-STR-9")] // No Req: pins .droppy-code, which PR-STORE-008 changes to .marrow (OD-0012 F1); rework.
    public void Cached_application_info_uses_the_compiled_build_channel()
    {
#if DEBUG
        Assert.True(AppInfo.Current.IsDevelopment);
        Assert.Equal(".droppy-code-dev", AppInfo.Current.StateDirectoryName);
#else
        Assert.False(AppInfo.Current.IsDevelopment);
        Assert.Equal(".droppy-code", AppInfo.Current.StateDirectoryName);
#endif
    }
}
