// PORT-ONLY, owner-ruled addition (OD-0092 and its amendment; first-release voice typing, VT-1): the embedded model manifest, 29
// files with the bytes and SHA-256 of voice-SPEC V-9.1 (from bench\scripts\hf_tree.json at the pinned commit). Nothing is trusted
// from the server (V-3.3). Generated from the spec's table by a script and checked against it by ModelCatalogTests.
namespace Marrow.Core.Port.Voice;

/// <summary>The size/quality version of a model file (V-9.1): q5 is Compact, q8 Balanced, f16 Full.</summary>
public enum ModelVersion { Compact, Balanced, Full }

/// <summary>One Whisper model file of the manifest (V-9.1): its id (the file stem), model, version, language scope, size and hash.</summary>
public sealed record ModelFile(string Id, string Model, ModelVersion Version, bool EnglishOnly, long Bytes, string Sha256)
{
    /// <summary>The file on disk and on the server: ggml-&lt;id&gt;.bin.</summary>
    public string FileName => "ggml-" + Id + ".bin";

    /// <summary>The pinned download address (V-3.3).</summary>
    public string Url => "https://huggingface.co/ggerganov/whisper.cpp/resolve/" + ModelCatalog.HuggingFaceCommit + "/" + FileName;
}

internal static class ModelManifest
{
    internal static readonly ModelFile[] Files =
    [
        new("tiny-q5_1", "Tiny", ModelVersion.Compact, false, 32152673, "818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7"),
        new("tiny.en-q5_1", "Tiny", ModelVersion.Compact, true, 32166155, "c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b"),
        new("tiny-q8_0", "Tiny", ModelVersion.Balanced, false, 43537433, "c2085835d3f50733e2ff6e4b41ae8a2b8d8110461e18821b09a15c40c42d1cca"),
        new("tiny.en-q8_0", "Tiny", ModelVersion.Balanced, true, 43550795, "5bc2b3860aa151a4c6e7bb095e1fcce7cf12c7b020ca08dcec0c6d018bb7dd94"),
        new("tiny", "Tiny", ModelVersion.Full, false, 77691713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21"),
        new("tiny.en", "Tiny", ModelVersion.Full, true, 77704715, "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f"),
        new("base-q5_1", "Base", ModelVersion.Compact, false, 59707625, "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898"),
        new("base.en-q5_1", "Base", ModelVersion.Compact, true, 59721011, "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f"),
        new("base-q8_0", "Base", ModelVersion.Balanced, false, 81768585, "c577b9a86e7e048a0b7eada054f4dd79a56bbfa911fbdacf900ac5b567cbb7d9"),
        new("base.en-q8_0", "Base", ModelVersion.Balanced, true, 81781811, "a4d4a0768075e13cfd7e19df3ae2dbc4a68d37d36a7dad45e8410c9a34f8c87e"),
        new("base", "Base", ModelVersion.Full, false, 147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"),
        new("base.en", "Base", ModelVersion.Full, true, 147964211, "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002"),
        new("small-q5_1", "Small", ModelVersion.Compact, false, 190085487, "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb"),
        new("small.en-q5_1", "Small", ModelVersion.Compact, true, 190098681, "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30"),
        new("small-q8_0", "Small", ModelVersion.Balanced, false, 264464607, "49c8fb02b65e6049d5fa6c04f81f53b867b5ec9540406812c643f177317f779f"),
        new("small.en-q8_0", "Small", ModelVersion.Balanced, true, 264477561, "67a179f608ea6114bd3fdb9060e762b588a3fb3bd00c4387971be4d177958067"),
        new("small", "Small", ModelVersion.Full, false, 487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
        new("small.en", "Small", ModelVersion.Full, true, 487614201, "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d"),
        new("medium-q5_0", "Medium", ModelVersion.Compact, false, 539212467, "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f"),
        new("medium.en-q5_0", "Medium", ModelVersion.Compact, true, 539225533, "76733e26ad8fe1c7a5bf7531a9d41917b2adc0f20f2e4f5531688a8c6cd88eb0"),
        new("medium-q8_0", "Medium", ModelVersion.Balanced, false, 823369779, "42a1ffcbe4167d224232443396968db4d02d4e8e87e213d3ee2e03095dea6502"),
        new("medium.en-q8_0", "Medium", ModelVersion.Balanced, true, 823382461, "43fa2cd084de5a04399a896a9a7a786064e221365c01700cea4666005218f11c"),
        new("medium", "Medium", ModelVersion.Full, false, 1533763059, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208"),
        new("medium.en", "Medium", ModelVersion.Full, true, 1533774781, "cc37e93478338ec7700281a7ac30a10128929eb8f427dda2e865faa8f6da4356"),
        new("large-v3-turbo-q5_0", "Large v3 Turbo", ModelVersion.Compact, false, 574041195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
        new("large-v3-turbo-q8_0", "Large v3 Turbo", ModelVersion.Balanced, false, 874188075, "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1"),
        new("large-v3-turbo", "Large v3 Turbo", ModelVersion.Full, false, 1624555275, "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69"),
        new("large-v3-q5_0", "Large v3", ModelVersion.Compact, false, 1081140203, "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1"),
        new("large-v3", "Large v3", ModelVersion.Full, false, 3095033483, "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"),
    ];
}
