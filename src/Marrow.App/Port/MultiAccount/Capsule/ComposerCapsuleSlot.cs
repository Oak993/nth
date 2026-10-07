// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). The composer's place for the account capsule: nothing (and no room) until the thread has a second account of its service or a pill relay, then THE capsule in the style Customization picked.
// A new style crossfades in place. The slot shows and passes the click on; it opens nothing (the account popover is the next unit's) and starts nothing in the engine.
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class ComposerCapsuleSlot : Decorator
{
    private readonly ComposerCapsuleModel model;
    private readonly CapsuleStyleSetting styleSetting;
    private readonly Func<CapsuleSubject?> subject;
    private bool handshake;
    private Guid? standby;
    private bool inSync;
    private AccountCapsule? capsule;
    private AnimatedScalar? fade;

    public ComposerCapsuleSlot(ComposerCapsuleModel model, CapsuleStyleSetting style, Func<CapsuleSubject?> subject, Marrow.Core.Accounts.Pills.PillStore? pills = null)
    {
        this.model = model;
        styleSetting = style;
        this.subject = subject;
        IsVisible = false;
        AttachedToVisualTree += (_, _) =>
        {
            styleSetting.Changed += OnChanged;
            if (pills is not null) pills.Changed += OnPillsChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            styleSetting.Changed -= OnChanged;
            if (pills is not null) pills.Changed -= OnPillsChanged;
        };
    }

    /// <summary>Raised when the capsule is pressed. Whoever holds the slot opens what it opens.</summary>
    public event Action? Clicked;

    public bool Shown => capsule is not null;

    public AccountCapsule? Capsule => capsule;

    /// <summary>The Handshake unit's hook: a Handshake runs, and the account on stand-by for it (the capsule's lights then look ahead to it).</summary>
    public void SetHandshake(bool running, Guid? standbyAccount)
    {
        handshake = running;
        standby = standbyAccount;
        Refresh();
    }

    internal void SetInSync(bool ready) { inSync = ready; Refresh(); }

    private void Tick(AccountCapsule target)
    {
        var mark = target.Children.OfType<TextBlock>().FirstOrDefault(text => text.Tag is "account-in-sync");
        if (mark is null)
        {
            mark = LibraryUi.Text("✓", 9, secondary: true);
            mark.Tag = "account-in-sync"; mark.IsHitTestVisible = false;
            mark.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            mark.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            mark.Named("In sync", "account-in-sync"); target.Children.Add(mark);
        }
        mark.IsVisible = inSync;
    }

    private static void OnMain(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private void OnChanged() => OnMain(Refresh);

    private void OnPillsChanged(object? sender, Marrow.Core.Accounts.Pills.PillStoreChange change) => OnMain(Refresh);

    /// <summary>Reads the thread again and shows what it is now.</summary>
    public void Refresh()
    {
        var wanted = subject() is { } s ? model.For(s with { Handshake = handshake, StandbyAccountId = standby ?? s.StandbyAccountId }) : null;
        if (wanted is null)
        {
            if (capsule is not null) { Child = null; capsule = null; }
            IsVisible = false;
            return;
        }
        var style = wanted.Pill is null ? "plain" : styleSetting.Value;
        if (capsule is null || capsule.Style != style)
        {
            var made = new AccountCapsule(style);
            made.Clicked += () => Clicked?.Invoke();
            var replacing = capsule is not null;
            capsule = made;
            Child = made;
            IsVisible = true;
            made.Apply(wanted, animate: false);
            Tick(made);
            if (replacing) FadeIn(made);
            return;
        }
        IsVisible = true;
        capsule.Apply(wanted);
        Tick(capsule);
    }

    // a new style crossfades in place (the page's swapIn: fadeIn 220 ms under Reduce Motion, else it grows in from .86)
    private void FadeIn(AccountCapsule made)
    {
        made.Opacity = 0;
        fade = new AnimatedScalar(made, 0, v => made.Opacity = v);
        var still = CapsuleMotion.Reduce(this);
        fade.Set(1, !FrameClock.IsBound ? null : Marrow.UI.Port.Animation.EaseOut(duration: still ? .22 : .3));
    }
}
