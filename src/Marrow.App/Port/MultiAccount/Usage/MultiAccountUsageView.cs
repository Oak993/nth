// PORT-ONLY: MA-3b-9, locked Usage B :4328-4350; OD-0115 usage=B and OD-0076 transitions.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Popover;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Usage;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Usage;
internal sealed class MultiAccountUsageView : Decorator
{
    private readonly UsagePanelModel model;
    private readonly CrossfadeSlot slot = new();
    private readonly AnimatedScalar slide;
    private Control? page, leaving;
    private readonly TranslateTransform pageOffset = new(), leavingOffset = new();
    private double slideFrom;
    private bool slidingAll;
    internal MultiAccountUsageView(UsagePanelModel model, bool ownsModel = false)
    {
        this.model = model; Child = slot; ClipToBounds = true; MinWidth = 240;
        slide = new AnimatedScalar(this, 0, x =>
        {
            var reduce = CapsuleMotion.Reduce(this);
            pageOffset.X = x; if (page is not null) page.RenderTransform = reduce ? null : pageOffset;
            if (leaving is null) return;
            var progress = slideFrom == 0 ? 1 : 1 - x / slideFrom;
            leavingOffset.X = (slidingAll ? -.28 : 1) * Bounds.Width * progress;
            leaving.RenderTransform = reduce ? null : leavingOffset;
        });
        Refresh(); AttachedToVisualTree += (_, _) => model.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => { model.Changed -= OnChanged; if (ownsModel) model.Dispose(); };
        KeyDown += (_, e) => { if (e.Key != Key.Escape) return; if (AllShown) Go(false); else CloseRequested?.Invoke(); e.Handled = true; };
    }
    internal bool AllShown { get; private set; }
    internal event Action? CloseRequested;
    private void OnChanged()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh(); else Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }
    private void Go(bool all)
    {
        if (AllShown == all) return;
        leaving = page; slidingAll = all; slideFrom = Bounds.Width * (all ? 1 : -.28);
        AllShown = all; Refresh();
        slide.Set(slideFrom, null); slide.Set(0, FrameClock.IsBound ? Animation.TimingCurve(.32, .72, 0, 1, .45) : null);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (page is not StackPanel panel) return;
            panel.Children.OfType<Button>().FirstOrDefault()?.Focus();
        });
    }
    private void Refresh()
    {
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        if (AllShown)
        {
            body.Children.Add(Button(model.BackText, "usage-back", () => Go(false)));
            foreach (var group in model.Groups)
            {
                body.Children.Add(LibraryUi.Text(group.Title, 12, secondary: true, weight: SwiftFont.Weight.Semibold));
                foreach (var row in group.Rows)
                {
                    var account = new StackPanel { Spacing = 5, Opacity = row.Dimmed ? .5 : 1 };
                    var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    labels.Children.Add(LibraryUi.Text(row.Account.Name, 12)); var summary = LibraryUi.Text(row.Summary, 11, secondary: true); Grid.SetColumn(summary, 1); labels.Children.Add(summary); account.Children.Add(labels);
                    if (!row.Account.NoLimit && !row.Account.Service.IsAPIKeyBased)
                    {
                        if (row.NoFiveHourText is { } no5) account.Children.Add(LibraryUi.Text(no5, 10.5, secondary: true));
                        else account.Children.Add(Meter("5 h", row.FiveHour, ""));
                        account.Children.Add(Meter("Week", row.Week, ""));
                    }
                    body.Children.Add(account);
                }
            }
        }
        else
        {
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            title.Children.Add(LibraryUi.Text(model.Title, 12, weight: SwiftFont.Weight.Semibold));
            var sub = LibraryUi.Text(model.Subtitle, 11, secondary: true); Grid.SetColumn(sub, 1); title.Children.Add(sub); body.Children.Add(title);
            if (model.Current is { } row)
            {
                body.Children.Add(LibraryUi.Text(row.Account.Name, 15, weight: SwiftFont.Weight.Semibold));
                if (row.Account.NoLimit || row.Account.Service.IsAPIKeyBased) body.Children.Add(LibraryUi.Text(row.Summary, 11.5, secondary: true));
                else
                {
                    if (row.NoFiveHourText is { } no5) body.Children.Add(new StackPanel { Spacing = 4, Children =
                    {
                        LibraryUi.Text(model.FiveHourLabel, 11.5, secondary: true), LibraryUi.Text(no5, 10.5, secondary: true),
                    } });
                    else body.Children.Add(Meter(model.FiveHourLabel, row.FiveHour, row.FiveHourReset));
                    body.Children.Add(Meter(model.WeekLabel, row.Week, row.WeekReset));
                }
            }
            if (model.NextText is { } next) body.Children.Add(LibraryUi.Text(next, 11.5, secondary: true));
            body.Children.Add(Button(model.AllAccountsText, "usage-all-accounts", () => Go(true)));
        }
        page = body; slot.Show(body, FrameClock.IsBound ? Animation.TimingCurve(.25, .1, .25, 1, .3) : null);
    }
    private static Control Meter(string label, PopoverUsageBar value, string reset)
    {
        var row = new StackPanel { Spacing = 4 };
        var names = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        names.Children.Add(LibraryUi.Text(label, 11.5, secondary: true));
        var pct = LibraryUi.Text(value.Percent, 11.5, weight: SwiftFont.Weight.Semibold); Grid.SetColumn(pct, 1); names.Children.Add(pct); row.Children.Add(names);
        var bar = new AccountUsageBar { Height = 6 }; bar.Apply(value); row.Children.Add(bar);
        if (reset.Length > 0) row.Children.Add(LibraryUi.Text(reset, 11.5, secondary: true)); return row;
    }
    private static Button Button(string words, string id, Action click)
    {
        var button = PlainControls.Button(LibraryUi.Text(words, 12), click); button.Focusable = true; button.Named(words, id); return button;
    }
}
