// PORT-ONLY: Tactus (ED-NEW-pcctl-41; DESIGN.md 2.4, 3.4). Screenshots for a client whose MCP bridge turns image parts into
// "[image]" (Pi, Command Code): the server saves them as JPEG files in the chat's folder and names each path in the text
// instead of sending image parts; the folder keeps only the newest files; a model without vision gets neither; and every
// other client still gets image parts. A scratch folder, no provider.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-41")]
public sealed class PcImageFilesTests
{
    private static PcResult Shot(string obs, int crops = 0) => PcResult.Success(body: "a window", image: new PcImage([0xFF, 0xD8, 1, 2, 0xFF, 0xD9], 4, 3, obs)) with
    {
        Details = [.. Enumerable.Range(1, crops).Select(i => new PcImage([0xFF, 0xD8, (byte)i, 0xFF, 0xD9], 2, 2, obs))],
    };

    [Fact]
    public void A_folder_client_gets_files_named_in_the_text_and_no_image_parts()
    {
        using var folder = new PcTestFolder("pc-images");
        var chat = Path.Combine(folder.Path, "chat-a");

        var content = PcControlMcpServer.Content(Shot("o:17", crops: 1), vision: true, imageFolder: chat);

        var parts = content["content"]!.AsArray();
        var text = Assert.Single(parts)!["text"]!.GetValue<string>();
        var files = Directory.GetFiles(chat, "*.jpg");
        Assert.Equal(2, files.Length);
        foreach (var file in files) Assert.Contains("screenshot o:17 saved: " + file + " ", text, StringComparison.Ordinal);
        Assert.Contains(files, f => File.ReadAllBytes(f).SequenceEqual(new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 }));
        Assert.StartsWith(Shot("o:17").Render(), text, StringComparison.Ordinal);
        Assert.False(content["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Other_clients_get_image_parts_and_a_text_only_model_gets_neither()
    {
        using var folder = new PcTestFolder("pc-images");
        var chat = Path.Combine(folder.Path, "chat-b");

        var parts = PcControlMcpServer.Content(Shot("o:3", crops: 2), vision: true)["content"]!.AsArray();
        var blind = PcControlMcpServer.Content(Shot("o:4"), vision: false, imageFolder: chat)["content"]!.AsArray();

        Assert.Equal(new[] { "text", "image", "image", "image" }, parts.Select(p => p!["type"]!.GetValue<string>()));
        Assert.Equal("image/jpeg", parts[1]!["mimeType"]!.GetValue<string>());
        Assert.Single(blind);
        Assert.DoesNotContain("saved:", blind[0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(chat));
    }

    [Fact]
    public void The_chat_folder_keeps_only_the_newest_files()
    {
        using var folder = new PcTestFolder("pc-images");
        var chat = Path.Combine(folder.Path, "chat-c");
        var first = PcImageFiles.Save(chat, [new PcImage([1], 1, 1, "o:1")]);
        File.SetLastWriteTimeUtc(first[0].Path, DateTime.UtcNow.AddMinutes(-10));

        for (var i = 2; i <= PcProviderWiring.KeptImageFiles + 1; i++) PcImageFiles.Save(chat, [new PcImage([(byte)i], 1, 1, "o:" + i)]);

        Assert.Equal(PcProviderWiring.KeptImageFiles, Directory.GetFiles(chat, "*.jpg").Length);
        Assert.False(File.Exists(first[0].Path));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_saves_nothing_and_never_throws()
    {
        using var folder = new PcTestFolder("pc-images");
        var blocker = Path.Combine(folder.Path, "not-a-folder");
        File.WriteAllBytes(blocker, [0]);

        var saved = PcImageFiles.Save(Path.Combine(blocker, "chat"), [new PcImage([1], 1, 1, "o:1")]);
        var content = PcControlMcpServer.Content(Shot("o:9"), vision: true, imageFolder: Path.Combine(blocker, "chat"));

        Assert.Empty(saved);
        var parts = content["content"]!.AsArray();
        Assert.Single(parts);
        Assert.Equal(Shot("o:9").Render(), parts[0]!["text"]!.GetValue<string>());
    }
}
