using System.Text.Json;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.Core.Support;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Voice.Models;

// VT-7 (voice-SPEC V-9.3, V-9.4, V-T44; OD-0099 Q-V-13 B): the store's folder, installed.json and its trust rules, and Remove.
[Trait("Spec", "V-T44")]
public sealed class ModelStoreTests : IDisposable
{
    private static readonly FakeArtifact Tiny = FakeArtifact.Pattern(5_000, seed: 71);
    private static readonly FakeArtifact Base = FakeArtifact.Pattern(7_000, seed: 72);
    private readonly TestRuntime runtime = new();
    private readonly string root;
    private string? selected;

    public ModelStoreTests()
    {
        var data = Environment.GetEnvironmentVariable(TestDataDirectory.VariableName) ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        root = Path.Combine(OperatingSystem.IsWindows() ? TestDataDirectory.Read()! : data, "VoiceStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose() => runtime.Dispose();

    private ModelStore Store() => new(root, () => selected, runtime.Time);

    private static ModelFile File1 => VoiceModelFixtures.Model(Tiny, "tiny-q5_1");

    private static ModelFile File2 => VoiceModelFixtures.Model(Base, "base-q5_1");

    private static void Put(ModelStore store, ModelFile file, FakeArtifact artifact) => System.IO.File.WriteAllBytes(store.PathFor(file), artifact.Bytes);

    private ModelStore Installed(params (ModelFile File, FakeArtifact Artifact)[] files)
    {
        var store = Store();
        Directory.CreateDirectory(store.Folder);
        foreach (var (file, artifact) in files)
        {
            Put(store, file, artifact);
            store.RecordInstalled(file);
        }
        return store;
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void The_default_store_lives_in_models_whisper_under_the_data_root_the_tests_set()
    {
        using var scope = StoragePaths.Locations.Override(StoragePathsLocations.Create(root));
        var store = ModelStore.CreateDefault(() => null);
        Assert.Equal(Path.Combine(root, "models", "whisper"), store.Folder);
        Assert.Equal(Path.Combine(root, "models", "whisper", "ggml-tiny-q5_1.bin"), store.PathFor(File1));
        Assert.Equal(Path.Combine(root, "models", "whisper", "installed.json"), store.InstalledJsonPath);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void State_is_NotInstalled_Installed_or_InUse_by_the_files_present_and_the_selection()
    {
        var store = Store();
        Assert.Equal(ModelInstallKind.NotInstalled, store.State(File1).Kind);
        Directory.CreateDirectory(store.Folder);
        Put(store, File1, Tiny);
        Assert.Equal(ModelInstallKind.Installed, store.State(File1).Kind);
        selected = File1.Id;
        Assert.Equal(ModelInstallKind.InUse, store.State(File1).Kind);
        Assert.Equal(ModelInstallKind.NotInstalled, store.State(File2).Kind);
        System.IO.File.WriteAllBytes(store.PathFor(File1), new byte[10]); // the wrong size is not an installed model
        Assert.Equal(ModelInstallKind.NotInstalled, store.State(File1).Kind);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void Opening_the_store_and_asking_its_state_reads_no_model_and_deletes_nothing()
    {
        var first = Installed((File1, Tiny));
        var wrong = (byte[])Tiny.Bytes.Clone();
        wrong[10] ^= 0xFF;
        System.IO.File.WriteAllBytes(first.PathFor(File1), wrong);
        System.IO.File.SetLastWriteTimeUtc(first.PathFor(File1), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var store = Store();
        var state = store.State(File1);
        _ = store.InstalledBytes;

        Assert.Equal(ModelInstallKind.Installed, state.Kind);
        Assert.Equal(wrong, System.IO.File.ReadAllBytes(store.PathFor(File1))); // nothing was hashed or repaired at start-up
        Assert.Single(store.Records);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void Installed_json_records_id_bytes_hash_verified_time_and_file_time()
    {
        var store = Installed((File1, Tiny));

        var record = Assert.Single(store.Records);
        Assert.Equal((File1.Id, 5_000L, Tiny.Sha256Hex), (record.Id, record.Bytes, record.Sha256));
        Assert.Equal(runtime.Time.GetUtcNow().UtcDateTime, record.VerifiedUtc);
        Assert.Equal(System.IO.File.GetLastWriteTimeUtc(store.PathFor(File1)), record.FileTimeUtc);
        using var json = JsonDocument.Parse(System.IO.File.ReadAllBytes(store.InstalledJsonPath));
        var entry = json.RootElement.GetProperty("models")[0];
        Assert.Equal(File1.Id, entry.GetProperty("id").GetString());
        Assert.Equal(5_000, entry.GetProperty("bytes").GetInt64());
        Assert.Equal(Tiny.Sha256Hex, entry.GetProperty("sha256").GetString());
        Assert.True(entry.TryGetProperty("verified", out _) && entry.TryGetProperty("fileTime", out _));
        Assert.Equal(record, Store().Records.Single()); // and it reads back
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_file_whose_size_and_time_match_its_record_is_trusted_without_being_read()
    {
        var store = Installed((File1, Tiny));
        var time = System.IO.File.GetLastWriteTimeUtc(store.PathFor(File1));
        var tampered = (byte[])Tiny.Bytes.Clone();
        tampered[1] ^= 0xFF; // same size, same time: only reading it could notice
        System.IO.File.WriteAllBytes(store.PathFor(File1), tampered);
        System.IO.File.SetLastWriteTimeUtc(store.PathFor(File1), time);

        Assert.Equal(ModelVerification.Trusted, await store.VerifyForLoad(File1, CancellationToken.None));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_changed_time_means_a_re_hash_and_wrong_bytes_are_Corrupt_and_removed()
    {
        var store = Installed((File1, Tiny));
        var tampered = (byte[])Tiny.Bytes.Clone();
        tampered[1] ^= 0xFF;
        System.IO.File.WriteAllBytes(store.PathFor(File1), tampered);
        System.IO.File.SetLastWriteTimeUtc(store.PathFor(File1), new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(ModelVerification.Corrupt, await store.VerifyForLoad(File1, CancellationToken.None));
        Assert.False(System.IO.File.Exists(store.PathFor(File1)));
        Assert.Empty(store.Records);
        Assert.Equal(ModelInstallKind.NotInstalled, store.State(File1).Kind);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_changed_time_with_the_right_bytes_is_re_hashed_re_recorded_and_trusted_next_time()
    {
        var store = Installed((File1, Tiny));
        var later = new DateTime(2022, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(store.PathFor(File1), later);

        Assert.Equal(ModelVerification.Rehashed, await store.VerifyForLoad(File1, CancellationToken.None));
        Assert.Equal(later, store.Records.Single().FileTimeUtc);
        Assert.Equal(ModelVerification.Trusted, await store.VerifyForLoad(File1, CancellationToken.None));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_changed_size_is_re_hashed_and_a_file_with_no_record_is_adopted_when_its_hash_is_right()
    {
        var store = Installed((File1, Tiny));
        System.IO.File.AppendAllText(store.PathFor(File1), "x");
        Assert.Equal(ModelVerification.Corrupt, await store.VerifyForLoad(File1, CancellationToken.None));

        Put(store, File2, Base); // copied in by hand: no record
        Assert.Equal(ModelVerification.Rehashed, await store.VerifyForLoad(File2, CancellationToken.None));
        Assert.Equal(File2.Id, store.Records.Single().Id);
        Assert.Equal(ModelVerification.Trusted, await store.VerifyForLoad(File2, CancellationToken.None));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_record_never_vouches_for_bytes_the_manifest_does_not_pin()
    {
        var store = Installed((File1, Tiny));
        var other = File1 with { Sha256 = new string('a', 64) }; // the manifest says something else
        Assert.Equal(ModelVerification.Corrupt, await store.VerifyForLoad(other, CancellationToken.None));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_missing_file_is_Missing_and_its_stale_record_is_dropped()
    {
        var store = Installed((File1, Tiny));
        System.IO.File.Delete(store.PathFor(File1));

        Assert.Equal(ModelVerification.Missing, await store.VerifyForLoad(File1, CancellationToken.None));
        Assert.Empty(store.Records);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Verifying_honours_cancellation_and_an_unreadable_installed_json_is_an_empty_one()
    {
        var store = Installed((File1, Tiny));
        System.IO.File.WriteAllText(store.InstalledJsonPath, "{ not json");
        var reopened = Store();
        Assert.Empty(reopened.Records);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reopened.VerifyForLoad(File1, cancelled.Token));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void Remove_refuses_the_model_in_use_and_removes_any_other_with_its_record()
    {
        var store = Installed((File1, Tiny), (File2, Base));
        selected = File1.Id;

        Assert.Equal(ModelRemoveResult.InUse, store.Remove(File1));
        Assert.True(System.IO.File.Exists(store.PathFor(File1)));
        Assert.Equal(2, store.Records.Count);

        Assert.Equal(ModelRemoveResult.Removed, store.Remove(File2));
        Assert.False(System.IO.File.Exists(store.PathFor(File2)));
        Assert.Equal([File1.Id], store.Records.Select(r => r.Id));
        Assert.Equal(ModelRemoveResult.NotInstalled, store.Remove(File2));
        Assert.Equal(ModelInstallKind.InUse, store.State(File1).Kind);

        selected = null;
        Assert.Equal(ModelRemoveResult.Removed, store.Remove(File1));
        Assert.Empty(store.Records);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void Remove_of_a_file_another_handle_holds_open_is_refused_as_in_use_and_keeps_the_file_and_its_record()
    {
        // WINDOWS-ONLY in effect (ED-305 (7)): Windows will not delete a file a handle holds without delete sharing (a load or a hash in
        // progress, a scanner); elsewhere the delete succeeds, so there is nothing to refuse.
        if (!OperatingSystem.IsWindows()) return;
        var store = Installed((File1, Tiny), (File2, Base));

        using (new FileStream(store.PathFor(File2), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(ModelRemoveResult.InUse, store.Remove(File2));
            Assert.True(System.IO.File.Exists(store.PathFor(File2)));
            Assert.Equal(2, store.Records.Count);
            Assert.Equal(2, new ModelStore(root, () => selected, runtime.Time).Records.Count);
        }

        Assert.Equal(ModelRemoveResult.Removed, store.Remove(File2));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void The_installed_total_is_the_sum_of_the_installed_files()
    {
        var store = Installed((File1, Tiny), (File2, Base));
        Assert.Equal(12_000, store.InstalledBytes);
        store.Remove(File2);
        Assert.Equal(5_000, store.InstalledBytes);
    }
}
