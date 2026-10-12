// PORT-ONLY: Tactus pill v2, the approval card's body (OD-0144, OD-0145, ED-NEW-pcctl-20, ED-NEW-pcctl-26; DESIGN.md 4.1, 10.4
// "Approval body", 2.12 the helper's egress line; canvas `bodyOf` 'ap', `apSum`). The app's tile, name and path (middle-
// truncated), the risky-category line, the helper egress line when the fast helper is on, the See and Use chips (they start
// as the model asked; Use implies See) and the line that says what they mean, then "Don't allow" and "Allow for this
// chat", and a second row with "Reply instead" and "Always allow" (heads never see it). "Always allow" is laid out at its
// armed width from the start and its capsule opens into free space when it arms, so nothing in the row moves. "Reply
// instead" opens the reply field: its text goes to the model as the reason, with Don't allow.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class ApprovalCardBody : CardBody
{
    public const string DenyId = "deny";
    public const string ChatId = "chat";
    public const string AlwaysId = "always";
    public const string ReplyId = "reply";
    public const string SeeId = "see";
    public const string UseId = "use";

    public const string RiskyLine = "Clicks act as you in this app. Sites you are signed in to see them as you.";
    public const string NothingLine = "Turn on See or Use to allow anything.";
    public const string ReplyPlaceholder = "Tell the model why, or what to do instead";
    public const string AlwaysLabel = "Always allow";

    private static readonly Color RestFill = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
    private static readonly Color ArmedFill = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    private static readonly Color ArmedRing = Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF);

    private readonly PillApprovalBody model;
    private readonly TextBlock summary;
    private readonly CardButton see;
    private readonly CardButton use;
    private readonly CardButton deny;
    private readonly CardButton chat;
    private readonly CardButton? always;
    private readonly CardButton reply;
    private readonly PillCapsule? capsule;
    private readonly Canvas? alwaysLabels;
    private readonly TextBlock? restLabel;
    private readonly TextBlock? armedLabel;
    private readonly MiniComposer composer;
    private double alwaysInset;

    public ApprovalCardBody(PillApprovalBody model)
        : base(model)
    {
        this.model = model;
        // the app: its tile, name and path (canvas `.kap`)
        var name = CardLook.Line(model.App, 15, CardLook.Text1, FontWeight.SemiBold, wrap: TextWrapping.NoWrap);
        var path = new TextBlock { Text = MiddleTruncate(model.Path, 52), FontFamily = CardLook.Mono, FontSize = 11.5, Foreground = CardLook.Path, Margin = new Thickness(0, 1, 0, 0), MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetName(path, model.Path);
        var app = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Tile(model.App, 34), new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, path } } } };
        Above.Children.Add(app);
        if (model.Risky) Above.Children.Add(Risk(RiskyLine));
        if (model.Helper is { Length: > 0 } helper) Above.Children.Add(CardLook.Line($"Also seen by {helper} (fast helper)", 13, CardLook.Text2, lineHeight: 19).With(m => m.Margin = new Thickness(0, 9, 0, 0)));
        // See and Use (canvas `.gch`): Use implies See
        see = Add(SeeId, CardControlKind.Free, Chip(CardLook.Eye, "See"));
        use = Add(UseId, CardControlKind.Free, Chip(CardLook.Click, "Use"));
        see.IsOn = true;
        use.IsOn = model.UseAsked;
        Above.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 12, 0, 0), Children = { see, use } });
        summary = CardLook.Line("", 13, CardLook.Text2, lineHeight: 19);
        summary.Margin = new Thickness(0, 9, 0, 0);
        Above.Children.Add(summary);
        // the answers
        deny = Add(DenyId, CardControlKind.Answer, Labelled(CardButtonLook.Plain, "Don't allow"));
        chat = Add(ChatId, CardControlKind.Answer, Labelled(CardButtonLook.Plain, "Allow for this chat"));
        Below.Children.Add(Row(deny, chat));
        reply = Add(ReplyId, CardControlKind.Free, Labelled(CardButtonLook.Link, "Reply instead"));
        reply.VerticalAlignment = VerticalAlignment.Center;
        if (model.AllowAlways)
        {
            (always, capsule, alwaysLabels, restLabel, armedLabel) = Always();
            Add(AlwaysId, CardControlKind.Always, always);
        }
        var second = Row(reply, always, 8);
        second.MinHeight = 34;
        Below.Children.Add(second);
        composer = new MiniComposer(ReplyPlaceholder) { IsVisible = false };
        Add(MiniComposer.SendId, CardControlKind.Free, composer.Send);
        composer.Grew += Relayout;
        Composer = composer;
        Below.Children.Add(composer);
        Footer = new CardFooter([PillHintPart.Key("Esc"), new(" Later"), PillHintPart.Dot, new(CardShield.EnterHint)]);
        Below.Children.Add(Footer);
        Summarize();
    }

    public new PillApprovalBody Model => model;

    /// <summary>The Use chip is on (Use implies See).</summary>
    public bool Use => use.IsOn && see.IsOn;

    public bool IsArmed { get; private set; }

    /// <summary>The line under the chips (tests).</summary>
    public string Summary => summary.Text ?? "";

    internal CardButton SeeChip => see;

    internal CardButton UseChip => use;

    internal CardButton? AlwaysButton => always;

    internal PillCapsule? AlwaysCapsule => capsule;

    internal TextBlock? ArmedLabel => armedLabel;

    internal TextBlock? RestLabel => restLabel;

    public override PillCardAnswer? Act(string id)
    {
        switch (id)
        {
            case SeeId:
                see.IsOn = !see.IsOn;
                if (!see.IsOn) use.IsOn = false;
                Summarize();
                return null;
            case UseId:
                use.IsOn = !use.IsOn;
                if (use.IsOn) see.IsOn = true;
                Summarize();
                return null;
            case ReplyId:
                Reply();
                return null;
            case DenyId:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Deny, Use);
            case ChatId when see.IsOn:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.AllowChat, Use);
            case AlwaysId when see.IsOn && model.AllowAlways:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.AllowAlways, Use);
            case MiniComposer.SendId when composer.HasText:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Deny, Use, composer.Text.Trim());
            default:
                return null;
        }
    }

    public override void Opened()
    {
        if (composer.IsVisible) composer.Field.Focus();
    }

    /// <summary>"Always allow" armed ("Click again to always allow"): its capsule opens leftward into free space and the label slides.</summary>
    public void Arm(bool on, double now, IPillAnimator animator)
    {
        if (always is null || capsule is null || restLabel is null || armedLabel is null || IsArmed == on) return;
        IsArmed = on;
        capsule.To(on ? 0 : alwaysInset, on ? ArmedFill : RestFill, on ? ArmedRing : null, now, instant: false);
        always.HitInset = on ? 0 : alwaysInset;
        AutomationProperties.SetName(always, on ? CardShield.ArmedLabel : AlwaysLabel);
        // the labels swap by a slide under the button's clip (no fade): the leaving one up, the coming one from below
        var (leaving, coming) = on ? (restLabel, armedLabel) : (armedLabel, restLabel);
        var h = always.Bounds.Height > 0 ? always.Bounds.Height : 34;
        coming.IsVisible = true;
        var end = now + ControlPill.TextSwapSeconds;
        animator.Translate(coming, PillTrack.Of(now, end, _ => 0), PillTrack.Of(now, end, t => h * (1 - Swept(now, t))));
        animator.Translate(leaving, PillTrack.Of(now, end, _ => 0), PillTrack.Of(now, end, t => -h * Swept(now, t)));
    }

    private static double Swept(double now, double t) => PillEase.CubicBezier(0.32, 0.72, 0, 1, Math.Clamp((t - now) / ControlPill.TextSwapSeconds, 0, 1));

    private void Reply()
    {
        if (composer.IsVisible) return;
        composer.IsVisible = true;
        reply.IsVisible = false;
        Relayout();
    }

    private void Summarize()
    {
        var who = model.Who;
        summary.Text = !see.IsOn ? NothingLine
            : use.IsOn ? $"{who} will be able to see and use {model.App}."
            : $"{who} will only see {model.App}. It can't click or type.";
        chat.IsEnabled = see.IsOn;
        if (always is not null) always.IsEnabled = see.IsOn;
    }

    private (CardButton, PillCapsule, Canvas, TextBlock, TextBlock) Always()
    {
        TextBlock Label(string text) => new() { Text = text, FontFamily = CardLook.Text, FontSize = 13.5, FontWeight = FontWeight.Medium, Foreground = CardLook.Text1, LineHeight = 34 };
        var rest = Label(AlwaysLabel);
        var armed = Label(CardShield.ArmedLabel);
        rest.Measure(Size.Infinity);
        armed.Measure(Size.Infinity);
        var restWidth = rest.DesiredSize.Width;
        var armedWidth = armed.DesiredSize.Width;
        var width = Math.Ceiling(armedWidth + 24);
        alwaysInset = Math.Max(0, armedWidth - restWidth);
        var labels = new Canvas { Width = width, Height = 34, ClipToBounds = true, IsHitTestVisible = false };
        Canvas.SetRight(rest, 12);
        Canvas.SetRight(armed, 12);
        armed.IsVisible = false;
        labels.Children.Add(rest);
        labels.Children.Add(armed);
        var fill = new PillCapsule { Width = width, Height = 34 };
        fill.To(alwaysInset, RestFill, null, 0, instant: true);
        var button = new CardButton(CardButtonLook.Always, new Panel { Width = width, Height = 34, Children = { fill, labels } })
        {
            Width = width,
            Padding = new Thickness(0),
            HitInset = alwaysInset,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, AlwaysLabel);
        AutomationProperties.SetAccessibilityView(labels, AccessibilityView.Raw);
        return (button, fill, labels, rest, armed);
    }

    private static CardButton Chip(string icon, string text)
    {
        var check = CardLook.Icon(CardLook.Check, 13, Brushes.White);
        var glyph = CardLook.Icon(icon, 13, CardLook.Text3);
        var chip = new CardButton(CardButtonLook.Toggle, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5,
            Children = { check, glyph, new TextBlock { Text = text, FontFamily = CardLook.Text, VerticalAlignment = VerticalAlignment.Center } },
        });
        // canvas: the check shows while on, the icon (at 45 %) while off
        glyph.Opacity = 0.45;
        chip.Toggled += () =>
        {
            check.IsVisible = chip.IsOn;
            glyph.IsVisible = !chip.IsOn;
        };
        AutomationProperties.SetName(chip, text);
        return chip;
    }

    internal static Control Tile(string app, double size)
    {
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), BoxShadow = CardLook.TileRim,
            Background = AppTile.Brush(app),
            Child = new TextBlock { Text = app.Length > 0 ? app[..1].ToUpperInvariant() : "?", FontFamily = CardLook.Text, FontSize = size * 0.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        AutomationProperties.SetAccessibilityView(tile, AccessibilityView.Raw);
        return tile;
    }

    internal static Control Risk(string text)
    {
        var risk = new Border
        {
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(11, 9), CornerRadius = new CornerRadius(12), Background = CardLook.Ctl2, BoxShadow = CardLook.RiskBar,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 9,
                Children = { CardLook.Icon(CardLook.Shield, 15, CardLook.Text1).With(i => i.VerticalAlignment = VerticalAlignment.Top), CardLook.Line(text, 13, CardLook.Text1, lineHeight: 18.85).With(t => t.MaxWidth = 360) },
            },
        };
        return risk;
    }

    /// <summary>Shortens a path in its middle so the drive and the file name both stay ("C:\Program Files\…\chrome.exe").</summary>
    public static string MiddleTruncate(string path, int max)
    {
        if (path.Length <= max || max < 5) return path;
        var keep = max - 1;
        var head = keep / 2;
        var tail = keep - head;
        return path[..head] + "…" + path[^tail..];
    }
}

/// <summary>A neutral app tile colour from the app's name (the real icon is the App's to supply; this is its stand-in).</summary>
internal static class AppTile
{
    private static readonly Color[] Tiles =
    [
        Color.FromRgb(0x1D, 0x6F, 0x42), Color.FromRgb(0x0F, 0x6C, 0xBD), Color.FromRgb(0x5B, 0x5F, 0x6A),
        Color.FromRgb(0x8A, 0x4B, 0x2E), Color.FromRgb(0x5C, 0x3D, 0x8F), Color.FromRgb(0x2F, 0x6B, 0x7A),
    ];

    // A stable pick (string hash codes change per process): the sum of the name's lower-cased characters.
    public static IBrush Brush(string app) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Tiles[app.ToLowerInvariant().Sum(c => c) % Tiles.Length]);
}

internal static class ControlWith
{
    public static T With<T>(this T control, Action<T> set) where T : Control
    {
        set(control);
        return control;
    }
}
