using Avalonia.Controls;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

[Collection("SetupHeadless")]
public sealed class C7U3ByteWidthChecks(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Reserved_width_is_stable_at_zero_and_above_four_GB_at_each_text_scale()
    {
        headless.OnUiThread(() =>
        {
            foreach (var scale in new[] { 1.0, 1.5, 2.25 })
            foreach (var total in new[] { 0L, 8192L, 102400L })
            {
                var count = new ByteCount(() => true);
                var window = new Window { Width = 700, SizeToContent = SizeToContent.Height, Content = count };
                SetupTheme.Apply(window.Resources, SetupPalette.Dark, scale);
                try
                {
                    window.Show();
                    count.Set($"0 of {total} MB", $"{total} of {total} MB", true);
                    window.UpdateLayout();
                    var width = count.DesiredSize.Width;
                    Assert.True(width > 0);
                    foreach (var copied in new[] { 0, total / 2, total })
                    {
                        count.Set($"{copied} of {total} MB", $"{total} of {total} MB", true);
                        window.UpdateLayout();
                        Assert.Equal(width, count.DesiredSize.Width, 3);
                    }
                }
                finally { window.Close(); }
            }
        });
    }
}
