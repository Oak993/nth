using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Support;

// Swift: none (port-only, ED-211). Pins that every Marrow.Services production data-folder site
// (CommandCodeAPI.SupportDirectory/.InstallMod, PiCLI.SupportDirectory, CommandCodeSession.ApprovalDirectoryRoot,
// MCPBridge.InstallBridge, Storage.StorageLocations.CreateProduction, APIKeychain and MCPKeychain's
// credential service name) resolves through DevDataRoot rather than the OS special folder or AppInfo.Current.Name
// directly.
//
// R-TEST-9 / never touch %LOCALAPPDATA%\Marrow Code: a redirected call always points MARROW_DEV_DATA_ROOT at a
// throwaway folder under MARROW_PORT_DATA_DIR, so a side-effecting production entry point (InstallMod,
// InstallBridge, StorageLocations.Root/.WorktreesDirectory/.PatchesDirectory) never writes anywhere but that
// throwaway folder. The two path-only sites (SupportDirectory, ApprovalDirectoryRoot) never create anything, so
// they are also checked unredirected against the real special folder, without ever calling a directory-creating
// method on that real path.
//
// [Collection]: every test in this class, and only this class, mutates the process-wide MARROW_DEV_DATA_ROOT
// variable that DevDataRoot itself reads; the collection serializes it against itself so two of its own tests can
// never interleave (xUnit still runs it in parallel with every other collection in this assembly).
[Collection("Dev data root env")]
public sealed class DevDataRootProductionSitesTests
{
    private static string ScratchRoot(string label)
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(dataDirectory, "DevDataRootProductionSitesTests", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WithRedirect(string root, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(DevDataRoot.VariableName);
        try
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, root);
            Assert.True(DevDataRoot.IsRedirected); // guards every test below against a broken redirect silently no-opping
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, previous);
        }
    }

    private static void WithoutRedirect(Action body)
    {
        var previous = Environment.GetEnvironmentVariable(DevDataRoot.VariableName);
        try
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, null);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, previous);
        }
    }

    // ---- CommandCodeAPI.SupportDirectory / InstallMod (Providers/CommandCodeAPI.swift:441-446) ----

    [Trait("Decision", "ED-211")]
    [Fact]
    public void CommandCodeAPI_SupportDirectory_moves_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("commandcode-support");
        WithRedirect(root, () =>
        {
            Assert.Equal(CommandCodeAPI.SupportDirectoryUnder(root), CommandCodeAPI.SupportDirectory);
        });
    }

    [Trait("Decision", "ED-211")]
    [Fact]
    public void CommandCodeAPI_InstallMod_writes_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("commandcode-installmod");
        WithRedirect(root, () =>
        {
            CommandCodeAPI.InstallMod();
            Assert.True(File.Exists(Path.Combine(root, "Marrow Code", "CommandCode", "marrow-approvals.ts")));
        });
    }

    // ---- PiCLI.SupportDirectory (Providers/PiCLI.swift:14-18) ----

    [Trait("Decision", "ED-211")]
    [Fact]
    public void PiCLI_SupportDirectory_moves_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("pi-support");
        WithRedirect(root, () =>
        {
            Assert.Equal(PiCLI.SupportDirectoryUnder(root), PiCLI.SupportDirectory);
        });
    }

    [Trait("Decision", "ED-211")]
    [Fact]
    public void PiCLI_SupportDirectory_is_the_real_special_folder_when_not_redirected()
    {
        WithoutRedirect(() =>
        {
            var real = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(PiCLI.SupportDirectoryUnder(real), PiCLI.SupportDirectory);
        });
    }

    // ---- CommandCodeSession.ApprovalDirectoryRoot (test seam duplicating CommandCodeAPI's own literal) ----

    [Trait("Decision", "ED-211")]
    [Fact]
    public void CommandCodeSession_ApprovalDirectoryRoot_moves_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("commandcode-approvals");
        WithRedirect(root, () =>
        {
            Assert.Equal(Marrow.Core.Port.SwiftPath.AppendingPathComponent(root, "Marrow Code/CommandCode"), CommandCodeSession.ApprovalDirectoryRoot());
        });
    }

    [Trait("Decision", "ED-211")]
    [Fact]
    public void CommandCodeSession_ApprovalDirectoryRoot_is_the_real_special_folder_when_not_redirected()
    {
        WithoutRedirect(() =>
        {
            var real = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(Marrow.Core.Port.SwiftPath.AppendingPathComponent(real, "Marrow Code/CommandCode"), CommandCodeSession.ApprovalDirectoryRoot());
        });
    }

    // ---- MCPBridge.InstallBridge (MCP/MCPBridgeSource.swift:240-251) ----

    [Trait("Decision", "ED-211")]
    [Fact]
    public void MCPBridgeSource_InstallBridge_writes_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("mcp-bridge");
        WithRedirect(root, () =>
        {
            var url = MCPBridge.InstallBridge();
            Assert.Equal(MCPBridge.InstallBridge(root), url);
            Assert.True(File.Exists(Path.Combine(root, "Marrow Code", "mcp", "droppy-mcp-bridge.mjs")));
        });
    }

    // ---- Storage.StorageLocations.CreateProduction (Store/Storage.swift:19-48) ----

    [Trait("Decision", "ED-211")]
    [Fact]
    public void StorageLocations_CreateProduction_moves_root_and_state_directory_under_the_dev_root_when_redirected()
    {
        var root = ScratchRoot("storage");
        WithRedirect(root, () =>
        {
            var locations = StorageLocations.CreateProduction();
            Assert.Equal(Path.Combine(root, "Marrow Code"), locations.Root);
            Assert.True(Directory.Exists(locations.ThreadsDirectory));
            Assert.StartsWith(root, locations.WorktreesDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".marrow-dev", locations.WorktreesDirectory, StringComparison.Ordinal);
            Assert.True(Directory.Exists(locations.WorktreesDirectory));
            Assert.True(Directory.Exists(locations.PatchesDirectory));
        });
    }

    // ---- APIKeychain / MCPKeychain credential service name (Providers/APIKeychain.swift:45-49/:60-67/:77-81,
    // MCP/MCPKeychain.swift:15/:39/:55): AppInfo.Current.Name goes through DevDataRoot.CredentialService, so two
    // dev launches on different roots never read or overwrite each other's stored keys, and a redirected launch
    // never touches the real "Marrow" / "Marrow Dev" Credential Manager entries. ----

    private static IDisposable UseApp(string name) =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleDisplayName"] = name }, isDevelopment: false));

    [Trait("Decision", "ED-211")]
    [Fact]
    public void APIKeychain_writes_under_a_tagged_service_when_redirected_and_the_plain_service_sees_nothing()
    {
        var root = ScratchRoot("apikeychain");
        var backend = new InMemoryCredentialBackend();
        using var app = UseApp("Marrow Dev");
        using var store = SecretStore.Shared.Override(new SecretStore(backend));
        WithRedirect(root, () =>
        {
            Assert.True(APIKeychain.SetKey("sk-dev-secret", ProviderKind.Deepseek));

            var taggedService = DevDataRoot.CredentialService("Marrow Dev");
            Assert.NotEqual("Marrow Dev", taggedService);
            Assert.Equal("sk-dev-secret", APIKeychain.Read("DeepSeek API Key", service: taggedService));
            // The plain, untagged "Marrow Dev" service (what a non-redirected launch or another dev root would
            // read) sees nothing: the write never touched it.
            Assert.Null(APIKeychain.Read("DeepSeek API Key", service: "Marrow Dev"));
        });
    }

    [Trait("Decision", "ED-211")]
    [Fact]
    public void APIKeychain_service_is_the_plain_AppInfo_name_when_not_redirected()
    {
        var backend = new InMemoryCredentialBackend();
        using var app = UseApp("Marrow Dev");
        using var store = SecretStore.Shared.Override(new SecretStore(backend));
        WithoutRedirect(() =>
        {
            Assert.True(APIKeychain.SetKey("sk-plain-secret", ProviderKind.Deepseek));
            Assert.Equal("sk-plain-secret", APIKeychain.Read("DeepSeek API Key", service: "Marrow Dev"));
        });
    }

    [Trait("Decision", "ED-211")]
    [Fact]
    public void MCPKeychain_writes_under_a_tagged_service_when_redirected()
    {
        var root = ScratchRoot("mcpkeychain");
        var backend = new InMemoryCredentialBackend();
        using var app = UseApp("Marrow Dev");
        using var store = SecretStore.Shared.Override(new SecretStore(backend));
        WithRedirect(root, () =>
        {
            Assert.True(MCPKeychain.Set("dev-token", server: "example", field: "token"));

            var taggedService = DevDataRoot.CredentialService("Marrow Dev");
            Assert.NotEqual("Marrow Dev", taggedService);
            var target = SecretStore.TargetName(taggedService, "MCP example token");
            Assert.NotNull(backend.Peek(target));
            Assert.Equal("dev-token", MCPKeychain.Value("example", "token"));
            // The plain, untagged target (what a non-redirected launch or another dev root would read) sees
            // nothing: the write never touched it.
            Assert.Null(backend.Peek(SecretStore.TargetName("Marrow Dev", "MCP example token")));
        });
    }
}
