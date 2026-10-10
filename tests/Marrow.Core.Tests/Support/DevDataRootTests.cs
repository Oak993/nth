using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Swift: none (port-only, ED-212: DevDataRoot.LocalApplicationData is a Debug-only development affordance with no
// B3 Swift counterpart). Every test clears MARROW_DEV_DATA_ROOT in a try/finally around its own body, so it never
// leaks into another test even if a developer's shell happens to have it set, and restores whatever value was there
// before. Test methods in this class run sequentially (xUnit does not parallelize methods within one class); no
// other class in this assembly reads MARROW_DEV_DATA_ROOT or DevDataRoot today, but R-TEST-5 ("nothing
// process-global changes outside one non-parallel collection") is unconditional, so this class still joins a
// DisableParallelization collection rather than relying on that absence staying true (review fix, R-TEST-5). The
// CollectionDefinition itself lives in DevDataRootEnvCollection.cs (R-TEST-2/3: one test class per file; a
// CollectionDefinition with no [Fact] is exempt only in its own file, never sharing this one).
[Collection("Dev data root env")]
public sealed class DevDataRootTests
{
    [Fact]
    [Trait("Decision", "ED-212")]
    public void An_explicit_performance_profile_isolates_disk_mutex_and_credentials_in_every_configuration()
    {
        var root = Directory.CreateTempSubdirectory("marrow-performance-profile-").FullName;
        try
        {
            using var profile = DevDataRoot.PerformanceRoot.Override(root);
            Assert.Equal(root, DevDataRoot.LocalApplicationData());
            Assert.True(DevDataRoot.IsRedirected);
            Assert.NotEqual("Local\\Marrow.SingleInstance", DevDataRoot.KernelObjectName("Local\\Marrow.SingleInstance"));
            Assert.NotEqual("Marrow", DevDataRoot.CredentialService("Marrow"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void WithVariable(string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(DevDataRoot.VariableName);
        try
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevDataRoot.VariableName, previous);
        }
    }

    private static string RealLocalAppData() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Unset_resolves_the_real_special_folder_and_is_not_redirected()
    {
        WithVariable(null, () =>
        {
            Assert.Equal(RealLocalAppData(), DevDataRoot.LocalApplicationData());
            Assert.False(DevDataRoot.IsRedirected);
        });
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Empty_resolves_the_real_special_folder_and_is_not_redirected()
    {
        WithVariable(string.Empty, () =>
        {
            Assert.Equal(RealLocalAppData(), DevDataRoot.LocalApplicationData());
            Assert.False(DevDataRoot.IsRedirected);
        });
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void A_directory_that_does_not_exist_resolves_the_real_special_folder_and_is_not_redirected()
    {
        var missing = Path.Combine(Path.GetTempPath(), "marrow-dev-data-root-missing-" + Guid.NewGuid().ToString("N"));
        WithVariable(missing, () =>
        {
            Assert.False(Directory.Exists(missing));
            Assert.Equal(RealLocalAppData(), DevDataRoot.LocalApplicationData());
            Assert.False(DevDataRoot.IsRedirected);
        });
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Kernel_object_name_is_unchanged_when_not_redirected()
    {
        WithVariable(null, () => Assert.Equal("Local\\Marrow.Example", DevDataRoot.KernelObjectName("Local\\Marrow.Example")));
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Credential_service_is_unchanged_when_not_redirected()
    {
        WithVariable(null, () => Assert.Equal("Marrow Dev", DevDataRoot.CredentialService("Marrow Dev")));
    }

    // ---- The redirect-active behavior below only exists in a Debug build (DevDataRoot.cs's `#if DEBUG`); a
    // Release build must ignore MARROW_DEV_DATA_ROOT even when it names a directory that exists (review fix,
    // finding: "no test proves the Release build ignores the variable" - reproduced: running these three tests
    // unconditionally against a Release build failed all three, since DevDataRoot.IsRedirected is a hardcoded
    // `false` there and LocalApplicationData() always returns the real special folder). ----

#if DEBUG
    [Trait("Decision", "ED-212")]
    [Fact]
    public void An_existing_directory_is_returned_verbatim_and_is_redirected()
    {
        var root = Directory.CreateTempSubdirectory("marrow-dev-data-root-").FullName;
        try
        {
            WithVariable(root, () =>
            {
                Assert.Equal(root, DevDataRoot.LocalApplicationData());
                Assert.True(DevDataRoot.IsRedirected);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Kernel_object_name_and_credential_service_are_tagged_when_redirected_with_no_backslashes_in_the_tag()
    {
        var root = Directory.CreateTempSubdirectory("marrow-dev-data-root-").FullName;
        try
        {
            WithVariable(root, () =>
            {
                var lockName = DevDataRoot.KernelObjectName("Local\\Marrow.SingleInstance.Dev");
                var service = DevDataRoot.CredentialService("Marrow Dev");
                Assert.StartsWith("Local\\Marrow.SingleInstance.Dev.", lockName, StringComparison.Ordinal);
                Assert.Matches("^Local\\\\Marrow\\.SingleInstance\\.Dev\\.[0-9a-f]{8}$", lockName);
                Assert.Matches("^Marrow Dev \\[[0-9a-f]{8}\\]$", service);
                // Same root twice in the same call is stable...
                Assert.Equal(lockName, DevDataRoot.KernelObjectName("Local\\Marrow.SingleInstance.Dev"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void IsMisconfigured_is_true_only_for_a_non_empty_value_naming_a_directory_that_does_not_exist()
    {
        WithVariable(null, () => Assert.False(DevDataRoot.IsMisconfigured));
        WithVariable(string.Empty, () => Assert.False(DevDataRoot.IsMisconfigured));

        var missing = Path.Combine(Path.GetTempPath(), "marrow-dev-data-root-missing-" + Guid.NewGuid().ToString("N"));
        WithVariable(missing, () =>
        {
            Assert.False(Directory.Exists(missing));
            Assert.True(DevDataRoot.IsMisconfigured);
        });

        var root = Directory.CreateTempSubdirectory("marrow-dev-data-root-").FullName;
        try
        {
            WithVariable(root, () => Assert.False(DevDataRoot.IsMisconfigured));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Trait("Decision", "ED-212")]
    [Fact]
    public void Two_different_dev_roots_get_two_different_tags()
    {
        var rootA = Directory.CreateTempSubdirectory("marrow-dev-data-root-a-").FullName;
        var rootB = Directory.CreateTempSubdirectory("marrow-dev-data-root-b-").FullName;
        try
        {
            string TagFor(string root)
            {
                string result = "";
                WithVariable(root, () => result = DevDataRoot.CredentialService("Marrow Dev"));
                return result;
            }

            Assert.NotEqual(TagFor(rootA), TagFor(rootB));
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }
#else
    [Trait("Decision", "ED-212")]
    [Fact]
    public void An_existing_directory_is_ignored_and_not_redirected_in_a_release_build()
    {
        var root = Directory.CreateTempSubdirectory("marrow-dev-data-root-release-").FullName;
        try
        {
            WithVariable(root, () =>
            {
                Assert.Equal(RealLocalAppData(), DevDataRoot.LocalApplicationData());
                Assert.False(DevDataRoot.IsRedirected);
                Assert.Equal("Marrow Dev", DevDataRoot.CredentialService("Marrow Dev"));
                Assert.Equal("Local\\Marrow.Example", DevDataRoot.KernelObjectName("Local\\Marrow.Example"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
#endif
}
