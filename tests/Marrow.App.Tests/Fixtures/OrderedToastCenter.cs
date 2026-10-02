using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A notification centre that records, in order, "permission" for each request for permission and "add:" plus the toast's
/// body for each toast it was given (R-TEST-10: nothing is posted). A refusal can be made to fail the request.
/// </summary>
public sealed class OrderedToastCenter : IToastCenter
{
    public List<string> Log { get; } = [];

    public bool FailPermission { get; set; }

    public int Permissions => Log.Count(entry => entry == "permission");

    public void InstallDelegate(AppDelegate appDelegate) { }

    public void RequestPermission()
    {
        Log.Add("permission");
        if (FailPermission) throw new InvalidOperationException("fake permission refusal");
    }

    public Task Add(ToastRequest request)
    {
        Log.Add("add:" + request.Body);
        return Task.CompletedTask;
    }
}
