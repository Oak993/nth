// PORT-ONLY: T11 step 2. One connected service's block in Settings > Models > Accounts: its mark, name and a one-line summary ("Signed in · Max plan" with its week bar for one account; "2 accounts · the same order as the composer's popover" for
// several), a ⋯ menu (Add another account, Usage details, Sign out) and, for several accounts, the list that folds open under it (OD-0076) with "Add another account" at its foot. One account looks as it does today: no list, no name.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class ServiceBlockControl : Decorator
{
    private readonly AccountsSettingsModel model;
    private readonly ModelPulse pulse;
    private readonly Action<ProviderKind> usageDetails;
    private readonly Action<ProviderKind> openKeys;
    private readonly Button? keysButton;
    private readonly LocalServerBox? serverBox;
    private readonly ProviderKind service;
    private readonly CzCrossfadeText title;
    private readonly CzCrossfadeText summary = new("") { FontSize = 12, Secondary = true };
    private readonly LibraryBar mini = new() { Width = 110 };
    private readonly CzCrossfadeText miniText = new("") { FontSize = 11.5, Secondary = true };
    private readonly StackPanel miniBox;
    private readonly StackPanel list = new() { Spacing = 0 };
    private readonly CzFoldRow listFold;
    private readonly Dictionary<Guid, AccountRowControl> rows = [];
    private readonly Control addRow;
    private readonly Control addHost;
    private ServiceBlockView view;

    internal ServiceBlockControl(AccountsSettingsModel model, ServiceBlockView first, ModelPulse pulse, Action<ProviderKind> usageDetails, Action<ProviderKind> openKeys)
    {
        this.model = model;
        this.pulse = pulse;
        this.usageDetails = usageDetails;
        this.openKeys = openKeys;
        service = first.Service;
        view = first;
        title = new CzCrossfadeText(first.Title) { FontSize = 14, FontWeight = Avalonia.Media.FontWeight.Medium };
        var mark = new ProviderIcon { Provider = service, Size = 22, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        miniBox = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { mini, miniText } };
        var more = MenuPopover.More("More for " + first.Title, "service-more-" + service.Id, () => model.BlockMenu(service), Pick);
        more.VerticalAlignment = VerticalAlignment.Center;
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, summary } };
        if (first.ActionLabel is { } label)
        {
            keysButton = LibraryButtons.Make(label, () => openKeys(service), ButtonLook.Plain, "service-keys-" + service.Id);
            keysButton.VerticalAlignment = VerticalAlignment.Center;
        }
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,Auto,Auto,22"), ColumnSpacing = 10, MinHeight = 56, Margin = new Thickness(0, 0) };
        Grid.SetColumn(words, 1);
        Grid.SetColumn(miniBox, 2);
        Grid.SetColumn(more, 4);
        foreach (var part in new Control[] { mark, words, miniBox, more }) head.Children.Add(part);
        if (keysButton is not null)
        {
            Grid.SetColumn(keysButton, 3);
            head.Children.Add(keysButton);
        }
        var header = new Border { Padding = new Thickness(16, 6, 12, 6), Child = head };

        addRow = AddRow();
        addHost = new Border { Child = addRow };
        listFold = new CzFoldRow(new StackPanel { Children = { list, addHost } }, first.Several);
        var body = new StackPanel { Children = { header, listFold } };
        if (first.ServerBox)
        {
            serverBox = new LocalServerBox(model);
            body.Children.Add(serverBox);
        }
        Child = body;
        Apply(first, animate: false);
    }

    internal ProviderKind Service => service;

    internal ServiceBlockView View => view;

    internal bool ListOpen => listFold.Open;

    internal double ListFraction => listFold.Fraction;

    internal IReadOnlyDictionary<Guid, AccountRowControl> Rows => rows;

    internal string SummaryShown => summary.Text;

    internal LocalServerBox? ServerBox => serverBox;

    internal Button? KeysButton => keysButton;

    internal string TitleShown => title.Text;

    private Control AddRow()
    {
        var plus = LibraryUi.Glyph("plus", 12, ChromeTokens.Accent, SwiftFont.Weight.Medium);
        var text = LibraryUi.Text(MultiAccountWords.AddAnother, 13, weight: SwiftFont.Weight.Medium);
        text.Foreground = ChromeTokens.Accent.Brush(text);
        var button = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 10), Children = { plus, text } }, () => _ = model.AddAsync(service));
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(MultiAccountWords.AddAnother, "service-add-" + service.Id);
        return button;
    }

    internal void Apply(ServiceBlockView next, bool animate = true)
    {
        view = next;
        title.SetText(next.Title);
        summary.SetText(next.Summary);
        listFold.Open = next.Several;
        addHost.IsVisible = next.CanAdd;
        miniBox.IsVisible = !next.Several && next.MiniFraction is not null;
        mini.Set(next.MiniFraction ?? 0, animate);
        miniText.SetText(next.MiniText);
        foreach (var gone in rows.Keys.Where(k => next.Rows.All(r => r.Id != k)).ToArray())
        {
            list.Children.Remove(rows[gone]);
            rows.Remove(gone);
        }
        for (var i = 0; i < next.Rows.Count; i++)
        {
            var row = next.Rows[i];
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

    internal void Pick(string key)
    {
        switch (key)
        {
            case "add": _ = model.AddAsync(service); break;
            case "usage": usageDetails(service); break;
            case "signout": break; // disabled with one account: the last one is signed out in the service's own app
        }
    }
}
