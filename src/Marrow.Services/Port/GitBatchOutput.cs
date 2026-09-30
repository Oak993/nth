// PORT-ONLY (Q2): retain a prefix of a real successful Git batch reply to prove the pinned
// positional-zip behavior (Git.swift:452-457). The default leaves every reply unchanged.
using Marrow.Core.Port;

namespace Marrow.Services.Port;

public static class GitBatchOutput
{
    public static Ambient<Func<string, IReadOnlyList<string>, string, int?>> RetainedCharacters { get; } =
        new((_, _, _) => null);

    public static string RetainPrefix(string reference, IReadOnlyList<string> paths, string output) =>
        RetainedCharacters.Current(reference, paths, output) is { } count
            ? output[..Math.Clamp(count, 0, output.Length)]
            : output;
}
