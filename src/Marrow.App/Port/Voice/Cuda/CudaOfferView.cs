// PORT-ONLY: OD-0106 CUDA A, vn-page.js card(): its own row below the recommendation, using the model list's controls and transitions.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port;
namespace Marrow.App.Port.Voice.Cuda;

internal sealed class CudaOfferView : Decorator
{
    private readonly CzCrossfadeText title = new(CudaOfferStates.Title) { FontSize = 14, FontWeight = FontWeight.Normal };
    private readonly CzCrossfadeText detail = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly TextBlock percent = new() { Text = "", FontSize = 12, FontFamily = new FontFamily("fonts:Inter#Inter"), FontFeatures = FontFeatureCollection.Parse("tnum"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid states;
    private readonly Panel liveFace = new();
    private readonly CzMotion motion;
    private readonly CzNumber fade, scale;
    private CudaOfferRow previous = CudaOfferRow.Hidden;
    public CzCapsuleButton Download { get; } = new("Download") { HorizontalAlignment = HorizontalAlignment.Right };
    public VoiceCancelButton Cancel { get; } = new();
    public Control Installed { get; }
    public VoiceProgressBar Bar { get; } = new();
    public string Title => title.Text;
    public string Detail => detail.Text;
    public string ProgressText => percent.Text ?? "";

    public CudaOfferView(Action<CudaOfferAction> action, Action detached)
    {
        title.SetValue(SwiftFont.LineBoxProperty, 20.0);
        detail.SetValue(SwiftFont.LineBoxProperty, 16.0);
        Download.Invoked += () => action(CudaOfferAction.Download);
        Cancel.Invoked += () => action(CudaOfferAction.Cancel);
        var check = new CzCheckMark { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        check.SetChecked(true);
        Installed = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { check, new CzCrossfadeText("Installed") { FontSize = 12, Secondary = true } } };
        var progress = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { percent, Cancel } };
        states = new Grid { Width = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { liveFace } };
        liveFace.Children.Add(new Grid { Children = { Download, Installed, progress } });
        liveFace.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        motion = new CzMotion(this);
        fade = new CzNumber(motion, 1, v => liveFace.Opacity = v);
        scale = new CzNumber(motion, 1, v => liveFace.RenderTransform = new ScaleTransform(v, v));
        var texts = new StackPanel { Spacing = 0, Children = { title, detail } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,96"), Children = { texts, states } };
        Grid.SetColumn(states, 2);
        var divider = new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0), Background = CzTheme.Brush(CzTheme.Token(this, "div")) };
        percent.Foreground = CzTheme.Text(this, true);
        ActualThemeVariantChanged += (_, _) => { divider.Background = CzTheme.Brush(CzTheme.Token(this, "div")); percent.Foreground = CzTheme.Text(this, true); };
        Bar.Margin = new Thickness(16, 0);
        Child = new StackPanel { Children = { divider, new Border { Padding = new Thickness(16, 9, 11, 9), MinHeight = 36, Child = grid }, new Border { Margin = new Thickness(0, 0, 0, 10), Child = Bar } } };
        DetachedFromVisualTree += (_, _) => detached();
        Apply(CudaOfferRow.Hidden);
    }

    private Control Snapshot(CudaOfferRow row)
    {
        if (row.Action == CudaOfferAction.Download) return new CzCapsuleButton("Download") { HorizontalAlignment = HorizontalAlignment.Right };
        if (row.ShowsBar) return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { new TextBlock { Text = row.StateText, FontSize = 12, FontFamily = new FontFamily("fonts:Inter#Inter"), FontFeatures = FontFeatureCollection.Parse("tnum"), Foreground = CzTheme.Text(this, true), VerticalAlignment = VerticalAlignment.Center }, new VoiceCancelButton() } };
        var check = new CzCheckMark { Width = 12, Height = 12 }; check.SetChecked(true);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { check, new CzCrossfadeText("Installed") { FontSize = 12, Secondary = true } } };
    }

    public void Apply(CudaOfferRow row)
    {
        if (previous.Shown && row.State != previous.State)
        {
            var snapshot = Snapshot(previous);
            snapshot.IsHitTestVisible = false;
            snapshot.IsEnabled = false;
            snapshot.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
            states.Children.Insert(0, snapshot);
            var outgoing = new CzNumber(motion, liveFace.Opacity, v =>
            {
                snapshot.Opacity = v;
                if (v == 0) states.Children.Remove(snapshot);
            });
            var shrink = new CzNumber(motion, 1, v => snapshot.RenderTransform = new ScaleTransform(v, v));
            outgoing.To(0, CzMotion.Ease(.15));
            shrink.To(.94, CzMotion.Ez(.25));
            fade.Jump(0); scale.Jump(.94);
            fade.To(1, CzMotion.Ease(.25), .08); scale.To(1, CzMotion.Ez(.35), .08);
        }
        previous = row;
        IsVisible = row.Shown;
        title.SetText(row.Title);
        detail.SetText(row.Detail);
        AutomationProperties.SetLiveSetting(percent, row.ShowsBar ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        percent.Text = row.StateText;
        Download.IsVisible = row.Action == CudaOfferAction.Download;
        Cancel.IsVisible = row.Action == CudaOfferAction.Cancel;
        percent.IsVisible = row.ShowsBar;
        Installed.IsVisible = row.ShowsCheck;
        AutomationProperties.SetName(Cancel, CudaOfferStates.CancelTitle);
        Bar.SetActive(row.ShowsBar);
        if (row.ShowsBar) Bar.SetFraction(row.Progress);
    }
}
