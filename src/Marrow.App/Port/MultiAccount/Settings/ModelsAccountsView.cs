// PORT-ONLY: T11 step 2 (OD-0114..OD-0116). The multi-account part of Settings > Models, as the owner-approved page draws it, as grouped inset sections of the port's own cards: "Accounts" (a block per connected service), "When an account runs
// out" with its indented rows, and "Handshake". Everything stays in plain sight (no Customize button): the two groups appear, folding open, once a service has two accounts or a pill exists; with one account per service Models looks as today.
// The view only draws AccountsSettingsModel and sends it the user's choices.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class ModelsAccountsView : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly ModelPulse pulse;
    private readonly StackPanel blocks = new() { Spacing = 0 };
    private readonly Dictionary<ProviderKind, ServiceBlockControl> blockViews = [];
    private readonly CzFoldRow accountsFold;
    private readonly CzFoldRow runFold;
    private readonly CzFoldRow handshakeFold;
    private readonly RowGroup runGroup;
    private readonly RowGroup handshakeGroup;
    private readonly Action<ProviderKind> usageDetails;
    private readonly Action<string>? handed;

    internal ModelsAccountsView(AccountsSettingsModel model, Action<ProviderKind> usageDetails, Action<string>? handed = null)
    {
        this.model = model;
        this.usageDetails = usageDetails;
        this.handed = handed;
        pulse = new ModelPulse(h => model.Changed += h);

        accountsFold = new CzFoldRow(Section(MultiAccountWords.AccountsHeading, new ChromeCard(() => blocks), MultiAccountWords.AccountsFoot), model.Blocks.Count > 0);

        runGroup = new RowGroup(model.RunRows, model.Row, pulse, model.Choose, model.SetSwitch, key => handed?.Invoke(key));
        runFold = new CzFoldRow(Section(MultiAccountWords.RunHeading, runGroup, MultiAccountWords.RunFoot), model.ShowRunOut);

        handshakeGroup = new RowGroup(model.HandshakeRows, model.Row, pulse, model.Choose, model.SetSwitch, _ => { });
        handshakeFold = new CzFoldRow(Section(MultiAccountWords.HandshakeHeading, handshakeGroup, MultiAccountWords.HandshakeFoot), model.ShowRunOut);

        Child = new StackPanel { Spacing = 0, Children = { accountsFold, runFold, handshakeFold } };
        this.Track(() => pulse.Revision, _ => Apply());
        Apply();
    }

    /// <summary>A grouped section: the heading, the card, the page's one grey line under it.</summary>
    private static Control Section(string heading, Control card, string foot)
    {
        var note = LibraryUi.Text(foot, 12, secondary: true, wrap: true);
        note.Margin = new Thickness(16, 0, 16, 0);
        return new Border
        {
            Padding = new Thickness(0, 0, 0, ChromeTokens.SectionSpacing),
            Child = new StackPanel { Spacing = 8, Children = { new ChromeSection(heading, () => card), note } },
        };
    }

    internal IReadOnlyDictionary<ProviderKind, ServiceBlockControl> Blocks => blockViews;

    internal bool AccountsOpen => accountsFold.Open;

    internal bool RunOpen => runFold.Open;

    internal bool HandshakeOpen => handshakeFold.Open;

    internal RowGroup RunRows => runGroup;

    internal RowGroup HandshakeRows => handshakeGroup;

    internal void Apply()
    {
        var next = model.Blocks;
        accountsFold.Open = next.Count > 0;
        foreach (var gone in blockViews.Keys.Where(k => next.All(b => b.Service != k)).ToArray())
        {
            blocks.Children.Remove(blockViews[gone]);
            blockViews.Remove(gone);
        }
        foreach (var block in next)
        {
            if (!blockViews.TryGetValue(block.Service, out var control))
            {
                blockViews[block.Service] = control = new ServiceBlockControl(model, block, pulse, usageDetails);
                blocks.Children.Add(control);
            }
            else control.Apply(block);
        }
        var show = model.ShowRunOut;
        runFold.Open = show && next.Count > 0;
        handshakeFold.Open = show && next.Count > 0;
        runGroup.Apply(model.RunRows);
        handshakeGroup.Apply(model.HandshakeRows);
    }
}
