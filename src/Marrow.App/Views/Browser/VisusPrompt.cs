using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.NthUi;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8a, the prompt for an app link (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.3 "Visus asks:
// '<site> wants to open <App name>' with the scheme shown, and Remember for this site only for mailto and tel", 8.4 the
// prompt rules: "Prompts never take focus when they appear. Enter does nothing in them; Esc refuses. F6 moves into the open
// prompt; Tab and Space work inside it. For 400 ms after a prompt appears, clicks and keys on its buttons are ignored", 2.4
// prompts are top-level popups (never under the native page), 13.3; the canvas's .pop .pt2 .row .pbtn .foot; ED-NEW-web-w8a-2).
// No Swift counterpart. The prompt hangs from the site chip with a title strip carrying nth's own icon and the source, so a
// page cannot draw a look-alike in its own area (the strip sits over the toolbar, outside any page).

namespace Marrow.App.Views.Browser;

/// <summary>DESIGN 8.3, 8.4: "example.com wants to open Mail (mailto:)" with Open and Cancel.</summary>
public sealed class VisusPrompt : Border
{
    /// <summary>How long after it appears the prompt ignores clicks and keys on its buttons (8.4).</summary>
    public static readonly TimeSpan Shield = TimeSpan.FromMilliseconds(400);

    /// <summary>The prompt's foot, word for word (the canvas's prompts).</summary>
    public const string Foot = "Enter does nothing here. Esc cancels.";

    private readonly long shownAt;
    private bool closed;

    public VisusPrompt(VisusExternalLaunch launch, string appName)
    {
        Launch = launch;
        shownAt = VisusHost.Time.GetTimestamp();
        Width = 310;
        Padding = new Thickness(14);
        Title = ExternalProtocols.PromptTitle(launch.Source, appName, launch.Scheme);
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                Icon(),
                Text("nth · Visus", 11.5, PaneTokens.Text3),
            },
        };
        var heading = Text(Title, 13.5, PaneTokens.Text1, wrap: true);
        heading.FontWeight = FontWeight.SemiBold;
        var detail = Text(Shorten(launch.Uri), 12.5, PaneTokens.Text2, wrap: true);
        detail.Margin = new Thickness(0, 2, 0, 0);
        var glyph = new PaneGlyph(PaneGlyphKind.External, 20) { Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        glyph.Foreground = PaneTokens.Text1.Brush(glyph);
        var words = new StackPanel { Orientation = Orientation.Vertical, Children = { heading, detail } };
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(glyph, Dock.Left);
        top.Children.Add(glyph);
        top.Children.Add(words);
        var body = new StackPanel { Orientation = Orientation.Vertical, Children = { strip, top } };
        if (launch.CanRemember)
        {
            Remember = new CheckBox { Content = Text("Remember for this site", 12.5, PaneTokens.Text1), Margin = new Thickness(30, 10, 0, 0) };
            ChatDialogControls.Checkbox(Remember);
            AutomationProperties.SetName(Remember, "Remember for this site");
            body.Children.Add(Remember);
        }
        OpenButton = NthUiPress.Button("Open " + appName, NthUiFaceStyle.Plain, null, () => Answer(true), 30);
        CancelButton = NthUiPress.Button("Cancel", NthUiFaceStyle.Plain, null, () => Answer(false), 30);
        OpenButton.IsDefault = false;
        CancelButton.IsCancel = false; // Esc is handled here (the window's own Esc must not run first)
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 12, 0, 0), Children = { OpenButton, CancelButton } });
        body.Children.Add(Text(Foot, 11.5, PaneTokens.Text3, wrap: true, margin: new Thickness(0, 10, 0, 0)));
        Child = body;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(this, Title);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Window);
        AutomationProperties.SetHelpText(this, launch.Uri);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointer, RoutingStrategies.Tunnel);
    }

    public VisusExternalLaunch Launch { get; }

    /// <summary>"example.com wants to open Mail (mailto:)".</summary>
    public string Title { get; }

    public Button OpenButton { get; }

    public Button CancelButton { get; }

    /// <summary>"Remember for this site" (mailto and tel from a browse or preview tab only).</summary>
    public CheckBox? Remember { get; }

    /// <summary>Within <see cref="Shield"/> of appearing: the buttons ignore clicks and keys.</summary>
    public bool IsShielded => VisusHost.Time.GetElapsedTime(shownAt) < Shield;

    /// <summary>The prompt was answered (or cancelled from outside) and should go.</summary>
    public event Action<VisusPrompt>? Done;

    /// <summary>F6 into the prompt (10.1): the first of its controls takes the keyboard. It never does so by itself.</summary>
    public bool FocusFirst()
    {
        if (closed) return false;
        Control first = Remember is not null ? Remember : OpenButton;
        return first.Focus(NavigationMethod.Tab);
    }

    /// <summary>Cancel from outside (the tab went, another tab was chosen, the pane closed).</summary>
    public void Cancel() => Answer(false);

    internal void Answer(bool open)
    {
        if (closed) return;
        if (IsShielded) return; // a click or key that landed as the prompt appeared does nothing
        closed = true;
        Launch.Answer(open, open && Remember?.IsChecked == true);
        Done?.Invoke(this);
    }

    /// <summary>Cancel that ignores the shield (the prompt is going away for another reason).</summary>
    internal void Dismiss()
    {
        if (closed) return;
        closed = true;
        Launch.Answer(false);
        Done?.Invoke(this);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true; // Enter does nothing here (8.4)
                return;
            case Key.Escape:
                e.Handled = true;
                Dismiss();
                return;
            case Key.Space when IsShielded:
                e.Handled = true;
                return;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && IsShielded) e.Handled = true; // a Button clicks on Space's release
    }

    private void OnPointer(object? sender, PointerEventArgs e)
    {
        if (IsShielded) e.Handled = true;
    }

    private static Control Icon()
    {
        var icon = AppIconImage.Make(14);
        icon.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    private static string Shorten(string uri) => uri.Length <= 120 ? uri : uri[..117] + "…";

    private static TextBlock Text(string text, double size, SwiftColor color, bool wrap = false, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = margin ?? default,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.Foreground = color.Brush(block);
        block.ActualThemeVariantChanged += (_, _) => block.Foreground = color.Brush(block);
        return block;
    }
}

/// <summary>
/// The open Visus prompts of a window, newest last: F6 goes into the newest (PaneWorkspace.PromptFirst, 10.1). W8b's
/// permission and download prompts join the same list.
/// </summary>
public static class VisusPrompts
{
    private static readonly List<(TopLevel Window, Func<bool> Focus)> Open = [];

    /// <summary>A prompt opened in <paramref name="window"/>; the result removes it.</summary>
    public static IDisposable Add(TopLevel window, Func<bool> focus)
    {
        var entry = (window, focus);
        Open.Add(entry);
        return new Removal(() => Open.Remove(entry));
    }

    /// <summary>Whether a prompt is open in <paramref name="window"/>.</summary>
    public static bool AnyIn(TopLevel? window) => window is not null && Open.Any(entry => ReferenceEquals(entry.Window, window));

    /// <summary>F6: the newest prompt of <paramref name="window"/> takes the keyboard; false when it has none.</summary>
    public static bool FocusNewest(TopLevel? window)
    {
        for (var i = Open.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Open[i].Window, window) && Open[i].Focus()) return true;
        return false;
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        private Action? pending = remove;

        public void Dispose()
        {
            pending?.Invoke();
            pending = null;
        }
    }
}

/// <summary>A quiet line under the address field ("Visus does not run script addresses."), gone after a few seconds.</summary>
public sealed class VisusNotice : Border
{
    /// <summary>How long a notice stays.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3.2);

    private readonly TextBlock text = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private DispatcherTimer? timer;

    public VisusNotice()
    {
        Padding = new Thickness(12, 8);
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new PaneGlyph(PaneGlyphKind.Warn, 13) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) }, text },
        };
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    public string Text => text.Text ?? "";

    /// <summary>The notice should go (its time ran out).</summary>
    public event Action? Expired;

    internal void Show(string message)
    {
        text.Text = message;
        AutomationProperties.SetName(this, message);
        timer?.Stop();
        if (!Dispatcher.UIThread.CheckAccess()) return;
        timer = new DispatcherTimer(Duration, DispatcherPriority.Background, (_, _) =>
        {
            timer?.Stop();
            timer = null;
            Expired?.Invoke();
        });
        timer.Start();
    }

    internal void Stop()
    {
        timer?.Stop();
        timer = null;
    }

    private void ApplyLook()
    {
        text.Foreground = PaneTokens.Text1.Brush(this);
        if (Child is StackPanel { Children: [PaneGlyph glyph, ..] }) glyph.Foreground = PaneTokens.Text2.Brush(this);
    }
}
