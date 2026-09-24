using System.Text;
using System.Text.Json;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Xunit;

#pragma warning disable CA1307, CA1310  // StringComparison analyzer warnings

// Source: DroppyCode/Services/MCP/MCPSessionOverrides.swift (B2 1fbf81e), :1-107.

namespace Marrow.Services.Tests.MCP;

public sealed class MCPSessionOverridesTests : IDisposable
{
    private readonly string tempDir;
    private readonly StorageScratchFolder storage;

    public MCPSessionOverridesTests()
    {
        // Set up test environment
        var marrowDataDir = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(marrowDataDir))
        {
            marrowDataDir = Path.Combine(Path.GetTempPath(), "marrow-test-data");
            Directory.CreateDirectory(marrowDataDir);
            Environment.SetEnvironmentVariable("MARROW_PORT_DATA_DIR", marrowDataDir);
        }

        tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        storage = new StorageScratchFolder();
    }

    public void Dispose()
    {
        storage?.Dispose();

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // Helpers to set up test environment
    private string CreateCopilotConfig(Dictionary<string, object>? servers = null)
    {
        var copilotDir = Path.Combine(tempDir, ".copilot");
        Directory.CreateDirectory(copilotDir);
        var configPath = Path.Combine(copilotDir, "mcp-config.json");

        var config = new Dictionary<string, object>
        {
            ["mcpServers"] = servers ?? new Dictionary<string, object>()
        };

        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(configPath, json);
        return configPath;  // Return path for test setup
    }

    private string CreateClaudeConfig(Dictionary<string, object>? mcpServers = null)
    {
        var claudeConfigPath = Path.Combine(tempDir, "claude.json");

        var config = new Dictionary<string, object>
        {
            ["mcpServers"] = mcpServers ?? new Dictionary<string, object>()
        };

        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(claudeConfigPath, json);
        return claudeConfigPath;
    }

    #region copilotUserServerNames tests

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    public void CopilotUserServerNames_WhenConfigMissing_ReturnsEmpty()
    {
        // No config file exists
        using var env = new HomeEnvironmentOverride(tempDir);
        var names = MCPSessionOverrides.CopilotUserServerNames();
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    public void CopilotUserServerNames_WhenConfigEmpty_ReturnsEmpty()
    {
        // :4-12: return empty array when no servers
        CreateCopilotConfig(new Dictionary<string, object>());
        using var env = new HomeEnvironmentOverride(tempDir);
        var names = MCPSessionOverrides.CopilotUserServerNames();
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    public void CopilotUserServerNames_ReturnsServersSorted()
    {
        // :4-12: extract and sort server keys
        var servers = new Dictionary<string, object>
        {
            ["zebra"] = new { },
            ["apple"] = new { },
            ["middle"] = new { }
        };
        var configPath = CreateCopilotConfig(servers);

        using var env = new CopilotConfigOverride(configPath);
        var names = MCPSessionOverrides.CopilotUserServerNames();
        Assert.Equal(new[] { "apple", "middle", "zebra" }, names.ToArray());
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    public void CopilotUserServerNames_WhenMalformedJSON_ReturnsEmpty()
    {
        // :4-12: guard against parsing errors
        var copilotDir = Path.Combine(tempDir, ".copilot");
        Directory.CreateDirectory(copilotDir);
        var configPath = Path.Combine(copilotDir, "mcp-config.json");
        File.WriteAllText(configPath, "{ invalid json");

        using var env = new HomeEnvironmentOverride(tempDir);
        var names = MCPSessionOverrides.CopilotUserServerNames();
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    public void CopilotUserServerNames_WhenNoMcpServersKey_ReturnsEmpty()
    {
        // :4-12: guard against missing mcpServers key
        var copilotDir = Path.Combine(tempDir, ".copilot");
        Directory.CreateDirectory(copilotDir);
        var configPath = Path.Combine(copilotDir, "mcp-config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { other = "data" }));

        using var env = new HomeEnvironmentOverride(tempDir);
        var names = MCPSessionOverrides.CopilotUserServerNames();
        Assert.Empty(names);
    }

    #endregion

    #region copilotArguments tests

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_WhenConfigNil_ReturnsEmpty()
    {
        // :14-30: return empty when no copilot config
        var args = MCPSessionOverrides.CopilotArguments();
        Assert.Empty(args);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_PassesConfigAsOneToken()
    {
        // :14-30: first two tokens are "--additional-mcp-config" and the JSON as one string
        var configJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["test-server"] = new { }
            }
        });

        // Mock MCPProviderConfig.CopilotConfigJSON to return our test JSON
        var args = MCPSessionOverrides.CopilotArguments(configJson);

        Assert.NotEmpty(args);
        Assert.Equal("--additional-mcp-config", args[0]);
        Assert.Equal(configJson, args[1]);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_DisablesUserServersNotInConfig()
    {
        // :14-30: disable user servers by name if not in config
        var configJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["configured-server"] = new { }
            }
        });

        // Create user config with extra servers
        var servers = new Dictionary<string, object>
        {
            ["configured-server"] = new { },
            ["user-server-1"] = new { },
            ["user-server-2"] = new { }
        };
        var configPath = CreateCopilotConfig(servers);
        using var env = new CopilotConfigOverride(configPath);

        var args = MCPSessionOverrides.CopilotArguments(configJson);

        // Should contain disable flags for the user servers not in config
        Assert.Contains("--disable-mcp-server", args);
        Assert.Contains("user-server-1", args);
        Assert.Contains("user-server-2", args);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_DisablesBuiltinsForGithub()
    {
        // :14-30: append "--disable-builtin-mcps" when github is in the config
        var configJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["github"] = new { }
            }
        });

        var args = MCPSessionOverrides.CopilotArguments(configJson);

        Assert.Contains("--disable-builtin-mcps", args);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_NoDisableBuiltinsWithoutGithub()
    {
        // :14-30: no "--disable-builtin-mcps" when github is not in config
        var configJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["other-server"] = new { }
            }
        });

        var args = MCPSessionOverrides.CopilotArguments(configJson);

        Assert.DoesNotContain("--disable-builtin-mcps", args);
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_ArgumentOrderExact()
    {
        // :14-30: exact order: --additional-mcp-config, JSON, disable-mcp-server pairs, --disable-builtin-mcps
        var configJson = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["github"] = new { }
            }
        });

        var servers = new Dictionary<string, object>
        {
            ["github"] = new { },
            ["user-server"] = new { }
        };
        var configPath = CreateCopilotConfig(servers);
        using var env = new CopilotConfigOverride(configPath);

        var args = MCPSessionOverrides.CopilotArguments(configJson);

        // Check order: [0] = "--additional-mcp-config", [1] = JSON, then disable pairs, then builtin
        Assert.Equal("--additional-mcp-config", args[0]);
        Assert.Equal(configJson, args[1]);

        // Find the disable flags
        var argsArray = args.ToArray();
        var disableUserIdx = Array.IndexOf(argsArray, "--disable-mcp-server");
        var disableBuiltinIdx = Array.IndexOf(argsArray, "--disable-builtin-mcps");

        Assert.True(disableUserIdx > 1, "disable-mcp-server should come after JSON config");
        if (disableBuiltinIdx >= 0)
        {
            Assert.True(disableBuiltinIdx > disableUserIdx, "--disable-builtin-mcps should come after disable-mcp-server");
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-173")]
    [Trait("Req", "PR-PROVCLI-136")]
    public void CopilotArguments_BadJsonStillPassesConfig()
    {
        // :14-30: failure_cases - unparseable JSON still passes the config
        var badJson = "{ invalid json }";
        var args = MCPSessionOverrides.CopilotArguments(badJson);

        Assert.Equal("--additional-mcp-config", args[0]);
        Assert.Equal(badJson, args[1]);
        // All user servers will be disabled since IDs set is empty
    }

    #endregion

    #region opencodeEnvironment tests

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_WhenFileMissing_ReturnsEmpty()
    {
        // :32-71: return empty dict when file missing
        using var env = new ClaudeConfigOverride(Path.Combine(tempDir, "nonexistent.json"));
        var result = MCPSessionOverrides.OpencodeEnvironment();
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_WhenParseFails_ReturnsEmpty()
    {
        // :32-71: return empty dict when parsing fails
        var configPath = Path.Combine(tempDir, "claude.json");
        File.WriteAllText(configPath, "{ invalid json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_WhenNoMcpServers_ReturnsEmpty()
    {
        // :32-71: return empty dict when mcpServers is empty or missing
        CreateClaudeConfig(new Dictionary<string, object>());
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_HttpServerWithUrl()
    {
        // :32-71: HTTP server produces {type: remote, url, enabled: true}
        var servers = new Dictionary<string, object>
        {
            ["http-server"] = new
            {
                type = "http",
                url = "https://example.com"
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        Assert.NotEmpty(result);
        Assert.True(result.ContainsKey("OPENCODE_CONFIG_CONTENT"));

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"type\":\"remote\"", json);
        Assert.Contains("\"url\":\"https://example.com\"", json);
        Assert.Contains("\"enabled\":true", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_HttpServerWithHeaders()
    {
        // :32-71: HTTP server includes headers when non-empty
        var servers = new Dictionary<string, object>
        {
            ["http-server"] = new
            {
                type = "http",
                url = "https://example.com",
                headers = new Dictionary<string, object>
                {
                    ["Authorization"] = "Bearer token123"
                }
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"headers\"", json);
        Assert.Contains("\"Authorization\"", json);
        Assert.Contains("\"Bearer token123\"", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_HttpServerEmptyUrl()
    {
        // :32-71: HTTP server with missing url becomes empty string
        var servers = new Dictionary<string, object>
        {
            ["http-server"] = new
            {
                type = "http"
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"url\":\"\"", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_LocalServerWithCommand()
    {
        // :32-71: Local server produces {type: local, command: [exe] + args, enabled: true}
        var servers = new Dictionary<string, object>
        {
            ["local-server"] = new
            {
                command = "mcp-server",
                args = new[] { "arg1", "arg2" }
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"type\":\"local\"", json);
        Assert.Contains("\"command\":[\"mcp-server\",\"arg1\",\"arg2\"]", json);
        Assert.Contains("\"enabled\":true", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_LocalServerWithEnv()
    {
        // :32-71: Local server includes environment (not env) when non-empty
        var servers = new Dictionary<string, object>
        {
            ["local-server"] = new
            {
                command = "mcp-server",
                env = new Dictionary<string, object>
                {
                    ["VAR1"] = "value1"
                }
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"environment\"", json);
        Assert.Contains("\"VAR1\"", json);
        Assert.Contains("\"value1\"", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_LocalServerEmptyCommand_Skipped()
    {
        // :32-71: Local server with empty command is skipped silently
        var servers = new Dictionary<string, object>
        {
            ["valid-server"] = new
            {
                command = "valid-command"
            },
            ["invalid-server"] = new
            {
                command = ""
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"valid-server\"", json);
        Assert.DoesNotContain("\"invalid-server\"", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_LocalServerMissingCommand_Skipped()
    {
        // :32-71: Local server without command is skipped
        var servers = new Dictionary<string, object>
        {
            ["valid-server"] = new
            {
                command = "valid-command"
            },
            ["invalid-server"] = new
            {
                args = new[] { "arg1" }
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"valid-server\"", json);
        Assert.DoesNotContain("\"invalid-server\"", json);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_ReturnsCorrectSchemaAndKey()
    {
        // :32-71: Root object has $schema and mcp keys
        var servers = new Dictionary<string, object>
        {
            ["test-server"] = new
            {
                command = "test"
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        Assert.True(result.ContainsKey("OPENCODE_CONFIG_CONTENT"));
        var json = result["OPENCODE_CONFIG_CONTENT"];

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("$schema", out var schema));
        Assert.Equal("https://opencode.ai/config.json", schema.GetString());
        Assert.True(doc.RootElement.TryGetProperty("mcp", out var mcp));
        Assert.Equal(JsonValueKind.Object, mcp.ValueKind);
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_SortedKeys()
    {
        // :32-71: JSON is serialized with sorted keys
        var servers = new Dictionary<string, object>
        {
            ["z-server"] = new
            {
                command = "z"
            },
            ["a-server"] = new
            {
                command = "a"
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        var aPos = json.IndexOf("\"a-server\"", StringComparison.Ordinal);
        var zPos = json.IndexOf("\"z-server\"", StringComparison.Ordinal);
        Assert.True(aPos < zPos, "Keys should be sorted alphabetically");
    }

    [Fact]
    [Trait("Req", "PR-MCP-174")]
    public void OpencodeEnvironment_OnlyStringValuesPreserved()
    {
        // :32-71: Non-string values in headers and env are dropped
        var servers = new Dictionary<string, object>
        {
            ["http-server"] = new
            {
                type = "http",
                url = "https://example.com",
                headers = new Dictionary<string, object>
                {
                    ["string-header"] = "value",
                    ["number-header"] = 123  // Should be dropped
                }
            },
            ["local-server"] = new
            {
                command = "cmd",
                env = new Dictionary<string, object>
                {
                    ["STRING_VAR"] = "value",
                    ["NUMBER_VAR"] = 456  // Should be dropped
                }
            }
        };
        CreateClaudeConfig(servers);
        var configPath = Path.Combine(tempDir, "claude.json");

        using var env = new ClaudeConfigOverride(configPath);
        var result = MCPSessionOverrides.OpencodeEnvironment();

        var json = result["OPENCODE_CONFIG_CONTENT"];
        Assert.Contains("\"string-header\"", json);
        Assert.DoesNotContain("\"number-header\"", json);
        Assert.Contains("\"STRING_VAR\"", json);
        Assert.DoesNotContain("\"NUMBER_VAR\"", json);
    }

    #endregion
}
