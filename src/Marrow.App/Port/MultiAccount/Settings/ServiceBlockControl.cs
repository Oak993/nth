// PORT-ONLY: T11 step 2; OD-0139 (ED-NEW-providersmerge-1). The accounts of one service inside its own card on Settings > Providers, the one place for every login (they were a block of their own on Settings > Models). One account
// looks as the card did: nothing but the quiet "Add another account" row at the card's foot. Several fold a list open (OD-0076): a small heading with the service's summary ("2 accounts · the same order as the composer's popover"), the
// rows in the order nth uses them (drag, Alt+Up and Alt+Down, the ⋯ menu; the composer's popover shows the same order), the add row and the page's grey line on the order. A service kept by API keys lists its keys the same way
// under "Keys and order" (each row the masked key with its eye, the credits left, its switch and ⋯ with Remove this key) and adds a key in a box that folds open under "Add another key". Local lists the local models found and
// keeps "Add a local server". The service's own ⋯ menu, its usage jump and "Keys and order" button are gone: the card above already shows the service's usage and status.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class ServiceBlockControl : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly ModelPulse pulse;
    private readonly Func<bool> addAllowed;
    private readonly ProviderKind service;
    private readonly bool keys;
    private readonly bool local;
    private readonly CzCrossfadeText title;
    private readonly CzCrossfadeText summary = new("") { FontSize = 12, Secondary = true };
    private readonly StackPanel list = new() { Spacing = 0 };
    private readonly CzFoldRow blockFold;
    private readonly CzFoldRow headFold;
    private readonly CzFoldRow listFold;
    private readonly CzFoldRow addFold;
    private readonly CzFoldRow footFold;
    private readonly CzCrossfadeText addStatus = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly CzFoldRow addStatusFold;
    private readonly Dictionary<Guid, AccountRowControl> rows = [];
    private readonly Button? addButton;
    private readonly Border addHost = new();
    private readonly Control? addGhost;
    private readonly KeyAddBox? keyBox;
    private readonly LocalServerBox? serverBox;
    private ServiceBlockView? view;

    /// <param name="addAllowed">Whether the card offers its add row now (Settings > Providers: the service is signed in; its "Sign in" row is the way to the first account). Always, when not given.</param>
    internal ServiceBlockControl(AccountsSettingsModel model, ProviderKind service, Func<bool>? addAllowed = null)
    {
        this.model = model;
        this.service = service;
        this.addAllowed = addAllowed ?? (() => true);
        keys = service.IsAPIKeyBased;
        local = service == ProviderKind.Local;
        // the part listens to the model only while it is on screen: a card is built again each time Settings opens its Providers page, the model lives as long as the app
        pulse = new ModelPulse(changed =>
        {
            AttachedToVisualTree += (_, _) => { model.Changed += changed; pulse!.Bump(); }; // assigned by then: attaching comes after construction
            DetachedFromVisualTree += (_, _) => model.Changed -= changed;
        });

        title = new CzCrossfadeText(Heading()) { FontSize = 13, FontWeight = Avalonia.Media.FontWeight.Medium };
        var head = new Border { Padding = new Thickness(16, 10, 16, 2), Child = new StackPanel { Spacing = 1, Children = { title, summary } } };
        headFold = new CzFoldRow(head, false);
        listFold = new CzFoldRow(list, false);

        // a service that can never add (Local, a one-account service) has no add row at all; one that cannot add now keeps a look-alike that folds away, the button itself leaves at once
        // (it is not offered: not a control a person or a screen reader can reach)
        if (keys ? AccountsSettingsModel.CanAddKey(service) : AccountsSettingsModel.CanAdd(service))
        {
            var words = keys ? MultiAccountWords.AddKey : MultiAccountWords.AddAnother;
            addButton = keys ? AddRowButton(words, "service-add-key-" + service.Id, () => keyBox!.Toggle()) : AddAnotherButton(service, () => _ = Add());
            addButton.HorizontalAlignment = HorizontalAlignment.Left;
            addGhost = new Border { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Child = AddRowLook(words) };
            addHost.Child = addGhost;
        }
        var addPart = new StackPanel { Children = { addHost } };
        addStatusFold = new CzFoldRow(new Border { Padding = new Thickness(16, 0, 16, 10), Child = addStatus }, false);
        addPart.Children.Add(addStatusFold);
        if (keys)
        {
            keyBox = new KeyAddBox(model, service);
            addPart.Children.Add(keyBox);
        }
        addFold = new CzFoldRow(addPart, false);

        var foot = LibraryUi.Text(keys ? MultiAccountWords.KeysFoot : MultiAccountWords.AccountsFoot, 12, secondary: true, wrap: true);
        footFold = new CzFoldRow(new Border { Padding = new Thickness(16, 0, 16, 12), Child = foot }, false);

        var body = new StackPanel { Children = { new ChromeRowDivider(), headFold, listFold, addFold, footFold } };
        if (local)
        {
            serverBox = new LocalServerBox(model);
            body.Children.Add(serverBox);
        }
        blockFold = new CzFoldRow(body, false);
        Child = blockFold;
        this.Track(() => (pulse.Revision, this.addAllowed()), _ => Apply(model.BlockFor(service)));
        Apply(model.BlockFor(service), animate: false);
    }

    internal ProviderKind Service => service;

    internal ServiceBlockView? View => view;

    /// <summary>Whether anything of the part shows in the card.</summary>
    internal bool Shown => blockFold.Open;

    internal bool HeadingOpen => headFold.Open;

    internal bool ListOpen => listFold.Open;

    internal double ListFraction => listFold.Fraction;

    internal bool AddOpen => addFold.Open;

    internal bool FootOpen => footFold.Open;

    internal IReadOnlyDictionary<Guid, AccountRowControl> Rows => rows;

    internal string SummaryShown => summary.Text;

    internal string TitleShown => title.Text;

    internal string AddStatusShown => addStatus.Text;

    internal LocalServerBox? ServerBox => serverBox;

    internal KeyAddBox? KeyBox => keyBox;

    /// <summary>Whether this API-key service's keys are listed (two or more, or one that is not the original): the list then stands in place of the card's single "API key" box
    /// (ED-NEW-providersmerge-keys-2).</summary>
    internal bool KeysListed { get; private set; }

    /// <summary>Raised when <see cref="KeysListed"/> changes.</summary>
    internal event Action<bool>? KeysListedChanged;

    private string Heading() => keys ? MultiAccountWords.KeysAndOrder : local ? MultiAccountWords.LocalModelsHeading : MultiAccountWords.AccountsHeading;

    // ED-NEW-providersmerge-2: one sign-in at a time (the model starts nothing while one runs; the row waits, saying so), and its result is always said under the row.
    private async Task Add()
    {
        if (model.Adding(service)) return;
        string? problem;
        try { problem = await model.AddAsync(service); }
        catch (OperationCanceledException) { problem = null; }
        if (problem is not null) { addStatus.SetText(problem); addStatusFold.Open = true; }
    }

    /// <summary>The quiet "Add another account" row: the accent plus and the accent words, a plain button the width of its words.</summary>
    internal static Button AddAnotherButton(ProviderKind service, Action add) => AddRowButton(MultiAccountWords.AddAnother, "service-add-" + service.Id, add);

    /// <summary>A quiet add row of a card (the accent plus and words): "Add another account", "Add another key".</summary>
    internal static Button AddRowButton(string words, string id, Action add)
    {
        var button = PlainControls.Button(AddRowLook(words), add);
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(words, id);
        return button;
    }

    /// <summary>What an add row shows: the accent plus and the accent words.</summary>
    private static StackPanel AddRowLook(string words)
    {
        var plus = LibraryUi.Glyph("plus", 12, ChromeTokens.Accent, SwiftFont.Weight.Medium);
        var text = LibraryUi.Text(words, 13, weight: SwiftFont.Weight.Medium);
        text.Foreground = ChromeTokens.Accent.Brush(text);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 10), Children = { plus, text } };
    }

    internal void Apply(ServiceBlockView? next, bool animate = true)
    {
        view = next;
        var listed = next?.Rows ?? [];
        var canAdd = next is not null && (keys ? next.CanAddKey : next.CanAdd) && addAllowed();
        title.SetText(Heading());
        summary.SetText(next?.Summary ?? "");
        headFold.Open = listed.Count > 0 || local;
        listFold.Open = listed.Count > 0;
        if (addButton is not null) addHost.Child = canAdd ? addButton : addGhost;
        addFold.Open = canAdd;
        if (!keys && addButton is not null)
        {
            var busy = model.Adding(service);
            addButton.IsEnabled = !busy;
            if (busy) { addStatus.SetText(MultiAccountWords.SigningIn); addStatusFold.Open = true; }
            else if (addStatus.Text == MultiAccountWords.SigningIn) { addStatus.SetText(""); addStatusFold.Open = false; }
        }
        footFold.Open = !local && listed.Count > 1;
        if (keys && listed.Count > 0 != KeysListed)
        {
            KeysListed = listed.Count > 0;
            KeysListedChanged?.Invoke(KeysListed);
        }
        blockFold.Open = local || listed.Count > 0 || canAdd;
        foreach (var gone in rows.Keys.Where(k => listed.All(r => r.Id != k)).ToArray())
        {
            list.Children.Remove(rows[gone]);
            rows.Remove(gone);
        }
        for (var i = 0; i < listed.Count; i++)
        {
            var row = listed[i];
            if (!rows.TryGetValue(row.Id, out var control))
            {
                rows[row.Id] = control = new AccountRowControl(model, row, pulse, (id, target, _) => model.MoveTo(id, target));
                list.Children.Add(control);
            }
            else control.Apply(row, animate);
            if (list.Children.IndexOf(control) != i)
            {
                list.Children.Remove(control);
                list.Children.Insert(Math.Min(i, list.Children.Count), control);
            }
        }
    }
}
