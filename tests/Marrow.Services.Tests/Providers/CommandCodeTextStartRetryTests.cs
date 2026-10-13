using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: final security gate N2 (ED-NEW-secfinal-23; ED-NEW-dextra-open-12). A private Command Code answer's folder (the question, the
// transcript) is removed when the answer ends. A removal that failed leaves a durable cleanup record, and the retry of the leftovers ran only
// inside the next Command Code answer, so an owner who moved to another brain left that folder in %TEMP% for good. The same retry now also runs at
// Dextra start, on a worker thread: it removes what an earlier process left, skips a live answer's lease, and keeps a record of a newer version.
[Trait("Decision", "ED-NEW-secfinal-23")]
public sealed class CommandCodeTextStartRetryTests
{
    private static string Fresh()
    {
        var root = Path.Combine(TestDataDirectory.Read()!, "cc-start-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static (string Folder, string Record) Leftover(string root, string text = "SECRET-SENTINEL")
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(root, "commandcode-text-" + id);
        var record = Path.Combine(root, ".commandcode-text-" + id + ".cleanup-v1");
        Directory.CreateDirectory(Path.Combine(folder, "transcripts"));
        File.WriteAllText(Path.Combine(folder, "transcripts", "question.txt"), text);
        File.WriteAllBytes(record, []); // Durable intent with no live lease: the process that made it ended before it recorded the outcome.
        return (folder, record);
    }

    [Fact]
    public void A_start_removes_what_an_earlier_process_left_and_keeps_unknown_versions_and_unregistered_folders()
    {
        var root = Fresh();
        try
        {
            var (folder, record) = Leftover(root);
            var unknown = Path.Combine(root, ".commandcode-text-" + Guid.NewGuid().ToString("N") + ".cleanup-v2");
            var unregistered = Path.Combine(root, "commandcode-text-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(unknown, "NEWER-SENTINEL");
            Directory.CreateDirectory(unregistered);

            CommandCodeTextHead.RetryLeftoverCleanup(root);

            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(record));
            Assert.Equal("NEWER-SENTINEL", File.ReadAllText(unknown));
            Assert.True(Directory.Exists(unregistered));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_start_skips_the_folder_of_an_answer_that_is_still_running_and_leaves_it_to_that_answer()
    {
        var root = Fresh();
        try
        {
            var live = CommandCodeTextFiles.Begin(root, Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(Path.Combine(live.Folder, "question.txt"), "LIVE-SENTINEL");
                var (folder, record) = Leftover(root);

                CommandCodeTextHead.RetryLeftoverCleanup(root);

                Assert.Equal("LIVE-SENTINEL", File.ReadAllText(Path.Combine(live.Folder, "question.txt")));
                Assert.False(Directory.Exists(folder));
                Assert.False(File.Exists(record));
            }
            finally { live.Dispose(); }
            Assert.False(Directory.Exists(live.Folder));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_removal_that_failed_is_tried_again_at_the_next_start_without_any_answer_in_between()
    {
        var root = Fresh();
        try
        {
            var (folder, record) = Leftover(root);
            var question = Path.Combine(folder, "transcripts", "question.txt");

            using (new FileStream(question, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CommandCodeTextHead.RetryLeftoverCleanup(root); // The child's file is still held open: removal fails and the cleanup entry stays.
                Assert.True(File.Exists(record));
                Assert.True(File.Exists(question));
            }

            CommandCodeTextHead.RetryLeftoverCleanup(root);

            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(record));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_async_start_retry_does_the_same_work_and_a_missing_folder_is_left_missing()
    {
        var root = Fresh();
        var missing = Path.Combine(root, "not-made");
        try
        {
            var (folder, record) = Leftover(root);

            await CommandCodeTextHead.RetryLeftoverCleanupAsync(root);
            await CommandCodeTextHead.RetryLeftoverCleanupAsync(missing);

            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(record));
            Assert.False(Directory.Exists(missing));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
