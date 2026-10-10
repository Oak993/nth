using System.Text;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port;

// F.SettingsStore (IMPLEMENTING.md 4.3, "Production settings store"; ED-079; R-SET-1). The production store behind
// UserDefaults.Standard: a per-user JSON file under the "Marrow Code" data folder (OD-0008 C7).
//
// Expected values come from the Swift call sites the store stands behind (App/AppSettings.swift,
// App/UpdateChecker.swift, B2 1fbf81e) and from UserDefaults' own contract as those sites rely on it:
// - object(forKey:) is nil for an absent key, so `as? Bool ?? true` gives true (AppSettings.swift:708) while a
//   stored false gives false; bool(forKey:) of an absent key is false (:719);
// - set(nil, forKey:) removes the key (:516, `lastHydraPairID?.uuidString`), as removeObject(forKey:) does (:265);
// - a stored value keeps its property-list type across launches: a Bool stays a Bool, an Int an Int (appZoom, :254,
//   read `as? Int` at :718), a Double a Double (backdropOpacity, :297, read `as? Double` at :722), a [CGFloat] pair an
//   array (:263, read `as? [Double]`), a [String: String] a dictionary (:497, read at :738), a [String] a string array
//   (:501, read at :739), a Date a Date (UpdateChecker.swift:238, read `as? Date` at :118), Data a Data;
// - UserDefaults copies what it stores (property-list values are values), and refuses a non-property-list object
//   ("Attempt to insert non-property list object"), which the port models as a trap.
// The file format is port-only and approved by ED-156: the tags are the XML
// property-list element names, keys are written in ordinal order, and every write is one atomic replace through
// FileOps (PA-0015, R-FS-1).
public sealed class SettingsStoreTests
{
    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void An_absent_key_reads_nil_while_a_stored_false_or_zero_reads_back_as_present()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);

        // AppSettings.swift:708: notifyWhenFinished = defaults.object(forKey: Key.notify) as? Bool ?? true
        Assert.Null(store.Object("notifyWhenFinished"));
        Assert.True(Plist.AsBool(store.Object("notifyWhenFinished")) ?? true);
        // AppSettings.swift:719: sidebarActivityView = defaults.bool(forKey: Key.sidebarActivityView)
        Assert.False(store.Bool("sidebarActivityView"));
        // AppSettings.swift:762: hydraMaxHeads = HydraPair.clampedCap(defaults.object(forKey: Key.hydraMaxHeads) as? Int)
        Assert.Null(Plist.AsInt(store.Object("hydraMaxHeads")));

        store.Set("notifyWhenFinished", false);
        store.Set("hydraMaxHeads", 0L);

        Assert.False(Plist.AsBool(store.Object("notifyWhenFinished")) ?? true);
        Assert.Equal(0L, Plist.AsInt(store.Object("hydraMaxHeads")));

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        Assert.False(Plist.AsBool(reopened.Object("notifyWhenFinished")) ?? true);
        Assert.Equal(0L, Plist.AsInt(reopened.Object("hydraMaxHeads")));
        Assert.Null(reopened.Object("sidebarActivityView"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void A_store_on_a_missing_file_is_empty_and_creates_nothing_until_the_first_write()
    {
        using var folder = new SettingsScratchFolder();
        var dataFolder = folder.PathOf("Nth");
        var path = Path.Combine(dataFolder, "UserDefaults.json");

        var store = new SettingsStore(path);
        Assert.Null(store.Object("appTheme"));
        Assert.False(Directory.Exists(dataFolder));

        // The data folder does not exist on a first launch; the first write makes it (AppSettings.swift:288).
        store.Set("appTheme", "dark");
        Assert.True(store.Synchronize());
        Assert.True(File.Exists(path));
        Assert.Equal("dark", new SettingsStore(path).String("appTheme"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Each_value_survives_a_relaunch_with_its_property_list_type()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var checkedAt = new DateTimeOffset(2026, 9, 23, 10, 15, 30, TimeSpan.Zero).AddTicks(1234567);
        var store = new SettingsStore(path);
        store.Set("showReasoning", true);                          // AppSettings.swift:231
        store.Set("appZoom", 100L);                                // :254
        store.Set("backdropOpacity", 1.0);                         // :297, a Double even when whole
        store.Set("terminalHeight", 260.5);                        // :310
        store.Set("commitInstructions", "Don\u2019t \u2026 \u2014 \u00dcber \U0001F600"); // :306
        store.Set("floatingPanelSize", new object[] { 1320.0, 860.0 }); // :263
        store.Set("providerBinaryPaths", new Dictionary<string, object> { ["codex"] = "C:\\bin\\codex.exe", ["claude"] = "" }); // :497
        store.Set("disabledProviders", new object[] { "grok", "pi" }); // :501
        store.Set("updateChecker_lastChecked", checkedAt);         // UpdateChecker.swift:238
        store.Set("modelList", new byte[] { 0x5B, 0x00, 0xFF, 0x5D }); // AppSettings.swift:860-861, Data

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);

        Assert.IsType<bool>(reopened.Object("showReasoning"));
        Assert.True((bool)reopened.Object("showReasoning")!);
        Assert.IsType<long>(reopened.Object("appZoom"));
        Assert.Equal(100L, (long)reopened.Object("appZoom")!);
        Assert.IsType<double>(reopened.Object("backdropOpacity"));
        Assert.Equal(1.0, (double)reopened.Object("backdropOpacity")!);
        Assert.Equal(260.5, (double)reopened.Object("terminalHeight")!);
        Assert.Equal(
            Encoding.UTF8.GetBytes("Don\u2019t \u2026 \u2014 \u00dcber \U0001F600"),
            Encoding.UTF8.GetBytes(reopened.String("commitInstructions")!));
        var panel = reopened.Array("floatingPanelSize");
        Assert.NotNull(panel);
        Assert.Equal(new object[] { 1320.0, 860.0 }, panel);
        Assert.All(panel!, item => Assert.IsType<double>(item));
        var paths = reopened.Dictionary("providerBinaryPaths");
        Assert.NotNull(paths);
        Assert.Equal(2, paths!.Count);
        Assert.Equal("C:\\bin\\codex.exe", paths["codex"]);
        Assert.Equal("", paths["claude"]);
        Assert.Equal(new[] { "grok", "pi" }, reopened.StringArray("disabledProviders"));
        var date = Assert.IsType<DateTimeOffset>(reopened.Object("updateChecker_lastChecked"));
        Assert.Equal(checkedAt.UtcTicks, date.UtcTicks);
        Assert.Equal(new byte[] { 0x5B, 0x00, 0xFF, 0x5D }, reopened.Data("modelList"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void The_type_conversions_read_the_stored_type_as_Foundation_bridges_it()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("hasSeenTour", 1L);          // an integer 1 bridges `as? Bool` to true
        store.Set("settledCollapsed", 2L);     // an integer 2 is not a Bool
        store.Set("appZoom", 125.0);           // a whole Double bridges `as? Int` (AppSettings.swift:718)
        store.Set("usagePanelHeight", 300L);   // an integer bridges `as? Double` (:691, read as? Double)
        store.Set("lastProjectID", 42L);       // string(forKey:) renders an integer as text
        store.Set("recentDownloadsPicker", true);

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        Assert.True(Plist.AsBool(reopened.Object("hasSeenTour")));
        Assert.True(reopened.Bool("hasSeenTour"));
        Assert.Null(Plist.AsBool(reopened.Object("settledCollapsed")));
        Assert.False(reopened.Bool("settledCollapsed"));
        Assert.Equal(125L, Plist.AsInt(reopened.Object("appZoom")));
        Assert.Equal(300.0, Plist.AsDouble(reopened.Object("usagePanelHeight")));
        Assert.Equal("42", reopened.String("lastProjectID"));
        Assert.True(reopened.Bool("recentDownloadsPicker"));
        Assert.Equal(1L, Plist.AsInt(reopened.Object("recentDownloadsPicker")));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Doubles_round_trip_bit_for_bit_including_negative_zero_and_non_finite_values()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        double[] values = [0.1, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, double.MaxValue, 1e-5];
        for (var i = 0; i < values.Length; i++) store.Set("real" + i, values[i]);

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        for (var i = 0; i < values.Length; i++)
        {
            var read = Assert.IsType<double>(reopened.Object("real" + i));
            Assert.Equal(BitConverter.DoubleToInt64Bits(values[i]), BitConverter.DoubleToInt64Bits(read));
        }
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Keys_are_kept_exactly_as_written_and_are_case_sensitive()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("appTheme", "dark");                    // AppSettings.swift:135, :288
        store.Set("apptheme", "light");
        store.Set("updateChecker_lastChecked", 1L);       // UpdateChecker.swift:79
        store.Set("MCP \"quoted\" key \u2019", "x");

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        Assert.Equal("dark", reopened.String("appTheme"));
        Assert.Equal("light", reopened.String("apptheme"));
        Assert.Null(reopened.Object("APPTHEME"));
        Assert.Equal(1L, reopened.Object("updateChecker_lastChecked"));
        Assert.Equal("x", reopened.String("MCP \"quoted\" key \u2019"));
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.Contains("\"appTheme\"", text, StringComparison.Ordinal);
        Assert.Contains("\"updateChecker_lastChecked\"", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Setting_nil_removes_the_key_as_set_nil_forKey_does()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("lastHydraPairID", "6F9619FF-8B86-D011-B42D-00C04FC964FF");
        Assert.True(store.Synchronize());
        Assert.NotNull(new SettingsStore(path).Object("lastHydraPairID"));

        // AppSettings.swift:516: defaults.set(lastHydraPairID?.uuidString, forKey: Key.lastHydraPair)
        store.Set("lastHydraPairID", null);

        Assert.Null(store.Object("lastHydraPairID"));
        Assert.True(store.Synchronize());
        Assert.Null(new SettingsStore(path).Object("lastHydraPairID"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void RemoveObject_removes_the_key_and_the_removal_survives_a_relaunch()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("floatingPanelSize", new object[] { 900.0, 700.0 });
        store.Set("lastProjectID", "6F9619FF-8B86-D011-B42D-00C04FC964FF");

        store.RemoveObject("floatingPanelSize");  // AppSettings.swift:265
        store.RemoveObject("neverStored");        // removing an absent key is not an error

        Assert.Null(store.Object("floatingPanelSize"));
        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        Assert.Null(reopened.Object("floatingPanelSize"));
        Assert.Null(reopened.Object("neverStored"));
        Assert.Equal("6F9619FF-8B86-D011-B42D-00C04FC964FF", reopened.String("lastProjectID"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Writes_apply_in_order_and_are_on_disk_once_Synchronize_returns()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);

        store.Set("appZoom", 90L);
        Assert.True(store.Synchronize());
        Assert.Equal(90L, new SettingsStore(path).Object("appZoom"));
        store.Set("appZoom", 110L);
        Assert.True(store.Synchronize());
        Assert.Equal(110L, new SettingsStore(path).Object("appZoom"));
        store.Set("appTheme", "dark");
        store.RemoveObject("appZoom");
        store.Set("appTheme", "light");
        store.Set("appZoom", 125L);
        store.Set("appTheme", null);

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        Assert.Equal(125L, reopened.Object("appZoom"));
        Assert.Null(reopened.Object("appTheme"));
        Assert.Equal(125L, store.Object("appZoom"));
        Assert.Null(store.Object("appTheme"));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void The_file_is_replaced_atomically_and_leaves_no_temporary_file_behind()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        for (var i = 0; i < 20; i++) store.Set("terminalHeight", 200.0 + i);

        Assert.True(store.Synchronize());
        Assert.Equal(new[] { "UserDefaults.json" }, Directory.EnumerateFileSystemEntries(folder.FullPath).Select(entry => Path.GetFileName(entry)).ToArray());
        Assert.Equal(219.0, new SettingsStore(path).Object("terminalHeight"));
    }

    [Fact]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void The_file_is_compact_JSON_with_property_list_tags_and_keys_in_ordinal_order()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("notifyWhenFinished", false);
        store.Set("appZoom", 100L);
        store.Set("backdropOpacity", 0.5);
        store.Set("appTheme", "dark");
        store.Set("Zed", new object[] { 1.0, "a" });
        store.Set("modelList", new byte[] { 1, 2, 3 });
        store.Set("updateChecker_lastChecked", new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(2)));
        store.Set("providerBinaryPaths", new Dictionary<string, object> { ["b"] = "2", ["a"] = "1" });
        store.Set("inf", double.PositiveInfinity);

        Assert.True(store.Synchronize());
        var expected =
            "{\"Zed\":{\"array\":[{\"real\":1},{\"string\":\"a\"}]}," +
            "\"appTheme\":{\"string\":\"dark\"}," +
            "\"appZoom\":{\"integer\":100}," +
            "\"backdropOpacity\":{\"real\":0.5}," +
            "\"inf\":{\"real\":\"+infinity\"}," +
            "\"modelList\":{\"data\":\"AQID\"}," +
            "\"notifyWhenFinished\":{\"bool\":false}," +
            "\"providerBinaryPaths\":{\"dict\":{\"a\":{\"string\":\"1\"},\"b\":{\"string\":\"2\"}}}," +
            "\"updateChecker_lastChecked\":{\"date\":\"2026-09-23T10:00:00.0000000+00:00\"}}";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Stored_values_are_copies_that_later_changes_to_the_argument_do_not_reach()
    {
        using var folder = new SettingsScratchFolder();
        var store = new SettingsStore(folder.PathOf("UserDefaults.json"));
        var bytes = new byte[] { 1, 2, 3 };
        var list = new List<object> { "grok" };
        var map = new Dictionary<string, object> { ["codex"] = "a" };
        store.Set("modelList", bytes);
        store.Set("disabledProviders", list);
        store.Set("providerBinaryPaths", map);

        bytes[0] = 9;
        list.Add("pi");
        map["claude"] = "b";
        store.Data("modelList")![1] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, store.Data("modelList"));
        Assert.Equal(new[] { "grok" }, store.StringArray("disabledProviders"));
        Assert.Single(store.Dictionary("providerBinaryPaths")!);
        Assert.True(store.Synchronize());
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Typed_collections_are_stored_as_property_list_arrays_and_dictionaries()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        // AppSettings.swift:497, :501, :263: [String: String], [String] and [CGFloat] values.
        store.Set("providerBinaryPaths", new Dictionary<string, string> { ["codex"] = "C:\\codex.exe" });
        store.Set("disabledProviders", new[] { "grok" });
        store.Set("floatingPanelSize", new[] { 1320.0, 860.0 });
        store.Set("hydraQueueHeads", 3); // an Int32 is an integer

        Assert.True(store.Synchronize());
        foreach (var s in new[] { store, new SettingsStore(path) })
        {
            Assert.Equal("C:\\codex.exe", s.Dictionary("providerBinaryPaths")!["codex"]);
            Assert.Equal(new[] { "grok" }, s.StringArray("disabledProviders"));
            Assert.Equal(new object[] { 1320.0, 860.0 }, s.Array("floatingPanelSize"));
            Assert.Equal(3L, Assert.IsType<long>(s.Object("hydraQueueHeads")));
        }
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void A_non_property_list_value_traps_and_stores_nothing()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);

        Assert.Throws<SwiftTrapException>(() => store.Set("lastProjectID", Guid.NewGuid()));
        Assert.Throws<SwiftTrapException>(() => store.Set("disabledProviders", new object?[] { "grok", null }));
        Assert.Throws<SwiftTrapException>(() => store.Set("providerBinaryPaths", new Dictionary<int, string> { [1] = "x" }));

        Assert.Null(store.Object("lastProjectID"));
        Assert.Null(store.Object("disabledProviders"));
        Assert.Null(store.Object("providerBinaryPaths"));
        Assert.True(store.Synchronize());
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void A_file_that_is_not_a_settings_document_reads_as_an_empty_store()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.UTF8.GetBytes("{not json");
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("appTheme"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
        store.Set("appTheme", "dark");
        Assert.True(store.Synchronize());
        Assert.Equal("dark", new SettingsStore(path).String("appTheme"));
    }

    // ED-156 (2): a top-level value that is valid JSON but not an object is a "non-object root": Load does not take
    // every top-level entry, so the store opens empty, the main file is left as it is and its original bytes are
    // preserved in <path>.unreadable before the first write can replace them.
    [Theory]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    [InlineData("[]")]
    [InlineData("[{\"appTheme\":{\"string\":\"dark\"}}]")]
    [InlineData("\"x\"")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("true")]
    public void A_file_whose_root_is_not_an_object_reads_as_an_empty_store_and_is_preserved(string json)
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("appTheme"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
        store.Set("appTheme", "dark");
        Assert.True(store.Synchronize());
        Assert.Equal("dark", new SettingsStore(path).String("appTheme"));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    [Fact]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void An_entry_the_store_cannot_read_is_skipped_and_the_rest_load()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.UTF8.GetBytes(
            "{\"appZoom\":{\"integer\":1.5},\"x\":{\"unknown\":1},\"y\":7,\"appTheme\":{\"string\":\"dark\"}}");
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("appZoom"));
        Assert.Null(store.Object("x"));
        Assert.Null(store.Object("y"));
        Assert.Equal("dark", store.String("appTheme"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void An_existing_file_that_cannot_be_read_fails_the_open_instead_of_starting_empty()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var first = new SettingsStore(path);
        first.Set("appTheme", "dark");
        Assert.True(first.Synchronize());

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => new SettingsStore(path));

        Assert.Equal("dark", new SettingsStore(path).String("appTheme"));
    }

    [Fact]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void A_failed_write_keeps_the_value_readable_and_records_the_error()
    {
        using var folder = new SettingsScratchFolder();
        var blocker = folder.PathOf("blocker");
        File.WriteAllBytes(blocker, []);
        var store = new SettingsStore(Path.Combine(blocker, "UserDefaults.json"));
        Assert.Null(store.LastWriteError);

        store.Set("appTheme", "dark"); // UserDefaults.set never throws at the call site (AppSettings.swift:288)

        Assert.Equal("dark", store.String("appTheme"));
        Assert.False(store.Synchronize());
        Assert.NotNull(store.LastWriteError);
    }

    [Fact]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void The_production_file_lives_in_the_Nth_folder_of_local_app_data()
    {
        // OD-0008 C7, renamed by OD-0135: the port stores its data under %LOCALAPPDATA%\Nth, never the Electron app's folder.
        Assert.Equal(
            "C:\\Users\\someone\\AppData\\Local\\Nth\\UserDefaults.json",
            SettingsStore.ProductionPath("C:\\Users\\someone\\AppData\\Local"));
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Bound_as_UserDefaults_Standard_it_serves_the_Swift_reads_and_writes()
    {
        using var runtime = new TestRuntime();
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        using (UserDefaults.Standard.Override(store))
        {
            var defaults = UserDefaults.Standard.Current;
            defaults.Set("chimeWhenFinished", false);            // AppSettings.swift:207
            Assert.False(Plist.AsBool(defaults.Object("chimeWhenFinished")) ?? true); // :709
        }
        Assert.True(store.Synchronize());
        Assert.False(Plist.AsBool(new SettingsStore(path).Object("chimeWhenFinished")) ?? true);
    }

    [Fact]
    [Trait("Rule", "R-SET-1")]
    [Trait("Decision", "ED-079")]
    [Trait("Decision", "ED-156")]
    public void Concurrent_writers_lose_no_key()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);

        Parallel.For(0, 16, i => store.Set("key" + i, (long)i));

        Assert.True(store.Synchronize());
        var reopened = new SettingsStore(path);
        for (var i = 0; i < 16; i++) Assert.Equal((long)i, reopened.Object("key" + i));
    }

    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-156")]
    public async Task Set_returns_and_reads_the_new_value_while_the_file_is_held()
    {
        using var runtime = new TestRuntime();
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var store = new SettingsStore(path);
        store.Set("appTheme", "dark");
        Assert.True(store.Synchronize());

        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Set("appTheme", "light");
            Assert.Equal("light", store.Object("appTheme"));
            var flush = Task.Run(store.Synchronize);
            Assert.True(SpinWait.SpinUntil(() => runtime.Time.ActiveTimers == 1 || flush.IsCompleted,
                TimeSpan.FromSeconds(10)), "writer did not reach its retry timer");
            Assert.False(flush.IsCompleted);
            Assert.Equal(1, runtime.Time.ActiveTimers);
            held.Dispose();
            runtime.Time.Advance(FileOps.RetryDelays[0]);
            Assert.True(await flush);
        }
        Assert.Equal("light", new SettingsStore(path).String("appTheme"));
    }

    [Fact]
    [Trait("Decision", "ED-156")]
    public void Unreadable_copies_are_deduplicated_and_new_bytes_use_the_first_free_suffix()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var first = Encoding.UTF8.GetBytes("{bad one");
        var second = Encoding.UTF8.GetBytes("{bad two");
        File.WriteAllBytes(path, first);
        _ = new SettingsStore(path);
        Assert.Equal(first, File.ReadAllBytes(path + ".unreadable"));
        _ = new SettingsStore(path);
        Assert.False(File.Exists(path + ".unreadable-2"));

        File.WriteAllBytes(path, second);
        _ = new SettingsStore(path);
        Assert.Equal(second, File.ReadAllBytes(path));
        Assert.Equal(first, File.ReadAllBytes(path + ".unreadable"));
        Assert.Equal(second, File.ReadAllBytes(path + ".unreadable-2"));
        _ = new SettingsStore(path);
        Assert.False(File.Exists(path + ".unreadable-3"));
    }

    [Fact]
    [Trait("Decision", "ED-156")]
    public void A_well_formed_file_has_no_unreadable_copy()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{\"appTheme\":{\"string\":\"dark\"}}"));
        Assert.Equal("dark", new SettingsStore(path).String("appTheme"));
        Assert.False(File.Exists(path + ".unreadable"));
    }

    [Fact]
    [Trait("Decision", "ED-156")]
    public void A_leading_UTF8_BOM_is_ignored_when_reading_and_never_written()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var body = Encoding.UTF8.GetBytes("{\"appTheme\":{\"string\":\"dark\"}}");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. body]);
        var store = new SettingsStore(path);
        Assert.Equal("dark", store.String("appTheme"));
        Assert.False(File.Exists(path + ".unreadable"));
        store.Set("appTheme", "light");
        Assert.True(store.Synchronize());
        Assert.Equal(Encoding.UTF8.GetBytes("{\"appTheme\":{\"string\":\"light\"}}"), File.ReadAllBytes(path));
    }

    // ED-156 (2): bytes that are not UTF-8 inside a key or a string make an entry the store cannot read (JSON text is
    // UTF-8, RFC 8259 section 8.1); that entry is skipped, the rest load, and the original bytes are preserved. An editor
    // saving in a Windows ANSI code page writes e-acute as the single byte 0xE9. Each '#' below becomes the given byte.
    [Theory]
    [Trait("Decision", "ED-156")]
    [InlineData("{\"a\":{\"string\":\"x#\"},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a#\":{\"string\":\"x\"},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"string\":\"caf#\"},\"b\":{\"bool\":true}}", 0xE9)]
    [InlineData("{\"a\":{\"date\":\"2026-01-01T00:00:00.0000000Z#\"},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"real\":\"nan#\"},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"string#\":\"x\"},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"array\":[{\"string\":\"x#\"}]},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"dict\":{\"k#\":{\"string\":\"x\"}}},\"b\":{\"bool\":true}}", 0xFF)]
    [InlineData("{\"a\":{\"dict\":{\"k\":{\"string\":\"x#\"}}},\"b\":{\"bool\":true}}", 0xE9)]
    public void An_entry_holding_bytes_that_are_not_UTF8_is_skipped_and_the_file_is_preserved(string ascii, byte raw)
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.ASCII.GetBytes(ascii).Select(b => b == (byte)'#' ? raw : b).ToArray();
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("a"));
        Assert.Equal(true, store.Object("b"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    // ED-156 (1)/(2), format as fcd7934 has it: a top-level entry is read whole, so one nested item TryRead rejects makes
    // the whole entry unread; it is skipped (never loaded in part) and the file is preserved.
    [Theory]
    [Trait("Decision", "ED-156")]
    [InlineData("{\"a\":{\"array\":[{\"real\":1},{\"bogus\":2}]},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"array\":[{\"real\":1},7]},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"dict\":{\"k\":{\"string\":\"x\"},\"m\":{\"integer\":1.5}}},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"dict\":{\"k\":{\"array\":[{\"bool\":true},{\"bool\":1}]}}},\"b\":{\"bool\":true}}")]
    public void A_nested_item_the_store_cannot_read_skips_its_whole_entry_and_the_file_is_preserved(string json)
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("a"));
        Assert.Equal(true, store.Object("b"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    // ED-156 (1): "each value a one-member object tagged with the XML property-list element name". A tagged object with
    // no member or with more than one member is an entry TryRead rejects (never read by its first member), at the top
    // level or nested; by ED-156 (2) it is skipped, the rest load, the main file is unchanged and its original bytes are
    // preserved in <path>.unreadable, so the first write cannot silently rewrite it.
    [Theory]
    [Trait("Decision", "ED-156")]
    [InlineData("{\"a\":{\"string\":\"x\",\"bool\":true},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"string\":\"x\",\"string\":\"y\"},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"array\":[{\"real\":1,\"string\":\"x\"}]},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"array\":[{}]},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"dict\":{\"k\":{\"string\":\"x\",\"bool\":true}}},\"b\":{\"bool\":true}}")]
    [InlineData("{\"a\":{\"dict\":{\"k\":{}}},\"b\":{\"bool\":true}}")]
    public void A_tagged_value_without_exactly_one_member_is_skipped_and_the_file_is_preserved(string json)
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var original = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("a"));
        Assert.Equal(true, store.Object("b"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
        store.Set("b", false);
        Assert.True(store.Synchronize());
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    // ED-156 (1): "Read skips exactly one leading UTF-8 BOM". A second BOM is not skipped, so the text is not a settings
    // document: the store opens empty, the main file is unchanged and its original bytes are preserved (ED-156 (2)).
    [Theory]
    [Trait("Decision", "ED-156")]
    [InlineData(2)]
    [InlineData(3)]
    public void More_than_one_leading_UTF8_BOM_is_not_skipped_and_the_file_is_preserved(int boms)
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        var body = Encoding.UTF8.GetBytes("{\"appTheme\":{\"string\":\"dark\"}}");
        var original = Enumerable.Repeat(new byte[] { 0xEF, 0xBB, 0xBF }, boms).SelectMany(b => b).Concat(body).ToArray();
        File.WriteAllBytes(path, original);

        var store = new SettingsStore(path);
        Assert.Null(store.Object("appTheme"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable"));
    }

    // ED-156 (2): a folder holding a copy's name counts as taken (File.Exists || Directory.Exists); the copy goes to the
    // first free name and the folders are left alone.
    [Fact]
    [Trait("Decision", "ED-156")]
    public void A_folder_with_an_unreadable_copy_name_counts_as_taken()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf("UserDefaults.json");
        Directory.CreateDirectory(path + ".unreadable");
        Directory.CreateDirectory(path + ".unreadable-2");
        var original = Encoding.UTF8.GetBytes("{bad");
        File.WriteAllBytes(path, original);

        _ = new SettingsStore(path);
        Assert.Empty(Directory.EnumerateFileSystemEntries(path + ".unreadable"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(path + ".unreadable-2"));
        Assert.Equal(original, File.ReadAllBytes(path + ".unreadable-3"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    // ED-156 (2): "if that write fails, the constructor throws", so the store never opens empty over settings it could
    // not keep a copy of. The copy is made unwritable by length: the settings file's name is 250 characters, so
    // "<name>.unreadable" exceeds the 255-character limit of an NTFS name component and cannot be created.
    [Fact]
    [Trait("Rule", "R-FS-1")]
    [Trait("Decision", "ED-156")]
    public void A_failed_copy_of_an_unreadable_file_fails_the_open_and_leaves_the_file_untouched()
    {
        using var folder = new SettingsScratchFolder();
        var path = folder.PathOf(new string('s', 245) + ".json");
        var original = Encoding.UTF8.GetBytes("{bad");
        File.WriteAllBytes(path, original);

        Assert.ThrowsAny<IOException>(() => new SettingsStore(path));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([Path.GetFileName(path)], Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }
}
