// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.6 first run, 4.3 the delegate responder, 5.4 reading, 12.1 the owner's
// 2026-10-08 rules, 12.6 the stop chord; ED-NEW-dextra-as8-8). No Swift twin.
// The parts the first-run card and Settings › Assistant share: their words, the brain's three menus (provider, account,
// model) over what the app has, the reading switches, "Answer questions for me" with its scope, and the rules shown
// read-only. Built from the Settings pages' own controls (ChromeRow, SettingsSwitch, CzMenuButton, ChromeSegmentedPicker).
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Assistant.Views;

/// <summary>Every word of Settings › Assistant and the first-run card.</summary>
public static class AssistantSettingsWords
{
    public const string Title = "Assistant";
    public const string Lead = "Dextra runs missions across your chats, asks before anything leaves this PC, and stops the moment you say so.";
    public const string RowOn = "Dextra";
    public const string RowOnDetail = "Off stops every mission and keeps nothing running.";
    public const string RowProvider = "Brain";
    public const string RowProviderDetail = "The provider Dextra itself thinks on.";
    public const string RowAccount = "Account";
    public const string RowModel = "Model";
    public const string Automatic = "Automatic";
    public const string AutomaticDetail = "Automatic picks the strongest model on the account with the most headroom.";
    public const string FirstAccount = "First account";
    public const string DefaultModel = "Provider default";
    public const string Reading = "What it may read";
    public const string RowSame = "Summaries of chats on the same provider";
    public const string RowSameDetail = "Off: Dextra sees titles, projects, states and costs only.";
    public const string RowOther = "Also chats on other providers";
    public const string RowOtherDetail = "Local-model chats only ever reach a brain on this PC. Hide any chat from its menu.";
    public const string Delegate = "Answer questions for me";
    public const string DelegateDetail = "Until the safety suite passes, every question still comes to you first.";
    public const string ThisProject = "This project only";
    public const string Everywhere = "Everywhere";
    public const string Stop = "Stop";
    public const string RowChord = "Stop chord";
    public const string RowChordDetail = "Stops every mission and Tactus at once, from anywhere. A new chord applies when work next starts.";
    public const string Permissions = "Permissions";
    public const string NoGrants = "No grants yet. Cards you answer with For this mission or Always… show here.";
    public const string Revoke = "Revoke";
    public const string Rules = "What it does alone and what it asks";
    public const string RulesLink = "Change in Settings";
    public const string FirstDuty = "A first duty";
    public const string FirstDutyDetail = "One standing job to start with. You can add more or turn it off later.";
    public const string FirstRunTitle = "Hi, I'm Dextra.";
    public const string FirstRunLead = "Four choices, each set to the safe side. Nothing leaves this PC and no mission starts until you press Done.";
    public const string Done = "Done";
    public const string Off = "Dextra is off.";
    public const string TurnOn = "Turn on";

    public static IReadOnlyList<string> Keywords { get; } =
        ["assistant", "dextra", "astur", "asturs", "missions", "brain", "delegate", "answer questions", "grants", "permissions", "stop chord", "stop all", "privacy", "reading"];

    /// <summary>A grant's line: what it allows, where, and for how long.</summary>
    public static string GrantLine(Grant grant, Func<Guid, string?>? missionTitle = null) =>
        $"{ActionClassName(grant.Class)} · {string.Join(", ", grant.Target.Values)}";

    public static string GrantScope(Grant grant, Func<Guid, string?>? missionTitle = null) => grant.Scope.Kind switch
    {
        GrantScopeKind.Once => "Once",
        GrantScopeKind.Mission => grant.Scope.MissionId is { } id && missionTitle?.Invoke(id) is { Length: > 0 } title ? "This mission: " + title : "This mission only",
        GrantScopeKind.Until => grant.Scope.Until is { } end ? "Until " + TaskWords.Local(end).ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : "Until a date",
        _ => "Until you revoke it",
    };

    public static string ActionClassName(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Agents => "Run its agents",
        ActionClass.Work => "Change files in the project",
        ActionClass.Run => "Run the project's build and tests",
        ActionClass.ReadWeb => "Read the web",
        ActionClass.ReadLocal => "Read files in the access folders",
        ActionClass.Login => "Sign in",
        ActionClass.WriteElsewhere => "Write outside the access folders",
        ActionClass.ConnectorWrite => "Write through a connector",
        ActionClass.Outward => "Send, post or reply",
        ActionClass.Commitment => "Book, accept or agree",
        ActionClass.Destructive => "Delete outside git",
        ActionClass.Install => "Install software",
        ActionClass.Money => "Pay (card, then Windows Hello)",
        ActionClass.PersonalDataOut => "Share personal data",
        ActionClass.HardDeny => "Password managers, security settings, its own grants",
        _ => "Anything it does not recognise",
    };

    /// <summary>The four groups of 12.1, in order: alone, asks, always confirmed, never.</summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<ActionClass> Classes)> RuleGroups { get; } =
    [
        ("Does alone", [.. ActionClasses.All.Where(c => ActionClasses.DefaultOf(c) == ClassDefault.Alone)]),
        ("Asks first", [.. ActionClasses.All.Where(c => ActionClasses.DefaultOf(c) is ClassDefault.AsksGrantPossible or ClassDefault.AsksEveryTime)]),
        ("Always asks you to confirm", [.. ActionClasses.All.Where(c => ActionClasses.DefaultOf(c) == ClassDefault.AlwaysConfirmed)]),
        ("Never", [.. ActionClasses.All.Where(c => ActionClasses.DefaultOf(c) == ClassDefault.Never)]),
    ];

    /// <summary>The stop chords offered (the default first).</summary>
    public static IReadOnlyList<string> Chords { get; } = ["Ctrl+Alt+Esc", "Ctrl+Alt+Pause", "Ctrl+Shift+Pause", "Ctrl+Alt+End"];
}

/// <summary>What the brain's menus offer: the providers that are on and installed, their accounts and their models.</summary>
public sealed record BrainChoices(
    Func<IReadOnlyList<ProviderKind>> Providers,
    Func<ProviderKind, IReadOnlyList<(Guid Id, string Name)>> Accounts,
    Func<ProviderKind, IReadOnlyList<(string Id, string Name)>> Models)
{
    public static BrainChoices None { get; } = new(() => [], _ => [], _ => []);

    /// <summary>The app's: available providers, each one's enabled accounts, and its model catalogue.</summary>
    public static BrainChoices Of(AppModel model) => new(
        () => model.Providers.AvailableProviders,
        provider => [.. model.Settings.Accounts.ForService(provider).Where(a => a.Enabled)
            .Select(a => (a.Id, model.Settings.Accounts.DisplayName(a.Id) ?? model.Settings.Accounts.DefaultName(a.Id)))],
        provider => model.Providers.Catalogs.TryGetValue(provider, out var catalog) ? [.. catalog.Select(m => (m.Id, m.Name))] : []);
}

/// <summary>The rows the card and the page share, over one <see cref="DextraSettings"/>.</summary>
internal sealed class AssistantRows
{
    private readonly DextraSettings settings;
    private readonly BrainChoices brain;

    public AssistantRows(DextraSettings settings, BrainChoices? brain)
    {
        this.settings = settings;
        this.brain = brain ?? BrainChoices.None;
        ProviderMenu = new CzMenuButton(AssistantSettingsWords.RowProvider, ProviderOptions(), ProviderId(settings.Value.BrainProvider));
        ProviderMenu.SelectionChanged += id => settings.Update(p => p with
        {
            BrainProvider = Enum.TryParse<ProviderKind>(id, out var kind) && id != "auto" ? kind : null,
            BrainAccount = null,
            BrainModel = null,
        });
        AccountMenu = new CzMenuButton(AssistantSettingsWords.RowAccount, AccountOptions(), AccountId(settings.Value.BrainAccount));
        AccountMenu.SelectionChanged += id => settings.Update(p => p with { BrainAccount = Guid.TryParse(id, out var account) ? account : null });
        ModelMenu = new CzMenuButton(AssistantSettingsWords.RowModel, ModelOptions(), settings.Value.BrainModel ?? "default");
        ModelMenu.SelectionChanged += id => settings.Update(p => p with { BrainModel = id == "default" ? null : id });
        AccountRow = new ChromeRow(AssistantSettingsWords.RowAccount, () => AccountMenu);
        ModelRow = new ChromeRow(AssistantSettingsWords.RowModel, () => ModelMenu);

        SameSwitch = Switch(AssistantSettingsWords.RowSame, p => p.ReadSameProvider, (p, on) => p with { ReadSameProvider = on });
        OtherSwitch = Switch(AssistantSettingsWords.RowOther, p => p.ReadOtherProviders, (p, on) => p with { ReadOtherProviders = on });
        DelegateSwitch = Switch(AssistantSettingsWords.Delegate, p => p.Delegate != DelegateScope.Off,
            (p, on) => p with { Delegate = on ? (p.Delegate == DelegateScope.Off ? DelegateScope.ThisProject : p.Delegate) : DelegateScope.Off });
        ScopePicker = new ChromeSegmentedPicker<DelegateScope>(
            new ChromeSegmentedOption<DelegateScope>[] { new(DelegateScope.ThisProject, AssistantSettingsWords.ThisProject), new(DelegateScope.Everywhere, AssistantSettingsWords.Everywhere) },
            new Binding<DelegateScope>(() => settings.Value.Delegate == DelegateScope.Off ? DelegateScope.ThisProject : settings.Value.Delegate,
                scope => settings.Update(p => p.Delegate == DelegateScope.Off ? p : p with { Delegate = scope })));
        AutomationProperties.SetName(ScopePicker, AssistantSettingsWords.Delegate);
        ScopeRow = new ChromeRow("", () => ScopePicker);
        Show();
    }

    public CzMenuButton ProviderMenu { get; }
    public CzMenuButton AccountMenu { get; }
    public CzMenuButton ModelMenu { get; }
    public ChromeRow AccountRow { get; }
    public ChromeRow ModelRow { get; }
    public SettingsSwitch SameSwitch { get; }
    public SettingsSwitch OtherSwitch { get; }
    public SettingsSwitch DelegateSwitch { get; }
    public ChromeSegmentedPicker<DelegateScope> ScopePicker { get; }
    public ChromeRow ScopeRow { get; }

    /// <summary>The brain's rows: provider, then account and model once a provider is picked.</summary>
    public Control BrainRows()
    {
        var rows = new StackPanel();
        rows.Children.Add(new ChromeRow(AssistantSettingsWords.RowProvider, () => ProviderMenu, AssistantSettingsWords.AutomaticDetail));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(AccountRow);
        rows.Children.Add(ModelRow);
        return rows;
    }

    public Control ReadingRows()
    {
        var rows = new StackPanel();
        rows.Children.Add(new ChromeRow(AssistantSettingsWords.RowSame, () => SameSwitch, AssistantSettingsWords.RowSameDetail));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(new ChromeRow(AssistantSettingsWords.RowOther, () => OtherSwitch, AssistantSettingsWords.RowOtherDetail));
        return rows;
    }

    public Control DelegateRows()
    {
        var rows = new StackPanel();
        rows.Children.Add(new ChromeRow(AssistantSettingsWords.Delegate, () => DelegateSwitch, AssistantSettingsWords.DelegateDetail));
        rows.Children.Add(ScopeRow);
        return rows;
    }

    /// <summary>Brings the menus and the scope row in line with the settings (after any change).</summary>
    public void Show()
    {
        var value = settings.Value;
        ProviderMenu.SetOptions(ProviderOptions(), ProviderId(value.BrainProvider));
        var picked = value.BrainProvider is not null;
        var accounts = AccountOptions();
        AccountMenu.SetOptions(accounts, AccountId(value.BrainAccount));
        AccountRow.IsVisible = picked && accounts.Count > 2;
        ModelMenu.SetOptions(ModelOptions(), value.BrainModel ?? "default");
        ModelRow.IsVisible = picked;
        ScopeRow.IsVisible = value.Delegate != DelegateScope.Off;
    }

    private SettingsSwitch Switch(string name, Func<DextraPreferences, bool> read, Func<DextraPreferences, bool, DextraPreferences> write)
    {
        var toggle = new SettingsSwitch(new Binding<bool>(() => read(settings.Value), on => settings.Update(p => write(p, on))));
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    private static string ProviderId(ProviderKind? provider) => provider?.ToString() ?? "auto";

    private static string AccountId(Guid? account) => account?.ToString("D") ?? "first";

    private IReadOnlyList<(string Id, string Text)> ProviderOptions()
    {
        var list = new List<(string, string)> { ("auto", AssistantSettingsWords.Automatic) };
        var providers = brain.Providers().ToList();
        if (settings.Value.BrainProvider is { } picked && !providers.Contains(picked)) providers.Add(picked);
        list.AddRange(providers.Select(p => (p.ToString(), p.DisplayName)));
        return list;
    }

    private IReadOnlyList<(string Id, string Text)> AccountOptions()
    {
        var list = new List<(string, string)> { ("first", AssistantSettingsWords.FirstAccount) };
        if (settings.Value.BrainProvider is { } provider) list.AddRange(brain.Accounts(provider).Select(a => (a.Id.ToString("D"), a.Name)));
        return list;
    }

    private IReadOnlyList<(string Id, string Text)> ModelOptions()
    {
        var list = new List<(string, string)> { ("default", AssistantSettingsWords.DefaultModel) };
        if (settings.Value.BrainProvider is { } provider) list.AddRange(brain.Models(provider));
        if (settings.Value.BrainModel is { } model && list.All(m => m.Item1 != model)) list.Add((model, model));
        return list;
    }

    /// <summary>The owner's rules (12.1) read-only: four short groups, each class in plain words.</summary>
    public static Control RulesList(Action? openSettings)
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(14, 12) };
        foreach (var (heading, classes) in AssistantSettingsWords.RuleGroups)
        {
            var head = new TextBlock { Text = heading };
            SwiftFont.System(12, SwiftFont.Weight.Semibold).Apply(head);
            head.Foreground = Chrome.PrimaryText.Brush(head);
            AutomationProperties.SetHeadingLevel(head, 4);
            var body = new TextBlock { Text = string.Join(" · ", classes.Select(AssistantSettingsWords.ActionClassName)), TextWrapping = TextWrapping.Wrap };
            SwiftFont.System(12).Apply(body);
            body.Foreground = Chrome.SecondaryText.Brush(body);
            stack.Children.Add(new StackPanel { Spacing = 2, Children = { head, body } });
        }
        if (openSettings is not null)
        {
            var link = new CzCapsuleButton(AssistantSettingsWords.RulesLink, link: true) { HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(link, AssistantSettingsWords.RulesLink);
            link.Invoked += openSettings;
            stack.Children.Add(link);
        }
        return stack;
    }
}
