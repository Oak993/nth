using Marrow.App.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, unit U0 (ED-NEW-inst-u0-1). Velopack's Start shortcut and nth's
// desktop shortcut must carry the AppUserModelID nth gives its own process, or the taskbar shows two nth buttons and pins
// and toasts disagree. The setup cannot reference the app, so it re-stamps Start with ShellLinkKeys.NthAppUserModelId;
// this pins that constant to the release identity (ED-220 as renamed by OD-0135; PLAN.md still says "MarrowCode.Marrow").
public sealed class ShellLinkAppUserModelIdTests
{
    [Fact]
    [Trait("Decision", "ED-220")]
    [Trait("Decision", "OD-0135")]
    public void The_shortcut_AppUserModelID_is_the_release_process_identity()
    {
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, ToastBuildIdentity.ForBuild(debug: false, dev: false).AppId);
        Assert.NotEqual(ShellLinkKeys.NthAppUserModelId, ToastBuildIdentity.ForBuild(debug: true, dev: false).AppId);
    }
}
