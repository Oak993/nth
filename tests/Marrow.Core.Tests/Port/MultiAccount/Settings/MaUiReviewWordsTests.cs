using Marrow.Testing.MultiAccountSettings;
using Xunit;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

public sealed class MaUiReviewWordsTests
{
    [Fact, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    public async Task MAUI_054_Local_discovery_success_keeps_the_locked_server_name()
    {
        // HTML 3774; checklist 1 Local row / 10 Add: the reference discovery is one
        // Ollama model on this PC, rather than an unspecified kind of server.
        var rig = new AccountsRig();
        rig.Actions.Local = new(Marrow.Core.Port.MultiAccount.Settings.LocalConnectOutcome.Found, 1, "this PC",
            Marrow.Core.Port.Local.LocalServerIdentity.FromRootReply("Ollama is running"));
        Assert.Equal("Found Ollama on this PC · 1 model added", await rig.Model.ConnectLocalAsync("http://localhost:11434"));
    }
}
