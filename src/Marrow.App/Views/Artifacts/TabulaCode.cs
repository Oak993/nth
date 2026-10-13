using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, Tabula's Code view (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1 "Projects: ... The Code
// view gets a file list", 6.3 "Code is read-only with 'Edit' to make it editable; saving an edit makes a version authored
// by 'you'", 6.4 "Code shows the text arriving"; the approved canvas index.html .code: 12px/1.7 mono, numbers in a 38 DIP
// gutter, keywords, strings and comments tinted; ED-NEW-web-w14-1). No Swift counterpart. One file's text, highlighted by
// Core's lexer and selectable (a selection becomes "Change this part"), with the project's files beside it when wide and as
// a strip of chips above it when narrow. Edit swaps the text for an editor in the same place, so nothing jumps.

namespace Marrow.App.Views.Artifacts;

/// <summary>The Code view: the line numbers, the highlighted text, the project's files, and the editor.</summary>
public sealed class TabulaCodeView : Grid
{
    /// <summary>Above this width a project's files list on the left; below it they are chips above the code.</summary>
    public const double FileListWidth = 560;

    /// <summary>Text longer than this shows unhighlighted (a few large files stay fast).</summary>
    public const int HighlightLimit = 96 * 1024;

    private readonly StackPanel sideList = new() { Spacing = 1, Margin = new Thickness(6, 8, 6, 8) };
    private readonly ScrollViewer side;
    private readonly StackPanel chipRow = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 8, 10, 4) };
    private readonly ScrollViewer chips;
    private readonly TextBlock numbers = new()
    {
        FontFamily = TabulaTokens.Mono,
        FontSize = TabulaTokens.CodeSize,
        LineHeight = TabulaTokens.CodeLine,
        TextAlignment = TextAlignment.Right,
        Width = 38,
        Margin = new Thickness(0, 14, 14, 14),
    };
    private readonly SelectableTextBlock code = new()
    {
        FontFamily = TabulaTokens.Mono,
        FontSize = TabulaTokens.CodeSize,
        LineHeight = TabulaTokens.CodeLine,
        TextWrapping = TextWrapping.NoWrap,
        Margin = new Thickness(0, 14, 16, 14),
    };
    private readonly TextBox editor = new()
    {
        FontFamily = TabulaTokens.Mono,
        FontSize = TabulaTokens.CodeSize,
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        BorderThickness = default,
        Background = Brushes.Transparent,
        Padding = new Thickness(52, 14, 16, 14),
        IsVisible = false,
        MinHeight = 0,
    };
    private readonly ScrollViewer scroll;
    private readonly Grid page = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    private IReadOnlyList<string> files = [];
    private string? selectedFile;
    private string text = "";
    private CodeFamily family = CodeFamily.Plain;
    private bool hasFiles;

    public TabulaCodeView()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        RowDefinitions = new RowDefinitions("Auto,*");
        side = new ScrollViewer { Content = sideList, Width = 176, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
        Grid.SetRowSpan(side, 2);
        chips = new ScrollViewer { Content = chipRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
        Grid.SetColumn(chips, 1);
        page.Children.Add(numbers);
        Grid.SetColumn(code, 1);
        page.Children.Add(code);
        scroll = new ScrollViewer
        {
            Content = page,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var body = new Panel { Children = { scroll, editor } };
        Grid.SetColumn(body, 1);
        Grid.SetRow(body, 1);
        Children.Add(side);
        Children.Add(chips);
        Children.Add(body);
        AutomationProperties.SetName(code, UiText.Get("tabula.code.name"));
        AutomationProperties.SetName(editor, UiText.Get("tabula.code.editor_name"));
        AutomationProperties.SetName(sideList, UiText.Get("tabula.code.files_name"));
        sideList.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.List);
        AutomationProperties.SetAccessibilityView(numbers, AccessibilityView.Raw);
        SizeChanged += (_, _) => PlaceFiles();
        ActualThemeVariantChanged += (_, _) => Paint();
        Paint();
    }

    /// <summary>A project file was chosen (its path).</summary>
    public event Action<string>? FileChosen;

    /// <summary>The text shown (or being edited).</summary>
    public string Text => text;

    /// <summary>The project file shown, or null for a single-file artifact.</summary>
    public string? File => hasFiles ? selectedFile : null;

    public IReadOnlyList<string> Files => files;

    public bool IsEditing => editor.IsVisible;

    public string EditedText => editor.Text ?? "";

    /// <summary>The text the user chose in the code, or null.</summary>
    public string? SelectedText => IsEditing ? NonEmpty(editor.SelectedText) : NonEmpty(code.SelectedText);

    /// <summary>The project's file list shows on the left (wide) rather than as chips (narrow).</summary>
    public bool ShowsFileList => side.IsVisible;

    internal SelectableTextBlock CodeBlock => code;

    internal TextBox Editor => editor;

    internal IReadOnlyList<Button> FileButtons => (side.IsVisible ? sideList.Children : chipRow.Children).OfType<Button>().ToList();

    /// <summary>Shows a file's text; <paramref name="projectFiles"/> lists a project's files (null for one file).</summary>
    public void Show(string content, string? language, IReadOnlyList<string>? projectFiles = null, string? file = null)
    {
        var changed = content != text || CodeHighlight.FamilyOf(language) != family;
        text = content;
        family = CodeHighlight.FamilyOf(language);
        var list = projectFiles ?? [];
        if (!list.SequenceEqual(files, StringComparer.Ordinal) || selectedFile != file || hasFiles != projectFiles is not null)
        {
            files = list;
            selectedFile = file;
            hasFiles = projectFiles is not null;
            BuildFiles();
        }
        if (changed) Paint();
    }

    /// <summary>Starts editing what is shown, in place.</summary>
    public void BeginEdit()
    {
        editor.Text = text;
        editor.IsVisible = true;
        scroll.IsVisible = false;
        editor.Focus();
        editor.CaretIndex = 0;
    }

    public void EndEdit()
    {
        editor.IsVisible = false;
        scroll.IsVisible = true;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private void Paint()
    {
        var lines = Math.Max(1, CountLines(text));
        numbers.Text = string.Join("\n", Enumerable.Range(1, lines));
        numbers.Foreground = TabulaTokens.LineNumber.Brush(numbers);
        code.Foreground = TabulaTokens.CodeText.Brush(code);
        editor.Foreground = TabulaTokens.CodeText.Brush(editor);
        code.Inlines?.Clear();
        if (text.Length == 0 || text.Length > HighlightLimit || family == CodeFamily.Plain)
        {
            code.Text = text;
            return;
        }
        code.Text = null;
        var inlines = new InlineCollection();
        var at = 0;
        foreach (var token in CodeHighlight.Tokens(text, family))
        {
            if (token.Start < at) continue;
            if (token.Start > at) inlines.Add(new Run(text[at..token.Start]));
            inlines.Add(new Run(text.Substring(token.Start, token.Length)) { Foreground = Tint(token.Kind) });
            at = token.Start + token.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
        code.Inlines = inlines;
    }

    private IBrush Tint(CodeTokenKind kind) => (kind switch
    {
        CodeTokenKind.Keyword => TabulaTokens.Keyword,
        CodeTokenKind.String => TabulaTokens.StringText,
        CodeTokenKind.Comment => TabulaTokens.Comment,
        CodeTokenKind.Number => TabulaTokens.Number,
        CodeTokenKind.Tag => TabulaTokens.Tag,
        _ => TabulaTokens.Number,
    }).Brush(code);

    internal static int CountLines(string value)
    {
        if (value.Length == 0) return 0;
        var count = 1;
        foreach (var c in value) if (c == '\n') count++;
        return value.EndsWith('\n') ? count - 1 : count;
    }

    private void BuildFiles()
    {
        sideList.Children.Clear();
        chipRow.Children.Clear();
        if (!hasFiles)
        {
            PlaceFiles();
            return;
        }
        foreach (var path in files)
        {
            sideList.Children.Add(FileRow(path, chip: false));
            chipRow.Children.Add(FileRow(path, chip: true));
        }
        PlaceFiles();
    }

    private Button FileRow(string path, bool chip)
    {
        var on = string.Equals(path, selectedFile, StringComparison.OrdinalIgnoreCase);
        var label = new TextBlock
        {
            Text = chip ? Path.GetFileName(path) : path,
            FontFamily = TabulaTokens.Mono,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new Border
        {
            Height = chip ? 24 : 26,
            Padding = new Thickness(chip ? 9 : 8, 0),
            CornerRadius = new CornerRadius(chip ? 12 : 7),
            Child = label,
        };
        var button = PlainControls.Button(face, () => FileChosen?.Invoke(path)).KeyboardFocus(chip ? 12 : 7);
        if (!chip) button.HorizontalAlignment = HorizontalAlignment.Stretch;
        void Paint(bool hover)
        {
            face.Background = on || hover ? PaneTokens.HoverFill.Brush(face) : chip ? TabulaTokens.PillFill.Opacity(0.5).Brush(face) : Brushes.Transparent;
            label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(label);
        }
        button.OnHover(Paint);
        Paint(false);
        ToolTip.SetTip(button, path);
        AutomationProperties.SetName(button, path);
        if (on) AutomationProperties.SetItemStatus(button, UiText.Get("tabula.common.shown"));
        return button;
    }

    private void PlaceFiles()
    {
        var wide = Bounds.Width >= FileListWidth;
        side.IsVisible = hasFiles && wide;
        chips.IsVisible = hasFiles && !wide;
        Grid.SetColumn(chips, side.IsVisible ? 1 : 0);
        Grid.SetColumnSpan(chips, side.IsVisible ? 1 : 2);
        var body = Children[2];
        Grid.SetColumn(body, side.IsVisible ? 1 : 0);
        Grid.SetColumnSpan(body, side.IsVisible ? 1 : 2);
        side.Background = TabulaTokens.InfoFill.Brush(side);
    }
}
