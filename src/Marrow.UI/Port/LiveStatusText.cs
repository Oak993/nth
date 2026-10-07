using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Marrow.UI.Port;

/// <summary>A visible label whose polite announcement also describes its custom-drawn state/progress.</summary>
public sealed class LiveStatusText : TextBlock
{
    public LiveStatusText() => AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusPeer(this);

    private sealed class StatusPeer : TextBlockAutomationPeer
    {
        public StatusPeer(LiveStatusText owner) : base(owner)
        {
            owner.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationProperties.NameProperty)
                    RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, e.OldValue, e.NewValue);
            };
        }

        protected override string? GetNameCore() => AutomationProperties.GetName(Owner) ?? base.GetNameCore();
    }
}
