using System.Text;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Voice;

// VT-14 (voice-SPEC V-2.4, V-2.5; ED of VT-13 and VT-5: "VT-14 binds CaptureSeams.Host and the app's start-up composition"): the production parts behind the voice seams, and that nothing
// in a test run is bound to them. Nothing here opens a device, reads a model or reaches the network.
public sealed class VoiceCompositionTests
{
    private static string Folder(string name)
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        var folder = Path.Combine(data, "VoiceCompositionTests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: a test run is bound to nothing: every default fails closed, whatever the unit tests above bind for their own flow
    public void Every_voice_seam_fails_closed_until_the_start_up_composition_binds_it()
    {
        Assert.IsType<FailClosedCaptureHost>(CaptureSeams.Host.Current);
        Assert.IsType<FailClosedMicrophoneAccess>(VoiceSeams.Microphone.Current);
        Assert.IsType<FailClosedAudioCapture>(VoiceSeams.Capture.Current);
        Assert.IsType<FailClosedHardwareProbe>(HardwareProbeSeams.Probe.Current);
        Assert.IsType<FailClosedSpeechModelBackend>(SpeechModelSeams.Backend.Current);
        Assert.IsType<NoSpeechSelfTest>(SpeechModelSeams.SelfTest.Current);
        Assert.Throws<SeamNotBoundException>(() => VoiceSeams.Capture.Current.Start(null!));
        Assert.Throws<SeamNotBoundException>(() => VoiceSeams.Microphone.Current.Status());
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // VT-5, VT-13: the endpoint seam is NAudio's, the privacy gate and the microphone are built over that same host
    public void The_production_parts_are_NAudios_host_the_privacy_gate_and_WASAPI_over_one_host()
    {
        var parts = VoiceComposition.Build(Folder("parts"), AppContext.BaseDirectory, () => null, () => null);
        Assert.IsType<NAudioCaptureHost>(parts.Host);
        Assert.IsType<Marrow.App.Port.Voice.MicrophoneAccess>(parts.Microphone);
        Assert.IsType<WasapiCapture>(parts.Capture);
        Assert.IsType<HardwareProbe>(parts.Probe);
        Assert.IsType<StoreSpeechModelBackend>(parts.Models);
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // the composition opens no model folder until the model list first asks
    public void Building_the_parts_creates_no_folder()
    {
        var root = Folder("lazy");
        _ = VoiceComposition.Build(root, AppContext.BaseDirectory, () => null, () => null);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // the model list asks the store: nothing installed, nothing in use, and the folder is the data root's models\whisper
    public void The_model_backend_is_the_store_on_the_data_root()
    {
        var root = Folder("store");
        var parts = VoiceComposition.Build(root, AppContext.BaseDirectory, () => null, () => "tiny-q5_1");
        var tiny = ModelCatalog.Find("tiny-q5_1")!;
        Assert.Equal(ModelInstallKind.NotInstalled, parts.Models.State(tiny).Kind);
        Assert.Equal(0, parts.Models.InstalledBytes);
        Assert.Equal(ModelRemoveResult.NotInstalled, parts.Models.Remove(tiny));
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // VT-5: the capture reads the Microphone row's stored pick each time it starts
    public void The_stored_pick_is_read_from_the_settings_store_each_time()
    {
        using var runtime = new TestRuntime();
        Assert.Null(VoiceComposition.StoredTyping().Mic);
        runtime.Defaults.Set(VoiceTypingSettings.StoreKey, Encoding.UTF8.GetBytes("""{"engine":"local","lang":null,"model":"tiny-q5_1","mic":"{0.0.1.00000000}.{abc}"}"""));
        Assert.Equal("{0.0.1.00000000}.{abc}", VoiceComposition.StoredTyping().Mic);
        Assert.Equal("tiny-q5_1", VoiceComposition.StoredTyping().Model);
        runtime.Defaults.Set(VoiceTypingSettings.StoreKey, Encoding.UTF8.GetBytes("""{"engine":"local","lang":null,"model":null}"""));
        Assert.Null(VoiceComposition.StoredTyping().Mic);
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // the voice assets are looked up in the app folder where the Services csproj copies them
    public void The_bundled_assets_are_looked_up_in_the_app_folders_voice_assets()
    {
        Assert.Equal(Path.Combine("app", "Port", "Voice", "Assets"), VoiceComposition.AssetsFolder("app"));
        Assert.True(File.Exists(Path.Combine(VoiceComposition.AssetsFolder(AppContext.BaseDirectory), "ggml-silero-v6.2.0.bin")));
    }

    [Fact]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // the composition is called from the application's AfterSetup, after the storage roots are bound (Program.cs binds them first)
    public void The_application_installs_the_composition_after_setup()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Marrow.App", "Port", "MarrowApplication.cs"));
        if (!File.Exists(root)) root = FindFile("MarrowApplication.cs");
        var text = File.ReadAllText(root);
        // a call on a line of its own that is not a comment: a commented-out call must not pass
        var call = System.Text.RegularExpressions.Regex.Match(text, @"^[ 	]*VoiceComposition\.Install\(\);", System.Text.RegularExpressions.RegexOptions.Multiline);
        var before = System.Text.RegularExpressions.Regex.Match(text, @"^[ 	]*SystemReduceMotion\.Install\(\);", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(call.Success, "MarrowApplication does not call VoiceComposition.Install();");
        Assert.True(before.Success && before.Index < call.Index);
        var program = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(root))!, "Program.cs"));
        Assert.True(program.IndexOf("StoragePaths.Locations.SetProcessDefault", StringComparison.Ordinal) < program.IndexOf("BuildAvaloniaApp", StringComparison.Ordinal));
    }

    private static string FindFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Marrow.App", "Port", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
