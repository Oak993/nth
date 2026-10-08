using Marrow.App.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Port;

[Trait("Decision", "ED-220")]
public sealed class PerformanceProfileRegistrationTests
{
    [Fact]
    public void A_copied_profile_never_registers_with_the_OS()
    {
        using var profile = DevDataRoot.PerformanceRoot.Override("E:\\isolated-performance-profile");
        var registrations = 0;
        WindowsToastStartup.Bind(() => registrations++);
        Assert.Equal(0, registrations);
    }

    [Fact]
    public void An_ordinary_launch_still_registers_notifications()
    {
        using var profile = DevDataRoot.PerformanceRoot.Override(string.Empty);
        var registrations = 0;
        WindowsToastStartup.Bind(() => registrations++);
        Assert.Equal(1, registrations);
    }

    [Fact]
    public async Task Copied_profiles_skip_external_startup_maintenance()
    {
        using var profile = DevDataRoot.PerformanceRoot.Override("E:\\isolated-performance-profile");
        var operations = 0;
        PerformanceProfileMaintenance.Run(() => { operations++; });
        await PerformanceProfileMaintenance.Run(() => { operations++; return Task.CompletedTask; });
        Assert.Equal(0, operations);
    }

    [Fact]
    public async Task Ordinary_launches_keep_external_startup_maintenance()
    {
        using var profile = DevDataRoot.PerformanceRoot.Override(string.Empty);
        var operations = 0;
        PerformanceProfileMaintenance.Run(() => { operations++; });
        await PerformanceProfileMaintenance.Run(() => { operations++; return Task.CompletedTask; });
        Assert.Equal(2, operations);
    }
}
