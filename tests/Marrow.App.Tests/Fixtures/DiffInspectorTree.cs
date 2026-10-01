using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Reads what the changes inspector put on screen: its controls, texts and cards. Helper types live here, never inside
// a test class.
public static class DiffInspectorTree
{
    /// <summary>Every control of type T under (and including) the root, through panels, decorators and content.</summary>
    public static IEnumerable<T> Descendants<T>(Control root) where T : class
    {
        if (root is T match) yield return match;

        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    foreach (var found in Descendants<T>(child))
                        yield return found;
                break;
            case Decorator decorator when decorator.Child is not null:
                foreach (var found in Descendants<T>(decorator.Child))
                    yield return found;
                break;
            case ContentControl content when content.Content is Control contentChild:
                foreach (var found in Descendants<T>(contentChild))
                    yield return found;
                break;
        }
    }

    /// <summary>The first text block whose text is exactly <paramref name="text"/>.</summary>
    public static TextBlock? Text(Control root, string text) =>
        Descendants<TextBlock>(root).FirstOrDefault(block => block.Text == text);

    /// <summary>Every text block's text under the root.</summary>
    public static string[] Texts(Control root) =>
        Descendants<TextBlock>(root).Select(block => block.Text ?? "").ToArray();
}
