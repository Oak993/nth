using Xunit;

namespace Marrow.App.Tests.Window.Native;

// The parent suite serializes native input; each Fact owns a separate child testhost and STA.
[CollectionDefinition("WindowManagerNative", DisableParallelization = true)]
public sealed class WindowManagerNativeCollection : ICollectionFixture<WindowManagerNativeSession>;
