// PORT-ONLY: MA-3b-7 .hc/.hh :402-405, :891-892; streamed text belongs to its account, never restarted on a different stream's event.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionCardView : Decorator
{
    private readonly CardTitleBlock text;
    private string raw = "";
    private readonly TextBlock error = LibraryUi.Text("", 12, wrap: true);
    internal string Label { get; }
    internal string AccountName { get; }
    internal Guid AccountId { get; }
    internal string Text => raw;
    internal OpinionCardView(OpinionAnswer answer, ThreadAccount? account, bool first)
    {
        AccountId = answer.AccountId; AccountName = account?.Name ?? answer.AccountId.ToString("D");
        var mono = account?.Mono ?? "?"; var role = first ? "Answer" : "Opinion";
        Label = mono + " " + AccountName + " " + role; this.Named(Label, "opinion-card-" + AccountId.ToString("N"));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 7 };
        var markText = LibraryUi.Text(mono, 9, weight: SwiftFont.Weight.Bold); markText.HorizontalAlignment = HorizontalAlignment.Center; markText.VerticalAlignment = VerticalAlignment.Center;
        var mark = new OpinionSurface(OpinionSurfaceKind.Mark, 10, account) { Width = 20, Height = 20, Child = markText };
        header.Children.Add(mark);
        var name = LibraryUi.Text(AccountName, 11.5, weight: SwiftFont.Weight.Semibold, wrap: true); name.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(name, 1); header.Children.Add(name);
        var kind = LibraryUi.Text(role, 10.5, weight: SwiftFont.Weight.Medium); kind.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(kind, 2); header.Children.Add(kind);
        text = new CardTitleBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17 }; SwiftFont.System(12).Apply(text); SetText(answer.Text);
        error.LineHeight = 17; error.IsVisible = false;
        var stack = new StackPanel { Spacing = 6, Children = { header, text, error } };
        Child = new OpinionSurface(OpinionSurfaceKind.Card, 12) { Padding = new Thickness(11, 10), Child = stack };
        AttachedToVisualTree += (_, _) => Paint(); ActualThemeVariantChanged += (_, _) => Paint();
        void Paint()
        {
            var tokens = CapsuleTokens.For(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
            text.Foreground = name.Foreground = markText.Foreground = new SolidColorBrush(tokens.Tx); kind.Foreground = error.Foreground = new SolidColorBrush(tokens.Tx2);
        }
    }
    internal void Apply(OpinionAnswer answer) { if (raw != answer.Text) SetText(answer.Text); }
    internal void ApplyError(string? value) { error.Text = value ?? ""; error.IsVisible = value is not null; }
    private void SetText(string value)
    {
        raw = value; text.Inlines!.Clear(); var displayed = new System.Text.StringBuilder(); var cursor = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(value, "`([^`]+)`"))
        {
            var before = value[cursor..match.Index]; text.Inlines.Add(new Run(before)); displayed.Append(before);
            var code = match.Groups[1].Value; text.Inlines.Add(new Run(code) { FontFamily = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular).Resolve().FontFamily }); displayed.Append(code);
            cursor = match.Index + match.Length;
        }
        var rest = value[cursor..]; text.Inlines.Add(new Run(rest)); displayed.Append(rest); text.KeepText(displayed.ToString());
    }
}
