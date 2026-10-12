using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-1")]
public sealed class WebW10PagesTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("Visus", "Open links from chats in", "Search engine", "Keep at most N live tabs")]
    [InlineData("Tabula", "Live answers in chat", "Visual answers", "Artifacts may use your model")]
    public void Self_contained_pages_expose_the_approved_rows(string page, string row1, string row2, string row3) => headless.OnUiThread(() =>
    {
        var type = typeof(SettingsView).Assembly.GetType("Marrow.App.Views.Settings." + page + "SettingsPage");
        Assert.NotNull(type);
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var control = Assert.IsAssignableFrom<Control>(Activator.CreateInstance(type));
        control.Measure(new Avalonia.Size(640, 1800));
        control.Arrange(new Avalonia.Rect(0, 0, 640, 1800));
        var labels = control.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
        Assert.Contains(row1, labels); Assert.Contains(row2, labels); Assert.Contains(row3, labels);
        var enumValue = Enum.Parse<SettingsPage>(page);
        Assert.Equal(page, enumValue.Title);
        Assert.True(enumValue.IsReady);
    });
}
