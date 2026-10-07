// PORT-ONLY: MA-3b-13, the page's <b> account-name spans; the same title remains the accessible text.
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
namespace Marrow.App.Port.MultiAccount.Cards;
internal static class CardTitle
{
    internal static void Emphasize(TextBlock title, IReadOnlyList<string> accountNames)
    {
        var text = title.Text ?? ""; var names = accountNames.Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length).ToArray();
        var runs = new List<Run>(); var position = 0;
        while (position < text.Length)
        {
            var name = names.FirstOrDefault(n => text.AsSpan(position).StartsWith(n, StringComparison.Ordinal));
            if (name is not null) { runs.Add(new Run(name) { FontWeight = FontWeight.SemiBold }); position += name.Length; }
            else
            {
                var start = position++;
                while (position < text.Length && !names.Any(n => text.AsSpan(position).StartsWith(n, StringComparison.Ordinal))) position++;
                runs.Add(new Run(text[start..position]));
            }
        }
        title.Inlines!.Clear(); foreach (var run in runs) title.Inlines.Add(run);
        if (title is CardTitleBlock block) block.KeepText(text);
    }
}
