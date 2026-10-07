// PORT-ONLY: T11 step 2 (OD-0114..OD-0116). The multi-account part of Settings > Models, as the owner-approved page draws it, as grouped inset sections of the port's own cards: "Accounts" (a block per connected service), "When an account runs
// out" with its indented rows, and "Handshake". Everything stays in plain sight (no Customize button): the two groups appear, folding open, once a service has two accounts or a pill exists; with one account per service Models looks as today.
// The view only draws AccountsSettingsModel and sends it the user's choices.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
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
    private readonly Action<ProviderKind> openKeys;
    private readonly ServiceBlockControl localBlock;

    internal ModelsAccountsView(AccountsSettingsModel model, Action<ProviderKind> usageDetails, Action<ProviderKind> openKeys)
    {
        this.model = model;
        this.usageDetails = usageDetails;
        this.openKeys = openKeys;
        pulse = new ModelPulse(h => model.Changed += h);

        // Local is the last block and is always there (its box adds the first server); the others are the connected services
        localBlock = new ServiceBlockControl(model, model.LocalBlock, pulse, usageDetails, openKeys);
        blocks.Children.Add(localBlock);
        accountsFold = new CzFoldRow(Section(MultiAccountWords.AccountsHeading, new ChromeCard(() => blocks), MultiAccountWords.AccountsFoot), true);

        runGroup = new RowGroup(model.RunRows, model.Row, pulse, model.Choose, model.SetSwitch, key => { if (key == "handed") ShowExample(); });
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

    internal ServiceBlockControl LocalBlockView => localBlock;

    internal LocalServerBox LocalBox => localBlock.ServerBox!;

    internal bool AccountsOpen => accountsFold.Open;

    internal bool RunOpen => runFold.Open;

    internal bool HandshakeOpen => handshakeFold.Open;

    internal RowGroup RunRows => runGroup;

    internal RowGroup HandshakeRows => handshakeGroup;

    // "Show an example": the sheet in the window, like the other sheets of the pages (Escape and the cross close it).
    private void ShowExample()
    {
        if (TopLevel.GetTopLevel(this) is null) return;
        ChatSheetPresentation.Show(this, dismiss => new HandedSheetLayer(new HandedSheetView(model.Example(), dismiss)), inWindow: true, still: LibraryUi.Still(this));
    }

    internal void Apply()
    {
        var next = model.Blocks;
        accountsFold.Open = true;
        localBlock.Apply(model.LocalBlock);
        foreach (var gone in blockViews.Keys.Where(k => next.All(b => b.Service != k)).ToArray())
        {
            blocks.Children.Remove(blockViews[gone]);
            blockViews.Remove(gone);
        }
        foreach (var block in next)
        {
            if (!blockViews.TryGetValue(block.Service, out var control))
            {
                blockViews[block.Service] = control = new ServiceBlockControl(model, block, pulse, usageDetails, openKeys);
                blocks.Children.Insert(blocks.Children.Count - 1, control); // before the Local block
            }
            else control.Apply(block);
        }
        var show = model.ShowRunOut;
        runFold.Open = show;
        handshakeFold.Open = show;
        runGroup.Apply(model.RunRows);
        handshakeGroup.Apply(model.HandshakeRows);
    }
}
