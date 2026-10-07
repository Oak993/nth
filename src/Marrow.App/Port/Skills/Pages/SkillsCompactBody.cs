// PORT-ONLY: sd-page.js:366-368 keeps one fold per compact state, so leaving and arriving bodies coexist until their locked motions finish.
using Avalonia.Controls;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class SkillsCompactBody : Decorator
{
    private readonly SkillsCompactFold running = new(new Border());
    private readonly SkillsCompactFold done = new(new Border());
    private readonly SkillsCompactFold partial = new(new Border());
    internal SkillsCompactBody() => Child = new StackPanel { Children = { running, done, partial } };
    internal void Show(string phase, Control content, bool animate)
    {
        var selected = phase switch { "downloading" or "paused" => running, "done" => done, "partial" => partial, _ => null };
        if (selected is not null) selected.Child = content;
        running.Show(ReferenceEquals(selected, running), animate);
        done.Show(ReferenceEquals(selected, done), animate);
        partial.Show(ReferenceEquals(selected, partial), animate);
    }
}
