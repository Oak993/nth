using System.Xml.Linq;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using Windows.UI.Notifications;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 16.1 "Native gate",
// "toasts: shown for a needs-you decision, suppressed while the home is on screen, Review opens the card". (1) A needs-you card makes a real Windows toast
// under nth's dev identity, with its three buttons, and answering the card in the app takes it out of the action center. (2) With a real window in front
// no toast is made for a card; with it minimised one is. (3) A press carrying a decision's userInfo travels the real named pipe and decodes to the card.
// The owner also looks once at a real toast: Armilla's needs-you picture, "Store needs you", the card's title, "Dextra", and Allow once, Deny, Open.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-4")]
public sealed class DecisionToastNativeTests(WindowManagerNativeSession native)
{
    private static readonly ToastBuildIdentity Dev = ToastBuildIdentity.ForBuild(debug: true, dev: true);

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
        });
    }

    [Fact]
    public void No_real_toast_is_made_while_a_real_window_is_in_front_and_one_is_made_when_it_is_minimised()
    {
        native.Run(_ =>
        {
            var (center, logo, folder) = Shell();
            try
            {
                var window = native.OnUi(() =>
                {
                    var made = new Avalonia.Controls.Window { Width = 600, Height = 400, ShowInTaskbar = false };
                    native.Register(made);
                    made.Show();
                    made.Activate();
                    return made;
                });
                N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
                native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no HWND."), native.ThreadId));
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
        });
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
        });
    }
}
