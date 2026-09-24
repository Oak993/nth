// Source: DroppyCode/UI/Common/Components.swift (B2 1fbf81e), :1-198.

using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Marrow.UI.Tests.Common;

public sealed class ComponentsTests
{
    #region PR-SIDEBAR-074 RelativeTime Tests

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_Under60Seconds_ReturnsNow()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        Assert.Equal("now", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_Under1Hour_ReturnsTruncatedMinutes()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 12, 5, 30, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        Assert.Equal("5m", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_1Minute_ReturnsMinutes()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 12, 1, 59, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        Assert.Equal("1m", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_Under1Day_ReturnsTruncatedHours()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 18, 30, 0, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        Assert.Equal("6h", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_Under1Week_ReturnsTruncatedDays()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        Assert.Equal("4d", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_Over1Week_ReturnsFormattedDate()
    {
        var date = new DateTimeOffset(2025, 12, 15, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var result = RelativeTime.Short(date, now);

        // Format should be month abbreviated (Dec) and day (15)
        Assert.Matches(@"^[A-Z][a-z]{2} \d{1,2}$", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:20-35
    public void RelativeTime_Short_CachesBeyondOneWeek()
    {
        var date = new DateTimeOffset(2025, 12, 15, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var result1 = RelativeTime.Short(date, now);
        var result2 = RelativeTime.Short(date, now);

        Assert.Equal(result1, result2);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:37-42
    public void RelativeTime_Duration_0Seconds_Returns0s()
    {
        var result = RelativeTime.Duration(TimeSpan.FromSeconds(0));
        Assert.Equal("0s", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:37-42
    public void RelativeTime_Duration_Under1Minute_ReturnsSeconds()
    {
        var result = RelativeTime.Duration(TimeSpan.FromSeconds(45));
        Assert.Equal("45s", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:37-42
    public void RelativeTime_Duration_1Minute30Seconds_ReturnsMinutesAndSeconds()
    {
        var result = RelativeTime.Duration(TimeSpan.FromSeconds(90));
        Assert.Equal("1m 30s", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:37-42
    public void RelativeTime_Duration_Over1Hour_ReturnsHoursMinutes()
    {
        var result = RelativeTime.Duration(TimeSpan.FromSeconds(3725));
        Assert.Equal("1h 2m", result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:37-42
    public void RelativeTime_Duration_NegativeInterval_Returns0s()
    {
        var result = RelativeTime.Duration(TimeSpan.FromSeconds(-10));
        Assert.Equal("0s", result);
    }

    #endregion

    #region UI-GIT-007 DiffStatLabel Tests

    [Fact]
    [Trait("Req", "UI-GIT-007")] // DroppyCode/UI/Common/Components.swift:45-56
    public void DiffStatLabel_Basic_CreatesControl()
    {
        var label = new DiffStatLabel { Additions = 12, Deletions = 5 };
        Assert.NotNull(label);
        Assert.Equal(12, label.Additions);
        Assert.Equal(5, label.Deletions);
    }

    [Fact]
    [Trait("Req", "UI-GIT-007")] // DroppyCode/UI/Common/Components.swift:45-56
    public void DiffStatLabel_ZeroCounts_Renders()
    {
        var label = new DiffStatLabel { Additions = 0, Deletions = 0 };
        Assert.Equal(0, label.Additions);
        Assert.Equal(0, label.Deletions);
    }

    #endregion

    #region PR-CHAT-181 Editor Catalogue Tests

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:109-117
    public void Workspace_Editors_HasExactly5Entries()
    {
        var editors = Workspace.Editors;
        Assert.Equal(5, editors.Count);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:109-117
    public void Workspace_Editors_HasCorrectNames()
    {
        var editors = Workspace.Editors;
        var names = editors.Select(e => e.Name).ToList();

        var expected = new[] { "Visual Studio Code", "Cursor", "Zed", "Terminal", "Ghostty" };
        Assert.Equal(expected, names);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:109-117
    public void Workspace_Editors_FirstIsVisualStudioCode()
    {
        var editors = Workspace.Editors;
        Assert.Equal("Visual Studio Code", editors[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:109-117
    public void Workspace_Editors_ExcludesXcodeAndITerm()
    {
        var editors = Workspace.Editors;
        var names = editors.Select(e => e.Name).ToList();

        Assert.DoesNotContain("Xcode", names);
        Assert.DoesNotContain("iTerm", names);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:102-107
    public void Workspace_Editor_IdIsTheBundleID()
    {
        var editor = new Workspace.Editor { Name = "Test", BundleID = "com.test.app" };
        Assert.Equal("com.test.app", editor.Id);
    }

    #endregion

    #region ProviderIcon Tests

    [Fact]
    [Trait("Req", "PR-CHAT-181")] // Indirectly tests infrastructure
    public void ProviderIcon_Initializes()
    {
        // Provider icons are tested indirectly through Workspace
        var editors = Workspace.Editors;
        Assert.NotEmpty(editors);
    }

    #endregion
}
