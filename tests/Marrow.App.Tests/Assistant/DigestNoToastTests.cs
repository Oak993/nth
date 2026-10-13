using System.Runtime.CompilerServices;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2 "never pushed, never a toast", 6 "It sends no toast", 13.1 "Windows toasts only when a decision is needed"; ED-NEW-dextra-as11-12): the digest, the
// Suggestions lane and their card never reach the toast pipeline. The other tests prove the behaviour (no decision card is raised, which is the only thing that toasts); this
// one keeps it that way in the source: no file of the digest names the notification pipeline, a toast request, or the tray's balloon.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-12")]
public sealed class DigestNoToastTests
{
    private static readonly string[] Words = ["Toast", "Notify(", "ToastRequest", "NotificationChannel", "ShowBalloon", "NotificationService"];

    private static string AssistantFolder([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "src", "Marrow.App", "Assistant"));

    private static string[] DigestFiles() =>
    [
        .. Directory.EnumerateFiles(AssistantFolder(), "Digest*.cs"),
        .. Directory.EnumerateFiles(Path.Combine(AssistantFolder(), "Views"), "Digest*.cs"),
    ];

    [Fact]
    public void The_source_of_the_digest_names_no_toast_and_no_notification()
    {
        var files = DigestFiles();
        Assert.True(files.Length >= 6, "the digest files were not found where the test looks");
        var found = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) || lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;
                foreach (var word in Words)
                    if (lines[i].Contains(word, StringComparison.Ordinal)) found.Add($"{Path.GetFileName(file)}:{i + 1}: {word}");
            }
        }
        Assert.Empty(found);
    }
}
