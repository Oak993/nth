using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class AppInfoBuildChannelTests
{
    [Fact] // PR-STORE-008: exercised in both Debug and Release test runs.
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
