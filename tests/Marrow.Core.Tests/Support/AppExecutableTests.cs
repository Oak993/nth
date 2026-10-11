using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// nth installer PLAN.md section 7 (override 8), unit U7 (ED-NEW-inst-u7-1): the packaging script renames the apphost to nth.exe, a Debug or
// Release build keeps the SDK's name, so the three children that re-launch the app (the Cursor skills relay, the advisor relay and the ACP
// skills relay) take the running process's own path, never a name written by hand.
public sealed class AppExecutableTests
{
    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_process_path_is_the_app_whatever_the_exe_is_called()
    {
        Assert.Equal(@"C:\Users\Ana\AppData\Local\Programs\nth\current\nth.exe", AppExecutable.Resolve(@"C:\Users\Ana\AppData\Local\Programs\nth\current\nth.exe", @"C:\elsewhere\"));
        Assert.Equal(@"E:\build\bin\Debug\app.exe", AppExecutable.Resolve(@"E:\build\bin\Debug\app.exe", @"E:\build\bin\Debug\"));
    }

    [Theory]
    [Trait("Decision", "OD-0135")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\DOTNET.EXE")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_run_through_the_dotnet_host_or_with_no_process_path_falls_back_to_the_published_name_beside_the_app(string? processPath)
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-app-folder") + Path.DirectorySeparatorChar;
        Assert.Equal(Path.Combine(folder, "nth.exe"), AppExecutable.Resolve(processPath, folder));
        Assert.Equal("nth.exe", AppExecutable.PublishedName);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_running_test_process_resolves_to_itself()
    {
        var self = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(self));
        var host = string.Equals(Path.GetFileNameWithoutExtension(self), "dotnet", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(host ? Path.Combine(AppContext.BaseDirectory, "nth.exe") : self, AppExecutable.Path);
    }
}
