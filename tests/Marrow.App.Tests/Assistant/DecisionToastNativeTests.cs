using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using Windows.UI.Notifications;
using static Marrow.App.Tests.Fixtures.HomeSamples;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 16.1 "Native gate",
// "toasts: shown for a needs-you decision, suppressed while the home is on screen, Review opens the card". (1) A needs-you card makes a real Windows toast
// under nth's dev identity, with its three buttons, and answering the card in the app takes it out of the action center. (2) With a real window in front
// no toast is made for a card; with it minimised one is. (3) A press carrying a decision's userInfo travels the real named pipe and decodes to the card.
// (4) Review brings the card in front with real focus on the card and never on a button, and a real Enter on it sends nothing.
// (5, ED-NEW-dextra-as9-9) A "Notify me" duty's finding makes a real toast with the one button Open, signed "Dextra", and none while the home is in front.
// The owner also looks once at a real toast: Armilla's needs-you picture, "Store needs you", the card's title, "Dextra", and Allow once, Deny, Open;
// and once in the installed app: a toast's Review brings nth forward on the Decisions tab with the card in front.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-4")]
public sealed class DecisionToastNativeTests(WindowManagerNativeSession native)
{
    private const byte VkReturn = 0x0D;
    private const uint KeyUp = 0x0002;

    private static readonly ToastBuildIdentity Dev = ToastBuildIdentity.ForBuild(debug: true, dev: true);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    private static IReadOnlyList<ToastNotification> History() => ToastNotificationManager.History.GetHistory(Dev.AppId);

    private static bool Up(string tag) => History().Any(toast => toast.Tag == tag);

    private static (WindowsToastCenter Center, DecisionToastLogo Logo, string Folder) Shell()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-as9-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var logo = new DecisionToastLogo(new WindowsToastIconFiles(), folder, () => true);
        var platform = new WindowsToastPlatform();
        WindowsToastRegistration.Register(platform, Dev, Environment.ProcessPath ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no process path."), logo.Path() ?? "");
        return (new WindowsToastCenter(platform, Dev), logo, folder);
    }

    private static DecisionToasts Toasts(DecisionQueue queue, WindowsToastCenter center, DecisionToastLogo logo, Func<bool> homeOnScreen) =>
        new(queue, new WindowsNotificationChannel(logo: logo.Path, center: () => center), new DecisionToastOptions
        {
            NameOf = _ => "Store",
            HomeOnScreen = homeOnScreen,
            OnMain = work => work(),
        });

    private (Avalonia.Controls.Window Window, TControl Content) Host<TControl>(Func<TControl> content, double width = 640, double height = 480) where TControl : Control => native.OnUi(() =>
    {
        var made = new Avalonia.Controls.Window { Width = width, Height = height, ShowInTaskbar = false };
        native.Register(made);
        made.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        var built = content();
        made.Content = built;
        made.Show();
        made.Activate();
        return (made, built);
    });

    private void Front(Avalonia.Controls.Window window) => native.OnUi(() =>
        WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: the test window has no HWND."), native.ThreadId));

    [Fact]
    public void A_needs_you_card_shows_a_real_toast_with_its_three_buttons_and_answering_it_in_the_app_takes_it_out_of_the_action_center()
    {
        native.Run(_ =>
        {
            var (center, logo, folder) = Shell();
            try
            {
                var queue = new DecisionQueue();
                using var toasts = Toasts(queue, center, logo, () => false);
                var card = queue.Raise(DecisionToastRig.Approval("Run npm test?"), (_, _) => { });
                var tag = DecisionToastRequests.Identifier(card.Id);
                N.Until(() => Up(tag), "the toast never reached the action center", 15000);
                var shown = History().First(toast => toast.Tag == tag);
                var xml = XDocument.Parse(shown.Content.GetXml());
                Assert.Equal(["Allow once", "Deny", "Open"], xml.Descendants("action").Select(a => (string?)a.Attribute("content")));
                Assert.Contains(xml.Descendants("text"), t => t.Value == "Store needs you");

                Assert.Null(queue.Answer(card.Id, DecisionAnswer.AllowOnce));
                N.Until(() => !Up(tag), "the answered card's toast stayed in the action center", 15000);
            }
            finally { Directory.Delete(folder, recursive: true); }
        }, nameof(A_needs_you_card_shows_a_real_toast_with_its_three_buttons_and_answering_it_in_the_app_takes_it_out_of_the_action_center), typeof(DecisionToastNativeTests));
    }

    [Fact]
    public void No_real_toast_is_made_while_a_real_window_is_in_front_and_one_is_made_when_it_is_minimised()
    {
        native.Run(_ =>
        {
            var (center, logo, folder) = Shell();
            try
            {
                var (window, _) = Host(() => new Panel());
                N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
                Front(window);
                var queue = new DecisionQueue();
                // the window being active stands for "nth is in front with the home in the main area"
                using var toasts = Toasts(queue, center, logo, () => native.OnUi(() => window.IsActive));

                var hidden = queue.Raise(DecisionToastRig.Approval("Seen on the home"), (_, _) => { });
                Thread.Sleep(DecisionToasts.Together + TimeSpan.FromSeconds(3));
                Assert.False(Up(DecisionToastRequests.Identifier(hidden.Id)), "a toast was made with the window in front");

                native.OnUi(() => window.WindowState = WindowState.Minimized);
                N.Until(() => !native.OnUi(() => window.IsActive), "the window stayed active");
                var shown = queue.Raise(DecisionToastRig.Approval("Raised while away"), (_, _) => { });
                N.Until(() => Up(DecisionToastRequests.Identifier(shown.Id)), "no toast was made with the window away", 15000);
                queue.Answer(shown.Id, DecisionAnswer.Deny());
            }
            finally { Directory.Delete(folder, recursive: true); }
        }, nameof(No_real_toast_is_made_while_a_real_window_is_in_front_and_one_is_made_when_it_is_minimised), typeof(DecisionToastNativeTests));
    }

    [Fact]
    public void A_notify_me_duty_finding_shows_a_real_toast_with_one_open_button_and_none_while_a_real_window_is_in_front()
    {
        native.Run(_ =>
        {
            var (center, logo, folder) = Shell();
            try
            {
                var (window, _) = Host(() => new Panel());
                N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
                Front(window);
                var front = true;
                using var toasts = new DecisionToasts(new DecisionQueue(), new WindowsNotificationChannel(logo: logo.Path, newsLogo: logo.NewsPath, center: () => center), new DecisionToastOptions
                {
                    NameOf = _ => "Store",
                    HomeOnScreen = () => Volatile.Read(ref front) && native.OnUi(() => window.IsActive),
                    OnMain = work => work(),
                });

                var hidden = new DutyFinding(Guid.NewGuid(), "Espresso price", "The price fell to $640.");
                toasts.NotifyDuty(hidden);
                Thread.Sleep(DecisionToasts.Together + TimeSpan.FromSeconds(3));
                Assert.False(Up(DecisionToastRequests.DutyIdentifier(hidden.Mission)), "a duty toast was made with the window in front");

                Volatile.Write(ref front, false);
                var shown = new DutyFinding(Guid.NewGuid(), "Espresso price", "The price fell to $620.");
                toasts.NotifyDuty(shown);
                var tag = DecisionToastRequests.DutyIdentifier(shown.Mission);
                N.Until(() => Up(tag), "no duty toast was made with the window away", 15000);
                var xml = XDocument.Parse(History().First(toast => toast.Tag == tag).Content.GetXml());
                Assert.Equal(["Open"], xml.Descendants("action").Select(a => (string?)a.Attribute("content")));
                Assert.Contains(xml.Descendants("text"), t => t.Value == "Espresso price");
                Assert.Contains(xml.Descendants("text"), t => t.Value == "Dextra");
            }
            finally { Directory.Delete(folder, recursive: true); }
        }, nameof(A_notify_me_duty_finding_shows_a_real_toast_with_one_open_button_and_none_while_a_real_window_is_in_front), typeof(DecisionToastNativeTests));
    }

    [Fact]
    public void A_press_on_a_decision_toast_travels_the_real_pipe_and_decodes_to_the_card()
    {
        native.Run(_ =>
        {
            var transport = new WindowsToastLaunchTransport();
            var name = "Marrow.Toast.Native." + Guid.NewGuid().ToString("N");
            string? received = null;
            using var listener = transport.Listen(name, payload => Volatile.Write(ref received, payload));
            var card = Guid.NewGuid();
            var uri = ToastActivation.Encode(Dev, new Dictionary<string, object>
            {
                [DecisionToastKeys.Decision] = card.ToString("D"),
                [DecisionToastKeys.Action] = "review",
                [DecisionToastKeys.Nonce] = "n",
            });
            Assert.True(ToastSecondLaunch.Forward(transport, name, Dev, [uri]));
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref received) is not null, TimeSpan.FromSeconds(10)), "the press never reached the running app");
            var decoded = ToastActivation.Decode(Dev, Volatile.Read(ref received)!);
            Assert.NotNull(decoded);
            Assert.Equal(card.ToString("D"), decoded.UserInfo[DecisionToastKeys.Decision]);
            Assert.Equal("review", decoded.UserInfo[DecisionToastKeys.Action]);
        }, nameof(A_press_on_a_decision_toast_travels_the_real_pipe_and_decodes_to_the_card), typeof(DecisionToastNativeTests));
    }

    [Fact]
    public void Review_brings_an_irreversible_card_in_front_with_real_focus_on_the_card_and_a_real_Enter_on_it_sends_nothing()
    {
        native.Run(_ =>
        {
            FakeDecisions decisions = null!;
            var send = Irreversible("Send the reply to Sam");
            var (window, list) = Host(() =>
            {
                decisions = new FakeDecisions();
                decisions.Add(Reversible("Sign in to the shop's dashboard"));
                decisions.Add(send);
                return new DecisionList(decisions, new DecisionContext(decisions.Answer));
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);

            Assert.True(native.OnUi(() => list.Reveal(send.Id)));
            N.Until(() => native.OnUi(() => list.Cards.Single(c => c.Item.Id == send.Id) is { } card && ReferenceEquals(window.FocusManager?.GetFocusedElement(), card.Target)),
                "the revealed card never took the focus");
            Assert.False(native.OnUi(() => list.Cards.Single(c => c.Item.Id == send.Id).AnswerButtons.Any(b => b.IsFocused)));

            keybd_event(VkReturn, 0, 0, 0);
            keybd_event(VkReturn, 0, KeyUp, 0);
            Thread.Sleep(300);
            Assert.Empty(native.OnUi(() => decisions.Answers.ToList()));
        }, nameof(Review_brings_an_irreversible_card_in_front_with_real_focus_on_the_card_and_a_real_Enter_on_it_sends_nothing), typeof(DecisionToastNativeTests));
    }
}
