using System;
using System.IO;
using Xunit;

namespace Marrow.App.Tests;

// Swift: none (port-only, ED-212). Review fix (R-TEST-5): every test that mutates the process-wide
// MARROW_DEV_DATA_ROOT variable must run inside one non-parallel collection, so it can never interleave with
// another test in this assembly that reads or depends on it. This class, and only this class in Marrow.App.Tests,
// mutates that variable; DisableParallelization on the collection definition (DevDataRootEnvCollection.cs;
// R-TEST-2/3 keeps it out of this file) also serializes it against any future class added to the same collection.
[Collection("Dev data root env")]
public sealed class DroppyCodeAppDevDataRootTests
{
    private static void WithDevRoot(string? root, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(Marrow.Core.Support.DevDataRoot.VariableName);
        try
        {
            Environment.SetEnvironmentVariable(Marrow.Core.Support.DevDataRoot.VariableName, root);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Marrow.Core.Support.DevDataRoot.VariableName, previous);
        }
    }

    [Fact]
    [Trait("Decision", "ED-212")]
    public void ReopenSignalName_is_unchanged_with_no_dev_root_active()
    {
        WithDevRoot(null, () =>
        {
#if DEBUG
            Assert.Equal("Local\\Marrow.Reopen.Dev", DroppyCodeApp.ReopenSignalName);
#else
            Assert.Equal("Local\\Marrow.Reopen.Release", DroppyCodeApp.ReopenSignalName);
#endif
        });
    }

#if DEBUG
    [Fact]
    [Trait("Decision", "ED-212")]
    public void ReopenSignalName_is_tagged_per_dev_root_and_two_roots_never_collide()
    {
        var rootA = Directory.CreateTempSubdirectory("marrow-dev-data-root-a-").FullName;
        var rootB = Directory.CreateTempSubdirectory("marrow-dev-data-root-b-").FullName;
        try
        {
            string nameA = "", nameB = "";
            WithDevRoot(rootA, () => nameA = DroppyCodeApp.ReopenSignalName);
            WithDevRoot(rootB, () => nameB = DroppyCodeApp.ReopenSignalName);

            Assert.StartsWith("Local\\Marrow.Reopen.Dev.", nameA, StringComparison.Ordinal);
            Assert.NotEqual(nameA, nameB);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }
#else
    [Fact]
    [Trait("Decision", "ED-212")]
    public void ReopenSignalName_ignores_a_dev_root_in_a_release_build()
    {
        var root = Directory.CreateTempSubdirectory("marrow-dev-data-root-release-").FullName;
        try
        {
            WithDevRoot(root, () =>
                Assert.Equal("Local\\Marrow.Reopen.Release", DroppyCodeApp.ReopenSignalName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
#endif
}
