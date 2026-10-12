using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W6, the side chat's composer (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.9 "Composer placeholder: Ask about
// this thread…", "a model chip in its composer can pick another"; the approved canvas's .scomp: a glass pill, 12.5 DIP text, a 30 DIP round
// send button; ED-NEW-web-w6-1). No Swift counterpart. The main composer is a heavy control with attachments, slash commands, voice and
// a queue; a side chat is a light conversation, so its composer is a text input, the model chip the main composer uses (the compact one)
// and Send, which becomes Stop while the side chat answers. Return sends, Shift+Return writes a new line.

namespace Marrow.App.Views.Panes;

/// <summary>The pill at the foot of the Side chat pane.</summary>
public sealed class SideChatComposer : Border
{
    // The pill is 42 DIP tall at rest, the canvas's .scomp; the input grows to six lines, then scrolls.
    private const double RestHeight = 42;
    private const double SendSize = 30;
    private const double FieldFontSize = 12.5;
    private const double MaxFieldHeight = 120;

    // A scroll viewer with no template draws nothing (the app carries no theme package): the input's own scrolling, no bars.
    private static readonly FuncControlTemplate<ScrollViewer> FieldScroll = new((viewer, scope) =>
        new ScrollContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));

    private readonly TextBox input;
    private readonly Border chipHost = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 6), Height = SendSize };
    private readonly Button send;
    private readonly Border sendFace;
    private readonly PaneGlyph sendGlyph = new(PaneGlyphKind.Send, 13);
    private bool running;
    private bool settingText;

    public SideChatComposer()
    {
        input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = SideChatStrings.Placeholder,
            FontSize = FieldFontSize,
            BorderThickness = default,
            Background = Brushes.Transparent,
            Padding = default,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ChatDialogControls.TextEditor(input);
        input.Foreground = PaneTokens.Text1.Brush(input);
        AutomationProperties.SetName(input, SideChatStrings.Placeholder);
        input.AddHandler(InputElement.KeyDownEvent, OnFieldKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        input.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || settingText) return;
            Refresh();
            TextEdited?.Invoke(input.Text ?? "");
        };

        var scroll = new ScrollViewer
        {
            Template = FieldScroll,
            Content = input,
            MaxHeight = MaxFieldHeight,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12.5, 8, 12.5),
        };
        // A press anywhere on the pill, empty or not, puts the caret in the input.
        PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && !IsInside(source, send) && !IsInside(source, chipHost)) input.Focus(NavigationMethod.Pointer);
        };

        sendFace = new Border
        {
            Width = SendSize,
            Height = SendSize,
            CornerRadius = new CornerRadius(SendSize / 2),
            Child = sendGlyph,
        };
        sendGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        sendGlyph.VerticalAlignment = VerticalAlignment.Center;
        send = PlainControls.Button(sendFace, OnSendOrStop).KeyboardFocus(SendSize / 2);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Margin = new Thickness(0, 0, 0, 6);
        send.Focusable = true;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), MinHeight = RestHeight, Margin = new Thickness(14, 0, 6, 0) };
        Grid.SetColumn(scroll, 0);
        Grid.SetColumn(chipHost, 1);
        Grid.SetColumn(send, 2);
        row.Children.Add(scroll);
        row.Children.Add(chipHost);
        row.Children.Add(send);
        Child = new ComposerSurface().Body(row);
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The person sent <see cref="Text"/> (Return, or the button).</summary>
    public event Action<string>? Submitted;

    /// <summary>The person asked to stop the answer (the button while the side chat answers).</summary>
    public event Action? StopRequested;

    /// <summary>The text changed by typing; the pane keeps it as the side chat's draft.</summary>
    public event Action<string>? TextEdited;

    public TextBox Field => input;

    public Button SendButton => send;

    public PaneGlyphKind SendGlyphKind => sendGlyph.Kind;

    /// <summary>The model chip's place; the pane puts the side chat's chip here.</summary>
    public Border ChipHost => chipHost;

    public string Text
    {
        get => input.Text ?? "";
        set
        {
            if ((input.Text ?? "") == value) return;
            settingText = true;
            try { input.Text = value; }
            finally { settingText = false; }
            Refresh();
        }
    }

    /// <summary>The side chat is answering: Send becomes Stop.</summary>
    public bool IsRunning
    {
        get => running;
        set
        {
            if (running == value) return;
            running = value;
            Refresh();
        }
    }

    /// <summary>Whether the button would send now (there is text and no answer is running).</summary>
    public bool CanSend => !running && Text.Trim().Length != 0;

    public void FocusField() => input.Focus(NavigationMethod.Unspecified);

    private static bool IsInside(Visual source, Visual? ancestor)
    {
        for (Visual? at = source; at is not null; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private void OnSendOrStop()
    {
        if (running) StopRequested?.Invoke();
        else Submit();
    }

    private void Submit()
    {
        var text = Text.Trim();
        if (text.Length == 0 || running) return;
        Submitted?.Invoke(text);
    }

    // Return sends; Shift+Return (a new line) and Return while an input method is composing are the input's own.
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Submit();
    }

    private void Refresh()
    {
        sendGlyph.Kind = running ? PaneGlyphKind.Stop : PaneGlyphKind.Send;
        var enabled = running || Text.Trim().Length != 0;
        sendFace.Background = SideChatTokens.SendFill.Brush(sendFace);
        sendGlyph.Foreground = PaneTokens.Text1.Brush(sendGlyph);
        sendFace.Opacity = enabled ? 1 : 0.5;
        var name = running ? SideChatStrings.Stop : SideChatStrings.Send;
        AutomationProperties.SetName(send, name);
        ToolTip.SetTip(send, name);
    }
}
