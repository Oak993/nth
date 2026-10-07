// PORT-ONLY: MA-3b-2, locked page AccountPanel :2682-2718, .prow :227-246; OD-0115.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountPopoverRowControl : Decorator
{
    private readonly TextBlock reset;
    private readonly AnimatedScalar feedback;
    internal AccountPopoverRowControl(AccountPopoverRow row, Action<Guid> pin)
    {
        AccountId = row.Account.Id; Dimmed = row.Dimmed;
        Height = 70; // .prow.cf :826 overrides the original row height for the config line
        var main = new StackPanel { Spacing = 5, Opacity = row.Account.Enabled ? row.Dimmed ? .5 : 1 : .45 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var dot = LibraryUi.Text(row.Active ? "●" : "", 6); dot.Margin = new Thickness(0, 0, 6, 0); top.Children.Add(dot);
        var name = LibraryUi.Text(row.Account.Name, 13); name.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(name, 1); top.Children.Add(name);
        var chip = LibraryUi.Text(row.Chip ?? "", 10, secondary: true); chip.Margin = new Thickness(6, 0); Grid.SetColumn(chip, 2); top.Children.Add(chip);
        reset = LibraryUi.Text(row.ResetText, 11, secondary: true); Grid.SetColumn(reset, 3); top.Children.Add(reset); main.Children.Add(top);
        FiveHour = new AccountUsageBar(); Week = new AccountUsageBar(); FiveHour.Apply(row.FiveHour); Week.Apply(row.Week);
        var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        if (row.NoFiveHourText is { } no5) meters.Children.Add(LibraryUi.Text(no5, 10.5, secondary: true));
        else meters.Children.Add(Meter("5 h", FiveHour));
        var weekly = Meter("Week", Week); Grid.SetColumn(weekly, 1); weekly.Margin = new Thickness(10, 0, 0, 0); meters.Children.Add(weekly);
        FiveHour.IsVisible = row.LocalText is null && row.NoFiveHourText is null; Week.IsVisible = row.LocalText is null;
        main.Children.Add(row.LocalText is { } local ? LibraryUi.Text(local, 11, secondary: true) : meters);
        var config = new AccountConfigLine(row.Config); var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        line.Children.Add(config); var hs = LibraryUi.Text(row.HandshakeText, 10.5, secondary: true); hs.Named(row.HandshakeAria, "popover-handshake-" + AccountId.ToString("N")); Grid.SetColumn(hs, 1); line.Children.Add(hs); main.Children.Add(line);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("22,20,*"), Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var grip = LibraryUi.Glyph("line.3.horizontal", 12, ChromeTokens.SecondaryText); body.Children.Add(grip);
        var number = LibraryUi.Text(row.Account.Number.ToString(CultureInfo.InvariantCulture), 12, secondary: true); number.Opacity = main.Opacity; Grid.SetColumn(number, 1); body.Children.Add(number);
        Grid.SetColumn(main, 2); body.Children.Add(main);
        var button = PlainControls.Button(body, () => pin(AccountId)); button.Focusable = true; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Height = 70;
        var aria = row.Account.Number.ToString(CultureInfo.InvariantCulture) + ". " + row.Account.Name + (row.Account.IsOut ? ", out until " + row.ResetText + ", skipped" : ", 5 hours " + row.FiveHour.Percent + ", week " + row.Week.Percent) + (row.Active ? ", in use" : "") + ". Alt+Up or Alt+Down moves it.";
        button.Named(aria, "popover-pin-" + AccountId.ToString("N")); this.Named(aria, "popover-row-" + AccountId.ToString("N")); Child = button;
        feedback = new AnimatedScalar(this, 0, progress =>
        {
            FeedbackRunning = progress < 1;
            if (!CapsuleMotion.Reduce(this)) reset.RenderTransform = progress >= 1 ? null : new TranslateTransform(Math.Sin(progress * Math.PI * 4) * 3, 0);
            else reset.Opacity = .6 + .4 * progress;
        });
    }
    internal Guid AccountId { get; }
    internal AccountUsageBar FiveHour { get; }
    internal AccountUsageBar Week { get; }
    internal bool Dimmed { get; }
    internal bool FeedbackRunning { get; private set; }
    internal void Refuse()
    {
        feedback.Set(0, null); FeedbackRunning = true;
        feedback.Set(1, FrameClock.IsBound ? Animation.Linear(duration: .3) : null);
    }
    private static Control Meter(string label, AccountUsageBar bar)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,26") };
        row.Children.Add(LibraryUi.Text(label, 10.5, secondary: true)); Grid.SetColumn(bar, 1); bar.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(bar);
        var percent = LibraryUi.Text(bar.Percent, 10, secondary: true); percent.FontFamily = new FontFamily("Cascadia Mono,Consolas"); percent.TextAlignment = TextAlignment.Right; Grid.SetColumn(percent, 2); row.Children.Add(percent); return row;
    }
}
