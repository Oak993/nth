using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/ProjectIconPicker.swift (B2 1fbf81e), :1-273.
// Records: PR-SIDEBAR-150, PR-SIDEBAR-151, PR-SIDEBAR-152, PR-SIDEBAR-153, UI-SIDEBAR-052, UI-SIDEBAR-053,
// UI-SIDEBAR-054, UI-SIDEBAR-055, UI-SIDEBAR-056, UI-SIDEBAR-057.
// Port shape (R-VIEW-1, R-VIEW-2, R-VIEW-9, ED-095): each SwiftUI View is a Decorator built once in its constructor;
// @State the body reads is a private ViewState : MainActorModel; the popover goes through PlainControls.Popover and
// Presentation.Presenter; a SF Symbol tile draws through SwiftImage.SystemName under the OD-0010 mapping. "The
// system has this symbol" (:80-85 favourites, :130 and :133 filledForm) is answered under that same deviation by
// SymbolCatalogue.Mapping, the published sf-symbols-to-lucide table: PR-SIDEBAR-151 records that this keeps all 56
// shortlist names, so Favourites below is not filtered further, only re-checked against the table the port actually
// has. A Swift file-private type (`private struct IconTile`, `private static let columns`) is a `file` type here,
// same convention as Chrome.cs's `file static class Symbols`.

/// <summary>
/// :9-27 ProjectIconMark: a project's mark as the rows draw it, the emoji or symbol picked, or the folder mark until
/// one has been picked.
/// </summary>
public sealed class ProjectIconMark : Decorator
{
    /// <param name="icon">:12 <c>let icon: ProjectIcon?</c>.</param>
    /// <param name="size">:13 <c>var size: CGFloat = 13</c>.</param>
    // :16-26 body
    public ProjectIconMark(ProjectIcon? icon, double size = 13)
    {
        if (icon?.ProjectIconEmoji is { } emoji) // :17
        {
            var text = new TextBlock { Text = emoji }; // :18
            SwiftFont.System(size).Apply(text); // :19
            text.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(text); // :25
            Child = text;
        }
        else
        {
            var image = SwiftImage.SystemName(icon?.SymbolName ?? "folder.fill") // :21
                .Font(SwiftFont.System(size)) // :22
                .ForegroundStyle(Chrome.PrimaryText.Opacity(0.9)); // :25
            Child = image;
        }
    }
}

/// <summary>:30-51 ProjectIconButton: a project's mark as a button that opens the picker over it.</summary>
public sealed class ProjectIconButton : Decorator
{
    // :34 @State private var isPresented = false
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    /// <param name="icon">:31 <c>@Binding var icon: ProjectIcon?</c>.</param>
    /// <param name="size">:32 <c>var size: CGFloat = 13</c>.</param>
    // :37-49 body
    public ProjectIconButton(Binding<ProjectIcon?> icon, double size = 13)
    {
        var state = new ViewState();

        var markSlot = new Panel { Width = size + 9, Height = size + 9 }; // :41 .frame(width: size + 9, height: size + 9)
        markSlot.Track(() => icon.Value, value =>
        {
            markSlot.Children.Clear();
            var mark = new ProjectIconMark(value, size); // :40
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            markSlot.Children.Add(mark);
        });
        // :42 .contentShape(RoundedRectangle(cornerRadius: 6, style: .continuous)): the hit region is this frame's
        // own rectangle, which the continuous rounding does not enlarge, so the default rectangular hit test matches.

        var button = PlainControls.Button(markSlot, () => state.IsPresented = !state.IsPresented); // :37-39, :44 buttonStyle(.plain)
        ToolTip.SetTip(button, "Choose this project's icon"); // :45
        AutomationProperties.SetName(button, "Project icon"); // :46
        var popover = PlainControls.Popover(
            button,
            new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
            () => new ProjectIconPicker(icon)); // :47-49
        Child = new Panel { Children = { button, popover } };
    }
}

/// <summary>
/// :56-240 ProjectIconPicker: chooses a project's mark through an emoji grid, a search over the SF Symbols the app
/// offers, and a Folder tile that clears the mark back to its default.
/// </summary>
public sealed class ProjectIconPicker : Decorator
{
    // :60-78 private static let emojis: 131 entries in fixed order, transcribed as the literal characters (R-STR-9:
    // a block this long is verified against the Swift source by a UTF-8 byte test, ProjectIconPickerTests's
    // The_emoji_list_is_the_Swift_literal_byte_for_byte, rather than escaped and commented one by one).
    private static readonly string[] Emojis =
    [
        "\U0001F680", "\U0001F6E0️", "\U0001F4E6", "\U0001F3A8", "\U0001F9EA", "\U0001F331", "\U0001F525", "\U0001F9E9",
        "\U0001F4CA", "\U0001F579️", "\U0001F3AF", "\U0001F9E0", "⚙️", "\U0001F48E", "\U0001FA84", "\U0001F37F",
        "\U0001F6F0️", "\U0001F9ED", "\U0001F4DA", "\U0001F52E", "\U0001F419", "\U0001F980", "\U0001F41D", "\U0001F98A",
        "\U0001F30A", "\U0001F319", "⚡️", "\U0001F340", "\U0001F9F5", "\U0001F3D7️", "\U0001F527", "\U0001F9F0",
        "\U0001FAE7", "\U0001F9CA", "\U0001FAB5", "\U0001FAA8", "\U0001F9F1", "\U0001F3FA", "\U0001FA91", "\U0001F6CB️",
        "\U0001F9F2", "\U0001F50B", "\U0001F50C", "\U0001F4A1", "\U0001F526", "\U0001F56F️", "\U0001F6BF", "\U0001F6C1",
        "\U0001F9F4", "\U0001F9F9", "\U0001F9FA", "\U0001F9FC", "\U0001FAA3", "\U0001F9FD", "\U0001FAA5", "\U0001FA92",
        "\U0001F5DD️", "\U0001F510", "\U0001F6E1️", "⚔️", "\U0001FA93", "\U0001F5E1️", "\U0001F3F9", "\U0001FA83",
        "\U0001F9FF", "\U0001F4E1", "\U0001F39B️", "\U0001F5B2️", "\U0001F5A8️", "⌨️", "\U0001F5A5️", "\U0001F4BB",
        "\U0001F5B1️", "\U0001F4F1", "⌚️", "\U0001F3A7", "\U0001F50A", "\U0001F3A4", "\U0001F3AC", "\U0001F4F7",
        "\U0001F3A5", "\U0001F5BC️", "\U0001F58C️", "\U0001F58D️", "✏️", "\U0001F4DD", "\U0001F4CC", "\U0001F4CD",
        "\U0001F4CE", "\U0001F5C2️", "\U0001F5C3️", "\U0001F5C4️", "\U0001F4C1", "\U0001F4C2", "\U0001F5D3️", "\U0001F4C5",
        "⏰", "⏳", "\U0001F9EE", "\U0001F50D", "\U0001F50E", "\U0001F52D", "\U0001F52C", "\U0001F9EB",
        "\U0001F9EC", "\U0001F9A0", "\U0001F321️", "\U0001F30B", "\U0001F3D4️", "\U0001F3D5️", "\U0001F9D7", "\U0001F308",
        "☄️", "\U0001FA90", "\U0001F6F8", "\U0001F30D", "\U0001F5FA️", "\U0001F9F3", "\U0001F392", "\U0001FA81",
        "\U0001F3B2", "♟️", "\U0001F9F8", "\U0001FAA9", "\U0001F3BA", "\U0001F3B8", "\U0001F941", "\U0001F3C6",
        "\U0001F947", "\U0001F393", "\U0001F9D1‍\U0001F3EB",
    ]; // :77 last entry: student then teacher, joined by U+200D ZERO WIDTH JOINER

    // :87-102 private static let shortlist: 56 SF Symbol names, each already a filled mark.
    private static readonly string[] Shortlist =
    [
        "hammer.fill", "wrench.and.screwdriver.fill", "gearshape.fill", "terminal.fill",
        "curlybraces.square.fill", "chevron.left.square.fill", "shippingbox.fill", "cube.fill",
        "tray.full.fill", "doc.fill", "square.stack.3d.up.fill", "memorychip.fill",
        "cpu.fill", "externaldrive.fill", "paintbrush.fill", "paintpalette.fill",
        "book.closed.fill", "graduationcap.fill", "lightbulb.fill", "key.fill",
        "lock.fill", "leaf.fill", "flame.fill", "drop.fill",
        "sun.max.fill", "moon.stars.fill", "cloud.fill", "bolt.fill",
        "star.fill", "heart.fill", "diamond.fill", "crown.fill",
        "trophy.fill", "medal.fill", "checkmark.seal.fill", "gift.fill",
        "gamecontroller.fill", "dpad.fill", "popcorn.fill", "film.fill",
        "mic.fill", "speaker.wave.3.fill", "camera.fill", "photo.fill",
        "globe.fill", "point.3.filled.connected.trianglepath.dotted", "hand.point.up.left.fill", "person.fill",
        "person.2.fill", "pawprint.fill", "bird.fill", "fish.fill",
        "cup.and.saucer.fill", "waterbottle.fill", "takeoutbag.and.cup.and.straw.fill", "theatermasks.fill",
    ];

    // :80-85 favourites: the shortlist checked against the offered catalogue, which at B2 is the OD-0010 mapping
    // table (deviation, above). PR-SIDEBAR-151 records that every one of the 56 is kept.
    private static readonly ValueList<string> Favourites =
        new(Shortlist.Where(name => SymbolCatalogue.Mapping(name) is not null));

    // :104-109 groups: Popular (the favourites) followed by every catalogue group, in the catalogue's own order.
    private static readonly ValueList<ProjectSymbolCatalogue.Group> Groups =
        new(new[] { new ProjectSymbolCatalogue.Group("popular", "Popular", Favourites) } // :108
            .Concat(ProjectSymbolCatalogue.Groups)); // :109

    // :111 private static let columns = Array(repeating: GridItem(.fixed(26), spacing: 4), count: 9)
    private const int GridColumns = 9;
    private const double CellSize = 26;
    private const double GridSpacing = 4;

    /// <summary>
    /// :128-134 filledForm(of:): a typed name as the grid may offer it, under the OD-0010 deviation (above): the name
    /// itself when it already ends `.fill` or `.inverse` and the table maps it, its `.fill` form when the table maps
    /// that, otherwise null.
    /// </summary>
    private static string? FilledForm(string name)
    {
        if (name.EndsWith(".fill", StringComparison.Ordinal) || name.EndsWith(".inverse", StringComparison.Ordinal)) // :129
            return SymbolCatalogue.Mapping(name) is not null ? name : null; // :130
        var filled = name + ".fill"; // :132
        return SymbolCatalogue.Mapping(filled) is not null ? filled : null; // :133
    }

    /// <summary>:115-123 matchingSymbols: the catalogue names the trimmed query names, with a typed filled form
    /// hoisted to the front when the catalogue does not already hold it.</summary>
    private static ValueList<string> MatchingSymbols(string query)
    {
        var needle = FoundationText.Trim(query); // :116 query.trimmingCharacters(in: .whitespaces)
        if (needle.Length == 0) return ValueList<string>.Empty; // :117
        var names = ProjectSymbolCatalogue.All
            .Where(name => SwiftString.LocalizedCaseInsensitiveContains(name, needle)) // :118
            .ToList();
        if (FilledForm(needle) is { } typed && !names.Contains(typed, StringComparer.Ordinal)) // :119
            names.Insert(0, typed); // :120
        return new ValueList<string>(names); // :122
    }

    // :58 @State private var query = ""
    private sealed class ViewState : MainActorModel
    {
        private string query = "";

        public string Query { get => Read(query); set => Set(ref query, value); }
    }

    /// <param name="icon">:57 <c>@Binding var icon: ProjectIcon?</c>.</param>
    // :136-172 body
    public ProjectIconPicker(Binding<ProjectIcon?> icon)
    {
        var state = new ViewState();

        var heading = new TextBlock { Text = "Project icon" }; // :138
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Apply(heading); // :139
        heading.Foreground = Chrome.PrimaryText.Brush(heading); // :140

        var queryBinding = new Binding<string>(() => state.Query, value => state.Query = value);
        var searchField = BuildSearchField(queryBinding); // :141, :188-203

        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, Margin = new Thickness(0, 1) }; // :143 spacing 14, :153 padding(.vertical, 1)
        content.Track(() => (state.Query, icon.Value), _ => RebuildSections(content, state.Query, icon)); // :144-151
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // :155 .scrollIndicators(.never)
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        }; // :142

        var divider = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch }; // :156
        divider.Background = Chrome.Overlay(0.12).Brush(divider); // :157

        var folderTileSlot = new Panel { Width = CellSize, Height = CellSize };
        folderTileSlot.Track(() => icon.Value is null, _ =>
        {
            folderTileSlot.Children.Clear();
            folderTileSlot.Children.Add(new IconTile(
                isSelected: icon.Value is null, // :159
                action: () => icon.Value = null, // :159 icon = nil
                content: () => SwiftImage.SystemName("folder.fill") // :160
                    .Font(SwiftFont.System(SwiftFont.TextStyle.Callout)) // :161
                    .ForegroundStyle(Chrome.SecondaryText))); // :162
        });

        var folderLabel = new TextBlock { Text = "Folder" }; // :164
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(folderLabel); // :165
        folderLabel.Foreground = Chrome.SecondaryText.Brush(folderLabel); // :166
        folderLabel.VerticalAlignment = VerticalAlignment.Center;

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :158
        folderTileSlot.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(folderTileSlot);
        footer.Children.Add(folderLabel);
        // :167 Spacer(minLength: 0): the footer stretches to the panel's width and the rest is left empty.
        footer.HorizontalAlignment = HorizontalAlignment.Stretch;

        // :137 VStack(spacing: 10): a Grid, not a StackPanel, so the scroll view (:142) gets the space every fixed
        // sibling leaves inside the outer :171 frame(height: 420), the same way SwiftUI's VStack gives a ScrollView
        // the room its neighbours do not need.
        searchField.Margin = new Thickness(0, 10, 0, 0);
        scroll.Margin = new Thickness(0, 10, 0, 0);
        divider.Margin = new Thickness(0, 10, 0, 0);
        footer.Margin = new Thickness(0, 10, 0, 0);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        Grid.SetRow(searchField, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(divider, 3);
        Grid.SetRow(footer, 4);
        root.Children.Add(heading);
        root.Children.Add(searchField);
        root.Children.Add(scroll);
        root.Children.Add(divider);
        root.Children.Add(footer);

        Child = new Border { Width = 300, Height = 420, Padding = new Thickness(12), Child = root }; // :170-171
    }

    // :188-203 searchField
    private static Control BuildSearchField(Binding<string> query)
    {
        var magnifier = SwiftImage.SystemName("magnifyingglass") // :190
            .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline)) // :191
            .ForegroundStyle(Chrome.SecondaryText); // :192
        var field = PlainControls.TextField("Search symbols", query); // :193-194
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(field); // :195
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.VerticalAlignment = VerticalAlignment.Center;
        magnifier.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { magnifier, field } }; // :189
        var background = new Border { Padding = new Thickness(8, 5), Child = row } // :197-198
            .RoundedRectangle(6, RoundedCornerStyle.Continuous); // :199-201
        background.Background = Chrome.Overlay(0.10).Brush(background); // :201
        return background;
    }

    // :144-151 the sectioned default view, or a single Symbols grid once the search has a match.
    private static void RebuildSections(StackPanel content, string query, Binding<ProjectIcon?> icon)
    {
        content.Children.Clear();
        var matches = MatchingSymbols(query);
        if (matches.Count == 0) // :144
        {
            content.Children.Add(BuildSection("Emoji", BuildEmojiGrid(icon))); // :145, :205-216
            foreach (var group in Groups) content.Children.Add(BuildSection(group.Title, BuildSymbolGrid(group.Names, icon))); // :146-148, :174-186
        }
        else
        {
            content.Children.Add(BuildSection("Symbols", BuildSymbolGrid(matches, icon))); // :149-150, :218-230
        }
    }

    // :232-239 section(_:content:)
    private static Control BuildSection(string title, Control gridContent)
    {
        var heading = new TextBlock { Text = title }; // :234
        SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium).Apply(heading); // :235
        heading.Foreground = Chrome.SecondaryText.Brush(heading); // :236
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 5 }; // :233
        stack.Children.Add(heading);
        stack.Children.Add(gridContent);
        return stack;
    }

    // :205-216 emojiGrid
    private static Control BuildEmojiGrid(Binding<ProjectIcon?> icon)
    {
        var grid = new IconGrid();
        foreach (var emoji in Emojis)
        {
            var captured = emoji;
            grid.Children.Add(new IconTile(
                isSelected: icon.Value?.ProjectIconEmoji == captured, // :209
                action: () => icon.Value = new ProjectIcon.Emoji(captured), // :209
                content: () =>
                {
                    var text = new TextBlock { Text = captured }; // :210
                    SwiftFont.System(SwiftFont.TextStyle.Title3).Apply(text); // :211
                    return text;
                }));
        }
        return grid;
    }

    // :174-186 symbolSection's grid and :218-230 symbolGrid: the same tile shape over a name list.
    private static Control BuildSymbolGrid(IEnumerable<string> names, Binding<ProjectIcon?> icon)
    {
        var grid = new IconGrid();
        foreach (var name in names)
        {
            var captured = name;
            grid.Children.Add(new IconTile(
                isSelected: icon.Value?.SymbolName == captured, // :178, :221
                action: () => icon.Value = new ProjectIcon.Symbol(captured), // :178, :221
                content: () => SwiftImage.SystemName(captured) // :179, :222
                    .Font(SwiftFont.System(SwiftFont.TextStyle.Callout)) // :180, :223
                    .ForegroundStyle(Chrome.PrimaryText.Opacity(0.9)))); // :181, :224
        }
        return grid;
    }

    // :111 the nine fixed 26pt columns, 4pt apart, that every grid in this file lays its tiles into.
    private sealed class IconGrid : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var constraint = new Size(CellSize, CellSize);
            foreach (var child in Children) child.Measure(constraint);
            var rows = Children.Count == 0 ? 0 : (Children.Count + GridColumns - 1) / GridColumns;
            var width = GridColumns * CellSize + (GridColumns - 1) * GridSpacing;
            var height = rows == 0 ? 0 : rows * CellSize + (rows - 1) * GridSpacing;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (var i = 0; i < Children.Count; i++)
            {
                var column = i % GridColumns;
                var row = i / GridColumns;
                Children[i].Arrange(new Rect(
                    column * (CellSize + GridSpacing),
                    row * (CellSize + GridSpacing),
                    CellSize,
                    CellSize));
            }
            return finalSize;
        }
    }
}

/// <summary>
/// :242-272 IconTile: one mark in the picker's grid, filling as the pointer crosses it and wearing the accent ring
/// when it is the project's mark. File-private in Swift (`private struct IconTile`), a `file` type here.
/// </summary>
file sealed class IconTile : Decorator
{
    // :249 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    /// <param name="isSelected">:245 <c>let isSelected: Bool</c>.</param>
    /// <param name="action">:246 <c>let action: () -&gt; Void</c>.</param>
    /// <param name="content">:247 <c>@ViewBuilder var content: Content</c>.</param>
    // :251-266 body
    public IconTile(bool isSelected, Action action, Func<Control> content)
    {
        var state = new ViewState();

        var inner = content();
        inner.Width = 26; // :254
        inner.Height = 26;
        inner.HorizontalAlignment = HorizontalAlignment.Center;
        inner.VerticalAlignment = VerticalAlignment.Center;

        var fillLayer = new Border { Width = 26, Height = 26 } // :256-257
            .RoundedRectangle(6, RoundedCornerStyle.Continuous);
        var fill = new AnimatedColor(fillLayer);
        fillLayer.Background = fill.Brush;
        fill.Set(Chrome.Overlay(isSelected ? 0.14 : 0), null); // :257 isHovering || isSelected, isHovering starts false

        var selectionRing = new Border // :260-263
        {
            Width = 26,
            Height = 26,
            BorderThickness = new Thickness(1.5),
            IsVisible = isSelected,
        }.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        selectionRing.BorderBrush = Chrome.Accent.Brush(selectionRing);

        var stack = new Panel { Width = 26, Height = 26, Children = { fillLayer, inner, selectionRing } };
        // :265 .contentShape(RoundedRectangle(cornerRadius: 6, style: .continuous)): the hit region already matches
        // this 26x26 frame, which the continuous rounding does not enlarge.

        var button = PlainControls.Button(stack, action); // :252-253, :267 buttonStyle(.plain)
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :268-270
        button.Track(() => state.IsHovering, hovering =>
            Transaction.WithAnimation(Chrome.Hover, () => fill.Set(Chrome.Overlay(hovering || isSelected ? 0.14 : 0), Transaction.Current))); // :257
        Child = button;
    }
}
