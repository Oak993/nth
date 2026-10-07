// PORT-ONLY: first-release feature unit SK-15 (DL-17, ONDEMAND-PLAN.md 2.5, DL-15). The terms step of an item under a vendor's terms: the row label ("Under Anthropic's terms"), the licence text of the pinned file, and the
// separate "I have read these terms" control that stores the acceptance with its date and the licence hash. The Download button of the item waits for it. ENGINEERING DECISION (ED-354): the control stays off
// until the text is on screen (a user cannot say they read what failed to load), and a licence the catalogue holds no hash for yet cannot be accepted: the refusal's own words are shown.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Terms;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class TermsBox : Decorator
{
    private readonly Func<Task<string>> loadText;
    private readonly Action acceptTerms;
    private readonly TextBlock body = LibraryUi.Text("", 11.5, secondary: true, wrap: true);
    private readonly TextBlock problem = LibraryUi.Text("", 11.5, wrap: true);
    private readonly Border box;
    private readonly Button read;
    private readonly Border check;
    private readonly Control checkMark;
    private readonly AnimatedScalar ticked;
    private bool loading, loaded, accepted;

    public TermsBox(LibraryModel model, LibraryRow row)
        : this(row.TermsLabel ?? "", row.Licence, row.TermsOwed, () => model.Controller.LicenceText(row.Id), () => model.Controller.AcceptTerms(row.Id))
    {
    }

    /// <summary>SK-14: the same box for any surface that owes the terms step (the in-chat offer card): its label, the licence's name, whether the terms are still owed, how the pinned licence's text is read and what the control does.</summary>
    public TermsBox(string termsLabel, string? licenceName, bool owed, Func<Task<string>> loadText, Action acceptTerms)
    {
        this.loadText = loadText;
        this.acceptTerms = acceptTerms;
        var title = LibraryUi.Text(termsLabel, 12, weight: SwiftFont.Weight.Semibold);
        var licence = LibraryUi.Text(licenceName ?? "", 11.5, secondary: true, trim: true);
        licence.HorizontalAlignment = HorizontalAlignment.Right;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetColumn(licence, 1);
        heading.Children.Add(title);
        heading.Children.Add(licence);
        body.Margin = new Thickness(0, 0, 4, 0);
        var text = LibraryUi.Scroller(body, 96);
        problem.Foreground = ChromeTokens.Warning.Brush(problem);
        problem.IsVisible = false;
        checkMark = LibraryUi.Glyph("checkmark", 9, ChromeTokens.PrimaryText, SwiftFont.Weight.Bold);
        checkMark.HorizontalAlignment = HorizontalAlignment.Center;
        checkMark.VerticalAlignment = VerticalAlignment.Center;
        check = new Border { Width = 15, Height = 15, Child = checkMark, VerticalAlignment = VerticalAlignment.Center }.RoundedRectangle(4, RoundedCornerStyle.Continuous);
        check.Background = ChromeTokens.Overlay(0.12).Brush(check);
        ticked = new AnimatedScalar(check, 0, value => checkMark.Opacity = value);
        var label = LibraryUi.Text(SkillsNotices.ReadControl, 12, weight: SwiftFont.Weight.Medium);
        label.VerticalAlignment = VerticalAlignment.Center;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, label } };
        read = PlainControls.Button(line, Accept);
        read.Focusable = true;
        read.IsEnabled = false;
        read.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        read.Named(SkillsNotices.ReadControl, "terms-read");
        read.HorizontalAlignment = HorizontalAlignment.Left;
        read.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel { Spacing = 0, Children = { heading, text, problem, read } };
        box = LibraryUi.Panel(stack);
        Child = box;
        Apply(owed);
    }

    /// <summary>The licence text on show (empty until it loaded).</summary>
    public string LicenceTextShown => body.Text ?? "";

    public string ProblemShown => problem.IsVisible ? problem.Text ?? "" : "";

    public bool ReadEnabled => read.IsEnabled;

    public bool Accepted => accepted;

    public Button ReadControl => read;

    /// <summary>The text is read from the item's pinned licence file the first time the box is shown for an item that owes its terms.</summary>
    public void Apply(LibraryRow row) => Apply(row.TermsOwed);

    public void Apply(bool owed)
    {
        accepted = !owed;
        ticked.Set(accepted ? 1 : 0, LibraryUi.Ease(this, LibraryUi.Fade));
        read.IsEnabled = !accepted && loaded;
        if (!loaded && !loading && owed) _ = Load();
        if (accepted && !loaded && !loading) _ = Load();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            body.Text = await loadText();
            loaded = true;
            problem.IsVisible = false;
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            problem.Text = error.Message;
            problem.IsVisible = true;
        }
        finally
        {
            loading = false;
            read.IsEnabled = !accepted && loaded;
        }
    }

    private void Accept()
    {
        if (accepted || !loaded) return;
        try { acceptTerms(); }
        catch (InvalidOperationException error)
        {
            problem.Text = error.Message;
            problem.IsVisible = true;
        }
    }
}
