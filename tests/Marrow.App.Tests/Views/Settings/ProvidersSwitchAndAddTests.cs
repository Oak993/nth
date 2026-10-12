using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Settings > Providers, the owner's report on the release build: the provider's on/off switch did nothing when clicked (ED-NEW-providers-switch-1), and the page had no way to add a second
// account (ED-NEW-providers-add-1). Since OD-0149 (ED-NEW-providersmerge-1) the page is the one place for every login: each card holds its service's accounts in order, and the account-wide group closes the page.
[Collection("Headless")]
public sealed class ProvidersSwitchAndAddTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static readonly ProviderStatus SignedInStatus = new() { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) };

    // OD-0143 (ED-NEW-nocta-1, ED-NEW-reltests-1): the Nocta switch's 22 pt dome sits 2 in from its 46 pt track, so its left edge is at x = 2 off and slides 20 to x = 22 on, on a spring (response 0.38 s, damping 0.82)
    // that is settled, and snaps to its target, after the spring's own SettlingDuration (0.51 s); a switch is read at rest only after that.
    private const double KnobOff = 2, KnobOn = 2 + 20;
    private static readonly double SwitchSettledMs = SmallSwitch.Slide.Duration * 1000;

    [Fact]
    [Trait("Decision", "ED-NEW-providers-switch-1")]
    public void A_click_on_a_providers_switch_turns_the_provider_off_slides_the_knob_and_persists_and_a_second_click_turns_it_back_on() => With(
        () => new ProviderSettingsSection(ProviderKind.Codex, Binding<bool>.Constant(true)), (view, model, h, w) =>
    {
        Tick(h, 400); Drain(w); // the card has opened
        var toggle = Assert.Single(Nodes<SmallSwitch>(view));
        var toggles = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        Assert.True(model.Settings.IsEnabled(ProviderKind.Codex)); Assert.Equal(KnobOn, toggle.KnobNow().X, 3); Assert.Equal(ToggleState.On, toggles.ToggleState);
        Click(w, toggle);
        Assert.False(model.Settings.IsEnabled(ProviderKind.Codex)); Assert.Equal(ToggleState.Off, toggles.ToggleState);
        Assert.False(new AppSettings().IsEnabled(ProviderKind.Codex)); // persisted: a fresh read of the settings store has it off
        Tick(h, 72); var halfway = toggle.KnobNow().X; Assert.InRange(halfway, KnobOff + 0.5, KnobOn - 0.5); // the designed 0.38 s spring is under way, not a jump
        Assert.Equal(KnobOn - (KnobOn - KnobOff) * SmallSwitch.Slide.Progress(0.072), halfway, 3); // and 72 ms along that spring exactly
        Tick(h, SwitchSettledMs); Assert.Equal(KnobOff, toggle.KnobNow().X, 3);
        Click(w, toggle); Tick(h, SwitchSettledMs);
        Assert.True(model.Settings.IsEnabled(ProviderKind.Codex)); Assert.True(new AppSettings().IsEnabled(ProviderKind.Codex)); Assert.Equal(KnobOn, toggle.KnobNow().X, 3);
    }, model => Seed(model, ProviderKind.Codex, SignedInStatus));

    private static Button? AddRow(Control view) => Nodes<Button>(view).SingleOrDefault(b => AutomationProperties.GetName(b) == MultiAccountWords.AddAnother);

    private void WithAccounts(ProviderKind provider, ProviderStatus status, Action<Control, AccountsRig, AppModel, AppModelHarness, Avalonia.Controls.Window> check, Action<AccountsRig>? setup = null)
    {
        var rig = new AccountsRig();
        rig.Connect(provider, "Max");
        setup?.Invoke(rig);
        var context = new MultiAccountContext(rig.Model, rig.PillsModel, rig.Editor, _ => [], () => { });
        IDisposable? seam = null; // set on the headless UI thread, where the page reads it (the seam is an async-local)
        With(() => new ProviderSettingsSection(provider, Binding<bool>.Constant(true)), (view, model, h, w) =>
        {
            try { Tick(h, 400); Drain(w); check(view, rig, model, h, w); }
            finally { seam?.Dispose(); }
        }, model => Seed(model, provider, status), before: _ => seam = MultiAccountSettingsSeams.Context.Override(new MultiAccountOffer(_ => context)));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-providers-add-1")]
    public void A_signed_in_service_that_keeps_several_accounts_offers_Add_another_account_and_it_runs_the_Models_pages_sign_in() => WithAccounts(ProviderKind.Claude, SignedInStatus, (view, rig, model, h, w) =>
    {
        var add = Assert.IsType<Button>(AddRow(view));
        Assert.Contains(MultiAccountWords.AddAnother, Text(add));
        Click(w, add);
        Assert.Equal([ProviderKind.Claude], rig.Actions.Added);
        Seed(model, ProviderKind.Claude, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Drain(w);
        Assert.Null(AddRow(view)); // signed out, the card offers Sign in instead
    });

    [Theory]
    [Trait("Decision", "ED-NEW-providers-add-1")]
    [InlineData(ProviderKind.Antigravity)] // keeps one account
    [InlineData(ProviderKind.Zai)] // kept by API keys (Keys and order)
    public void A_service_that_cannot_add_an_account_shows_no_add_row(ProviderKind provider) => WithAccounts(provider, SignedInStatus, (view, _, _, _, _) => Assert.Null(AddRow(view)));

    private static bool Showing(Control control) => control.IsEffectivelyVisible && control.GetSelfAndVisualAncestors().OfType<Visual>().Aggregate(1.0, (a, v) => a * v.Opacity) > 0.01;

    [Theory]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    public void A_signed_in_service_with_two_accounts_lists_them_in_its_card_with_one_add_row_and_the_order_in_reach(ProviderKind provider) => WithAccounts(provider, SignedInStatus, (view, rig, model, h, w) =>
    {
        var section = Assert.IsType<ProviderSettingsSection>(view);
        var part = Assert.IsType<ServiceBlockControl>(section.AccountsPart);
        Assert.Same(part, Nodes<ServiceBlockControl>(Assert.Single(Nodes<ChromeCard>(section))).Single()); // inside the service's own card
        Assert.True(part.ListOpen && part.HeadingOpen && part.FootOpen && part.AddOpen);
        var ids = rig.Accounts.ForService(provider).Select(a => a.Id).ToArray();
        Assert.Equal(ids, part.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.Id));
        Assert.Single(Nodes<Button>(view), b => AutomationProperties.GetName(b) == MultiAccountWords.AddAnother && Showing(b)); // one add action for the service
        Assert.Equal(2, Nodes<Control>(view).Count(c => AutomationProperties.GetAutomationId(c)?.StartsWith("account-grip-", StringComparison.Ordinal) == true && Showing(c)));
        var words = Text(view);
        foreach (var expected in new[] { MultiAccountWords.AccountsHeading, MultiAccountWords.Several(2), MultiAccountWords.AccountsFoot })
            Assert.Contains(expected, words);
        Assert.Equal(3, Nodes<SmallSwitch>(view).Count(Showing)); // the provider's own and one per account
        // the order: Alt+Down moves the first account below the second, and nth then starts on the other one
        part.Rows[ids[0]].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt }); Drain(w); Tick(h, 400); Drain(w);
        Assert.Equal([ids[1], ids[0]], rig.Accounts.ForService(provider).Select(a => a.Id));
        Assert.Equal(ids[1], rig.Accounts.Choose(provider)!.Id);
        Assert.Equal([ids[1], ids[0]], part.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.Id));
        Click(w, AddRow(view)!);
        Assert.Equal([provider], rig.Actions.Added);
    }, setup: rig => rig.ConnectAnother(provider, "Max"));

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void An_api_key_service_card_keeps_its_API_key_box_and_offers_another_key_under_it() => WithAccounts(ProviderKind.Zai, SignedInStatus, (view, rig, model, h, w) =>
    {
        var section = Assert.IsType<ProviderSettingsSection>(view);
        var part = Assert.IsType<ServiceBlockControl>(section.AccountsPart);
        Assert.NotNull(Row(view, "API key")); // the first key, as the source's card has it
        Assert.True(part.AddOpen);
        var add = Nodes<Button>(view).Single(b => AutomationProperties.GetName(b) == MultiAccountWords.AddKey);
        Assert.True(Showing(add));
        Click(w, add); Tick(h, 400); Drain(w);
        Assert.True(part.KeyBox!.Open);
        Assert.True(Showing(part.KeyBox.Input));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-keys-2")]
    public void With_two_keys_the_list_stands_in_place_of_the_single_API_key_box_and_the_box_comes_back_with_one() => WithAccounts(ProviderKind.Zai, SignedInStatus, (view, rig, model, h, w) =>
    {
        var section = Assert.IsType<ProviderSettingsSection>(view);
        var part = Assert.IsType<ServiceBlockControl>(section.AccountsPart);
        var fold = Assert.IsType<Marrow.UI.Port.Settings.CzFoldRow>(section.KeyFold);
        var box = Assert.IsType<ChromeRow>(Row(view, "API key"));
        Assert.True(part.KeysListed && part.ListOpen);
        Assert.False(fold.Open); Assert.Equal(0, fold.Bounds.Height, 3); Assert.False(Showing(box)); // one place for the keys: the list, not the box and the list
        var ids = rig.Accounts.ForService(ProviderKind.Zai).Select(a => a.Id).ToArray();
        Assert.Equal(ids, part.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.Id)); // every key in the order nth uses them, the first key too
        Assert.All(part.Rows.Values, row => Assert.True(Showing(row.Eye!)));
        Assert.Contains(rig.Model.Menu(ids[1]), e => e.Key == "rename"); Assert.Contains(rig.Model.Menu(ids[1]), e => e.Key == "top"); Assert.Contains(rig.Model.Menu(ids[1]), e => e.Key == "remove"); // a name, the key used first, remove
        Assert.True(Showing(Nodes<Button>(view).Single(b => AutomationProperties.GetName(b) == MultiAccountWords.AddKey)));
        rig.Model.RemoveKeyAsync(ids[1]).GetAwaiter().GetResult(); Drain(w); Tick(h, 400); Drain(w);
        Assert.False(part.KeysListed);
        Assert.True(fold.Open); Assert.True(fold.Bounds.Height > 20); Assert.True(Showing(box)); // one key again: the source's box
        Assert.True(Showing(Assert.Single(Nodes<TextBox>(box))));
    }, setup: rig => Assert.Null(rig.Model.AddKeyAsync(ProviderKind.Zai, "zai-second-key-bbbb").GetAwaiter().GetResult()));

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void The_page_ends_with_the_account_wide_group_which_opens_with_a_second_account() => WithAccountsPage((view, rig, model, h, w) =>
    {
        var page = Assert.IsType<ProvidersSettingsPage>(view);
        var stack = Assert.IsType<StackPanel>(page.Child);
        var rules = Assert.IsType<AccountRulesView>(page.Rules);
        Assert.Same(rules, stack.Children[^1]); // under every provider
        Assert.Equal(16, Nodes<ProviderSettingsSection>(view).Length);
        Assert.False(rules.RunOpen);
        Assert.Equal(0, rules.Bounds.Height, 3); // folded, it adds no more than the page's space between sections
        rig.ConnectAnother(ProviderKind.Claude, "Max"); rig.Model.Refresh(); Drain(w); Tick(h, 600); Drain(w);
        Assert.True(rules.RunOpen && rules.HandshakeOpen);
        Assert.Contains(MultiAccountWords.RunHeading, Text(rules));
        Assert.Contains(MultiAccountWords.HandshakeHeading, Text(rules));
        Assert.True(rules.Bounds.Height > 100);
    });

    private void WithAccountsPage(Action<Control, AccountsRig, AppModel, AppModelHarness, Avalonia.Controls.Window> check)
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        var context = new MultiAccountContext(rig.Model, rig.PillsModel, rig.Editor, _ => [], () => { });
        IDisposable? seam = null;
        With(() => new ProvidersSettingsPage(), (view, model, h, w) =>
        {
            try { Tick(h, 400); Drain(w); check(view, rig, model, h, w); }
            finally { seam?.Dispose(); }
        }, model => Seed(model, ProviderKind.Claude, SignedInStatus), before: _ => seam = MultiAccountSettingsSeams.Context.Override(new MultiAccountOffer(_ => context)));
    }

    // The whole Settings window on its Providers page, its HTTP refused (a typed key is never sent anywhere).
    private void WithProvidersPage(Action<Control, AppModel, AppModelHarness, Avalonia.Controls.Window> check, Action<AppModel>? seed = null, double height = 1100)
    {
        IDisposable? transport = null; InProcessHttpHandler? refused = null;
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            try { check(view, model, h, w); }
            finally { transport?.Dispose(); refused?.Dispose(); }
        }, seed, before: _ => { refused = new InProcessHttpHandler(); transport = URLSession.Transport.Override(refused); }, height: height);
    }

    // The pointer lands on the control itself (or inside it), not on something drawn over it or on a frozen page.
    private static void Hits(Avalonia.Controls.Window w, Control control, string what)
    {
        Assert.True(control.IsEffectivelyVisible && control.IsEffectivelyEnabled, what + " is hidden or disabled");
        var hit = w.InputHitTest(At(control, w)) as Avalonia.Visual;
        Assert.True(hit is not null && (ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control)), what + " is covered by " + hit?.GetType().Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-providers-switch-1")]
    public void Every_control_of_a_provider_section_takes_the_pointer_and_keyboard_after_the_page_is_scrolled_to_it() => WithProvidersPage((view, model, h, w) =>
    {
        Click(w, RowButton(view, "Providers")); Tick(h, 400); Drain(w);
        var zai = Nodes<ProviderSettingsSection>(view).Single(s => s.Provider == ProviderKind.Zai);
        var scroll = Nodes<ScrollViewer>(Field<Control>(view, "detail")).First();
        Reveal(w, h, zai); w.MouseWheel(At(scroll, w), new Avalonia.Vector(0, -1)); Drain(w); Tick(h, 400); Drain(w); // a wheel turn as a person scrolls, then the rows thaw
        Assert.True(scroll.Offset.Y > 0, "the page did not scroll");
        Reveal(w, h, zai); Tick(h, 400); Drain(w);
        Assert.True(zai.IsOpenAndSettled);
        // the switch
        var toggle = Assert.Single(Nodes<SmallSwitch>(zai)); Hits(w, toggle, "Z.ai's switch");
        Click(w, toggle); Tick(h, SwitchSettledMs); Assert.False(model.Settings.IsEnabled(ProviderKind.Zai)); Assert.Equal(KnobOff, toggle.KnobNow().X, 3);
        Click(w, toggle); Tick(h, SwitchSettledMs); Assert.True(model.Settings.IsEnabled(ProviderKind.Zai)); Assert.Equal(KnobOn, toggle.KnobNow().X, 3);
        // the API key box and its eye
        var key = Assert.Single(Nodes<TextBox>(zai)); Hits(w, key, "Z.ai's API key box");
        Type(w, key, "k1"); Assert.True(key.IsFocused); Assert.Equal("k1", key.Text); Assert.Equal("k1", model.Settings.ApiKeyInput(ProviderKind.Zai));
        Assert.Equal('\u2022', key.PasswordChar);
        var eye = Nodes<Button>(zai).Single(b => ToolTip.GetTip(b) as string == "Show the API key"); Hits(w, eye, "Z.ai's eye");
        Click(w, eye); Assert.Equal('\0', key.PasswordChar);
        // the header's chevron folds the card shut and open again
        var header = Assert.Single(Nodes<Button>(Assert.Single(Nodes<CollapsibleProviderHeader>(zai)))); Hits(w, header, "Z.ai's header");
        Click(w, header); Tick(h, 600); Drain(w); Assert.False(zai.IsOpenAndSettled);
        Reveal(w, h, zai); Tick(h, 200); Drain(w); // the shorter page's offset was clamped under the fold: the header is scrolled to, and the rows thaw 150 ms after the movement (SettingsScrollTracker)
        Hits(w, header, "Z.ai's header, folded");
        Click(w, header); Tick(h, 600); Drain(w); Assert.True(zai.IsOpenAndSettled);
        // a CLI provider's binary path box
        var codex = Nodes<ProviderSettingsSection>(view).Single(s => s.Provider == ProviderKind.Codex);
        Reveal(w, h, codex); Tick(h, 400); Drain(w);
        var path = Assert.Single(Nodes<TextBox>(codex)); Hits(w, path, "Codex's binary path box");
        Type(w, path, "codex-x.exe"); Assert.Equal("codex-x.exe", model.Settings.BinaryPath(ProviderKind.Codex));
        Assert.Single(Nodes<SmallSwitch>(zai)); Assert.Single(Nodes<SmallSwitch>(codex)); // nothing was built twice
    }, model => { Seed(model, ProviderKind.Zai, SignedInStatus); Seed(model, ProviderKind.Codex, SignedInStatus); }, height: 700);

    [Fact]
    [Trait("Decision", "ED-NEW-providers-reveal-1")]
    public void Keys_and_order_lands_on_the_services_open_section_scrolled_to_the_top_of_the_page() => WithProvidersPage((view, model, h, w) =>
    {
        Assert.Empty(Nodes<ProvidersSettingsPage>(view)); // on General
        SettingsNavigation.Shared.RequestedProvider = ProviderKind.Deepseek; SettingsNavigation.Shared.RequestedPage = SettingsPage.Providers; // what "Keys and order" asked before OD-0149 (a request to open one service's card, which SettingsNavigation still honours)
        Drain(w); for (var i = 0; i < 8; i++) { Tick(h, 100); Drain(w); }
        var page = Assert.Single(Nodes<ProvidersSettingsPage>(view));
        var section = Nodes<ProviderSettingsSection>(view).Single(s => s.Provider == ProviderKind.Deepseek);
        Assert.Equal(ProviderKind.Deepseek, page.Revealed); Assert.True(page.IsOpen(ProviderKind.Deepseek)); Assert.True(section.IsOpenAndSettled);
        var scroll = Nodes<ScrollViewer>(Field<Control>(view, "detail")).First();
        var top = section.TranslatePoint(default, scroll)!.Value.Y;
        var inset = Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight + 16;
        Assert.True(scroll.Offset.Y > 0, "the page stayed at its top");
        var atEnd = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 0.5;
        Assert.True(Math.Abs(top - inset) <= 1 || atEnd && top >= inset - 1, $"the section starts at {top}, the page's first section at {inset}");
        Assert.True(top + section.Bounds.Height <= scroll.Viewport.Height + 1 || Math.Abs(top - inset) <= 1, "the section is not in view");
        Assert.Null(SettingsNavigation.Shared.RequestedProvider);
    }, height: 600);

    // ED-NEW-softscroll-2 (1): one notch of D points from rest follows A (1 - (1 + t / tau) e^(-t / tau)), tau = ScrollPhysics.GlideStage and A = D + RestSpeed x tau (the glide aims that far past its
    // stop), and rests on D when (1 + u) e^(-u) = RestSpeed x tau / A, u = t / tau (0.4617 s for 100 pt); one 60 Hz frame is added so the frame that lands it has run.
    private static double NotchRestMs(double distance)
    {
        var tau = ScrollPhysics.GlideStage; var residual = ScrollPhysics.RestSpeed * tau / (distance + ScrollPhysics.RestSpeed * tau);
        double low = 0, high = 60;
        for (var i = 0; i < 100; i++) { var mid = (low + high) / 2; if ((1 + mid) * Math.Exp(-mid) > residual) low = mid; else high = mid; }
        return high * tau * 1000 + 1000 / 60.0;
    }

    private static void PublishLimits(AppModel model, ProviderKind provider, int windows)
    {
        var limits = new PlanLimits { PlanName = "Max", Windows = new ValueList<PlanLimits.Window>(Enumerable.Range(0, windows).Select(i => new PlanLimits.Window { Id = "w" + i, Title = "Window " + i, Percent = 10 + i, ResetsAt = SwiftDate.Now.AddHours(i + 1) })) };
        var property = typeof(ProviderRegistry).GetProperty(nameof(ProviderRegistry.PlanLimits))!;
        property.SetValue(model.Providers, ((ValueMap<ProviderKind, PlanLimits>)property.GetValue(model.Providers)!).Mutating(map => map[provider] = limits));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-reveal-2")]
    public void A_revealed_section_stays_under_the_chrome_row_when_plan_limits_load_above_it_after_it_settled_until_the_person_scrolls() => WithProvidersPage((view, model, h, w) =>
    {
        SettingsNavigation.Shared.RequestedProvider = ProviderKind.Deepseek; SettingsNavigation.Shared.RequestedPage = SettingsPage.Providers;
        Drain(w); for (var i = 0; i < 8; i++) { Tick(h, 100); Drain(w); }
        var sections = Nodes<ProviderSettingsSection>(view);
        var section = sections.Single(s => s.Provider == ProviderKind.Deepseek);
        var above = sections.Where(s => s.Provider is ProviderKind.Claude or ProviderKind.Codex).ToArray();
        Assert.Equal(2, above.Length);
        Assert.All(above, s => Assert.True(s.TranslatePoint(default, section)!.Value.Y < 0, s.Provider + " is not above DeepSeek"));
        Assert.True(section.IsOpenAndSettled);
        var scroll = Nodes<ScrollViewer>(Field<Control>(view, "detail")).First();
        var inset = Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight + 16;
        double Top() => section.TranslatePoint(default, scroll)!.Value.Y;
        Assert.InRange(Top(), inset - 0.5, inset + 0.5);
        // a second after the fold settled, Claude's and Codex's plan limits arrive: their rows grow the cards above DeepSeek
        var heightAbove = above.Sum(s => s.Bounds.Height);
        Tick(h, 1000); Drain(w);
        PublishLimits(model, ProviderKind.Claude, 3); PublishLimits(model, ProviderKind.Codex, 2);
        Drain(w); Tick(h, 400); Drain(w);
        Assert.True(above.Sum(s => s.Bounds.Height) > heightAbove + 20, $"the cards above did not grow ({heightAbove} -> {above.Sum(s => s.Bounds.Height)})");
        Assert.InRange(Top(), inset - 0.5, inset + 0.5);
        // the person turns the wheel: the page is theirs, and a later change above moves the section with the page instead of the page being moved under them
        var before = scroll.Offset.Y;
        w.MouseWheel(At(section, w), new Vector(0, -1)); Drain(w); Tick(h, NotchRestMs(100)); Drain(w); // ED-NEW-softscroll-2: the notch (100 pt at the default 3 lines) glides and is read once it has rested, not part way along
        var theirs = scroll.Offset.Y;
        Assert.Equal(before + 100, theirs, 3); // the whole notch, 3 lines x 100/3 pt (ED-NEW-softscroll-2 (3)): the glide has rested on its stop
        Assert.True(Math.Abs(Top() - inset) > 0.5, "the wheel did not move the page");
        PublishLimits(model, ProviderKind.Claude, 6);
        Drain(w); Tick(h, 400); Drain(w);
        Assert.Equal(theirs, scroll.Offset.Y, 3);
    }, model => { Seed(model, ProviderKind.Claude, InstalledAndSignedIn); Seed(model, ProviderKind.Codex, InstalledAndSignedIn); }, height: 600);

    private static readonly ProviderStatus InstalledAndSignedIn = new() { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch"), ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) };
}
