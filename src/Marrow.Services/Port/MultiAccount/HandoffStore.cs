using System.Text;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.Services.Port.MultiAccount;

// PORT-ONLY: OD-0114; ED-407. Immutable checkpoints in the thread's own folder.
public static class HandoffStore
{
    public static HandoffSummary Save(HandoffContext context, string threadFolder, HandoffOptions? options = null)
    {
        var folder = Path.GetFullPath(threadFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "handoff-" + Guid.NewGuid().ToString("N") + ".json");
        var result = HandoffSummary.Build(context, path, options);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, result.FullConversation, new UTF8Encoding(false, true));
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return result;
    }
}
