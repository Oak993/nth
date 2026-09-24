using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A fake IUpdateToastCenter (App/Port/UpdateNotificationCenter.cs) that records every Show call, for
/// UpdateCheckerSecondTests (PR-UPDATE-021, PR-UPDATE-022).</summary>
public sealed class RecordingToastCenter : IUpdateToastCenter
{
    public List<(string Identifier, string Title, string Body, string Version)> Shown { get; } = [];

    public void Show(string identifier, string title, string body, string version) =>
        Shown.Add((identifier, title, body, version));
}
