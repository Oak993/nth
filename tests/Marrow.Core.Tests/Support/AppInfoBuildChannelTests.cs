using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class AppInfoBuildChannelTests
{
    [Fact] // Exercised in both Debug and Release test runs.
    [Trait("Req", "PR-STORE-008")] // Core/Support/Text.swift:5-6: the development build uses the -dev name (OD-0012 F1, OD-0135: ".nth-dev")
    public void Cached_application_info_uses_the_compiled_build_channel()
    {
#if DEBUG
        Assert.True(AppInfo.Current.IsDevelopment);
        Assert.Equal(".nth-dev", AppInfo.Current.StateDirectoryName);
#else
        Assert.False(AppInfo.Current.IsDevelopment);
        Assert.Equal(".nth", AppInfo.Current.StateDirectoryName);
#endif
    }
}
