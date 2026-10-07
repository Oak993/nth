using Marrow.Core.Port;
using System.Text;

namespace Marrow.Services.Port.MultiAccount;

public sealed record FullWorkingContext(string Diff, ValueList<string> ChangedFiles)
{
    public static async Task<FullWorkingContext> ReadAsync(string directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var git = new Marrow.Services.Git.Git(directory);
        if (!await git.IsRepository().WaitAsync(ct)) return new("", ValueList<string>.Empty);
        var root = Path.GetFullPath(await git.RepositoryRoot().WaitAsync(ct));
        var committed = await git.HasCommits().WaitAsync(ct);
        var patch = committed
            ? await git.Output(["diff", "--no-ext-diff", "--no-textconv", "--binary", "HEAD", "--"]).WaitAsync(ct)
            : await git.Output(["diff", "--cached", "--no-ext-diff", "--no-textconv", "--binary", "--"]).WaitAsync(ct)
                + await git.Output(["diff", "--no-ext-diff", "--no-textconv", "--binary", "--"]).WaitAsync(ct);
        var names = committed ? await git.Output(["diff", "--name-only", "-z", "HEAD", "--"]).WaitAsync(ct)
            : await git.Output(["diff", "--cached", "--name-only", "-z", "--"]).WaitAsync(ct)
                + await git.Output(["diff", "--name-only", "-z", "--"]).WaitAsync(ct);
        var untracked = (await git.Output(["ls-files", "--full-name", "--others", "--exclude-standard", "-z"]).WaitAsync(ct)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var diff = new StringBuilder(patch);
        foreach (var relative in untracked)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("An untracked path is outside the conversation folder.");
            diff.Append("\nUntracked file: ").Append(relative).Append('\n');
            var info = new FileInfo(path);
            if (info.LinkTarget is { } target) { diff.Append("Symbolic link: ").Append(target).Append('\n'); continue; }
            var bytes = await File.ReadAllBytesAsync(path, ct);
            try
            {
                if (bytes.Contains((byte)0)) throw new DecoderFallbackException();
                diff.Append(new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (DecoderFallbackException) { diff.Append("Binary content (base64):\n").Append(Convert.ToBase64String(bytes)); }
            diff.Append('\n');
        }
        return new(diff.ToString(), new(names.Split('\0', StringSplitOptions.RemoveEmptyEntries).Concat(untracked).Distinct(StringComparer.Ordinal)));
    }
}
