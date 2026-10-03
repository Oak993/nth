using System.Linq;
using System.Reflection;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e). This part (part 1 of the unit)
// covers PR-THEME-105 to 129 except 122 (not in this part's spec). Helpers reflect into private members the
// same way tests/Marrow.UI.Tests/Chrome/ProjectIconPickerTests.cs does.
public sealed class MarkdownBlockSelectionTests
{
    [Fact]
    [Trait("Req", "PR-THEME-126")] // DroppyCode/UI/Markdown/MarkdownView.swift:890-899; dead_code.md entry 30 (the else branch is dead and not implemented, ED-061). Only that Body is the identity: that the blocks select and copy is VeiledTextSelectionTests
    public void Body_returns_its_content_unchanged()
    {
        var content = new Avalonia.Controls.TextBlock();
        var result = new MarkdownBlockSelection().Body(content);
        Assert.Same(content, result);
    }
}
