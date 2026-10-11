// PORT-ONLY: OD-0139 (ED-NEW-providersmerge-1). The accounts of Settings > Providers without the rest of the page: the accounts part of each service's card (ServiceBlockControl) one under the other, then the account-wide
// group at the page's foot (AccountRulesView), over an AccountsRig. The parts are the page's own controls; only the provider cards around them are left out (ProvidersSwitchAndAddTests draws them whole).
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.App.Tests.Fixtures;

internal sealed class ProvidersAccountsPreview : Decorator
{
    private readonly Dictionary<ProviderKind, ServiceBlockControl> blocks = [];

    internal ProvidersAccountsPreview(AccountsSettingsModel model, params ProviderKind[] services)
    {
        var stack = new StackPanel();
        foreach (var service in services.Length > 0 ? services : [ProviderKind.Claude])
            stack.Children.Add(blocks[service] = new ServiceBlockControl(model, service));
        Rules = new AccountRulesView(model);
        stack.Children.Add(Rules);
        Child = stack;
    }

    internal IReadOnlyDictionary<ProviderKind, ServiceBlockControl> Blocks => blocks;

    internal ServiceBlockControl Block(ProviderKind service) => blocks[service];

    internal AccountRulesView Rules { get; }

    internal bool RunOpen => Rules.RunOpen;

    internal bool HandshakeOpen => Rules.HandshakeOpen;

    internal RowGroup RunRows => Rules.RunRows;

    internal RowGroup HandshakeRows => Rules.HandshakeRows;
}
