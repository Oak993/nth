// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5: "the Activity view verifies the chain on open and
// shows 'Verified' or the first line that fails"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the strip that says whether the log can be trusted.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>
/// The chain check's result, in one strip above the log: a spinner while it runs; "Verified" with how many lines it checked;
/// or the first line that fails, in words ("Line 812 of 2026-10-11.jsonl fails: its HMAC does not match its contents…"), with a
/// button that brings that line into sight. It is a polite live region, so a screen reader hears the result when the check
/// ends. The strip's height does not change between states (the text wraps inside a minimum height), so nothing below it moves.
/// </summary>
internal sealed class ActivityStatusStrip : Decorator
{
    public const double IconSize = 18;
    public const double CornerRadius = 12;
    public const double StripMinHeight = 56;

    private readonly ActivityLogModel model;
    private readonly Action showFailure;
    private readonly Border box = new();
    private readonly Border iconSlot = new() { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Top };
    private readonly LiveStatusText headline = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border actionSlot = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly AnimatedColor fill;
    private string? iconName;
    private Button? action;

    public ActivityStatusStrip(ActivityLogModel model, Action showFailure)
    {
        this.model = model;
        this.showFailure = showFailure;
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(headline);
        headline.Foreground = Chrome.PrimaryText.Brush(headline);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(detail);
        detail.Foreground = Chrome.SecondaryText.Brush(detail);
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { headline, detail } };
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,12,Auto"), MinHeight = StripMinHeight - 24 };
        Grid.SetColumn(iconSlot, 0);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(actionSlot, 4);
        row.Children.Add(iconSlot);
        row.Children.Add(text);
        row.Children.Add(actionSlot);
        box.Padding = new Thickness(14, 12);
        box.MinHeight = StripMinHeight;
        box.Child = row;
        box.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous);
        fill = new AnimatedColor(box);
        box.Background = fill.Brush;
        AutomationProperties.SetAccessibilityView(iconSlot, AccessibilityView.Raw);
        Child = box;
        Refresh(animate: false);
    }

    /// <summary>The text of the headline (the strip's tests read it).</summary>
    public string Headline => headline.Text ?? "";

    public string Detail => detail.Text ?? "";

    /// <summary>The name of the button that brings the failing line into sight, or null when the strip has none.</summary>
    public string? ActionTitle { get; private set; }

    public Button? ActionButton => action;

    /// <summary>The symbol drawn, or null while the spinner runs.</summary>
    public string? Symbol => iconName;

    /// <summary>Reads the model again and shows what it says now.</summary>
    public void Refresh(bool animate = true)
    {
        var (tint, symbol, title, subtitle, button) = Look();
        var animation = animate && !Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? Animation.EaseOut(0.25) : null;
        fill.Set(tint.Opacity(0.11), animation);
        headline.Text = title;
        detail.Text = subtitle;
        detail.IsVisible = subtitle.Length > 0;
        AutomationProperties.SetName(headline, subtitle.Length > 0 ? title + ". " + subtitle : title);
        SetIcon(symbol, tint);
        SetAction(button);
    }

    private (SwiftColor Tint, string? Symbol, string Title, string Subtitle, string? Button) Look()
    {
        switch (model.Phase)
        {
            case ActivityLoadPhase.Idle:
            case ActivityLoadPhase.Checking:
                return (Chrome.PrimaryText, null, ActivityWords.Checking, "", null);
            case ActivityLoadPhase.Unavailable:
                return (Chrome.Warning, "exclamationmark.triangle.fill", "The log could not be read", model.Failure ?? "", null);
        }
        var snapshot = model.Snapshot!;
        var verification = snapshot.Verification;
        var detailText = ActivityWords.Detail(verification, snapshot);
        switch (verification.Status)
        {
            case AuditVerificationStatus.Verified:
                return (Chrome.Success, "checkmark.shield.fill", ActivityWords.Headline(verification), detailText, null);
            case AuditVerificationStatus.Empty:
                return (Chrome.PrimaryText, "checkmark.shield.fill", ActivityWords.Headline(verification), ActivityWords.EmptyLog, null);
            case AuditVerificationStatus.Failed:
                var button = snapshot.FirstFailureRow is not null ? ActivityWords.ShowLine
                    : verification.FirstFailure is { File.Length: >= 10 } ? ActivityWords.ShowDay : null;
                return (Chrome.Danger, "xmark.octagon.fill", ActivityWords.Headline(verification), detailText, button);
            default:
                return (Chrome.Warning, "exclamationmark.triangle.fill", ActivityWords.Headline(verification), detailText, null);
        }
    }

    private void SetIcon(string? symbol, SwiftColor tint)
    {
        iconName = symbol;
        if (symbol is null)
        {
            iconSlot.Child = new ProgressBar { IsIndeterminate = true, Width = IconSize, Height = IconSize };
            return;
        }
        var image = SwiftImage.SystemName(symbol).Font(SwiftFont.System(IconSize - 2, SwiftFont.Weight.Semibold)).ForegroundStyle(tint);
        iconSlot.Child = image;
    }

    private void SetAction(string? title)
    {
        ActionTitle = title;
        if (title is null)
        {
            action = null;
            actionSlot.Child = null;
            return;
        }
        var label = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        var face = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = label, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        action = PlainControls.Button(face, showFailure);
        AutomationProperties.SetName(action, title);
        actionSlot.Child = action.ChromeGlassCapsule();
    }
}
