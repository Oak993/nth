using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

public static class LiveApiKeyLaunch
{
    public static OpenAICompatibleSession Ensure(ThreadRuntimeTurnRig rig)
    {
        Task<ProviderSession> task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task<ProviderSession>>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        rig.H.Main.Loop.Run(() => task, AppModelHarness.Bound);
        return Assert.IsAssignableFrom<OpenAICompatibleSession>(task.GetAwaiter().GetResult());
    }

    public static AccountKeyStore Keys(ThreadRuntimeTurnRig rig)
    {
        var property = rig.App.Settings.GetType().GetProperty("AccountKeys", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<AccountKeyStore>(property.GetValue(rig.App.Settings));
    }
}
