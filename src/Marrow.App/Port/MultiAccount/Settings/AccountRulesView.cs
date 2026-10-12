// PORT-ONLY: T11 step 2 (OD-0114..OD-0116); OD-0149 (ED-NEW-providersmerge-1). The account-wide settings at the foot of Settings > Providers, as the owner-approved page draws them, in grouped inset sections of the port's own
// cards: "When an account runs out" with its indented rows, and "Handshake". They were the lower half of the accounts part of Settings > Models; the accounts themselves are in each service's card now (ServiceBlockControl).
// Everything stays in plain sight (no Customize button): the two groups appear, folding open, once a service has two accounts or a pill exists; with one account per service the page shows only its providers.
// The view only draws AccountsSettingsModel and sends it the user's choices.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class AccountRulesView : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly ModelPulse pulse;
    private readonly CzFoldRow runFold;
    private readonly CzFoldRow handshakeFold;
    private readonly RowGroup runGroup;
    private readonly RowGroup handshakeGroup;

    internal AccountRulesView(AccountsSettingsModel model)
    {
        this.model = model;
        // listens while on screen only (the page is built again each time it opens; the model lives as long as the app)
        pulse = new ModelPulse(changed =>
        {
            AttachedToVisualTree += (_, _) => { model.Changed += changed; pulse!.Bump(); }; // assigned by then: attaching comes after construction
            DetachedFromVisualTree += (_, _) => model.Changed -= changed;
        });

        runGroup = new RowGroup(model.RunRows, model.Row, pulse, model.Choose, model.SetSwitch, key => { if (key == "handed") ShowExample(); });
        runFold = new CzFoldRow(Section(MultiAccountWords.RunHeading, runGroup, MultiAccountWords.RunFoot, ChromeTokens.SectionSpacing), model.ShowRunOut);

        handshakeGroup = new RowGroup(model.HandshakeRows, model.Row, pulse, model.Choose, model.SetSwitch, _ => { });
        handshakeFold = new CzFoldRow(Section(MultiAccountWords.HandshakeHeading, handshakeGroup, MultiAccountWords.HandshakeFoot, 0), model.ShowRunOut);

        Child = new StackPanel { Spacing = 0, Children = { runFold, handshakeFold } };
        this.Track(() => pulse.Revision, _ => Apply());
        Apply();
    }

    /// <summary>A grouped section: the heading, the card, the page's one grey line under it, and the page's space between sections under it when another section follows (the page's own stack puts that space above the group).</summary>
    private static Control Section(string heading, Control card, string foot, double below)
    {
        var note = LibraryUi.Text(foot, 12, secondary: true, wrap: true);
        note.Margin = new Thickness(16, 0, 16, 0);
        return new Border
        {
            Padding = new Thickness(0, 0, 0, below),
            Child = new StackPanel { Spacing = 8, Children = { new ChromeSection(heading, () => card), note } },
        };
    }

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
        var show = model.ShowRunOut;
        runFold.Open = show;
        handshakeFold.Open = show;
        runGroup.Apply(model.RunRows);
        handshakeGroup.Apply(model.HandshakeRows);
    }
}
