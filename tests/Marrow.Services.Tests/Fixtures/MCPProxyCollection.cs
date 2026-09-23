// PORT-ONLY test fixture, no Swift twin: the named xUnit collection that owns MCPProxy.Shared, the real
// process-wide singleton (a Swift `static let`) MCPProxyTests and MCPStoreTests both drive (R-TEST-9).
//
// MCPProxySingletonFixture is declared as this COLLECTION's fixture (ICollectionFixture<T>), not as
// MCPProxyTests' own IClassFixture<T>: xUnit creates a collection fixture exactly once, before the first test
// of the first class in the collection runs, and disposes it after the last. That is the only way to guarantee
// MCPProxySingletonFixture's SecretStore.Shared.Override(Store) scope is what is active the first time anything
// calls MCPProxy.Shared.Current.Start() (MCPProxyTests.cs:27, MCPStoreTests.cs's own note) - Start() is
// idempotent and the proxy's background accept loop permanently captures whichever ExecutionContext (and so
// whichever ambient SecretStore) was live on the FIRST caller's stack (JSONRPC-style capture, MCPProxy.cs).
//
// MEASURED regression this fixes: putting MCPStoreTests in this collection (so it no longer races MCPProxyTests
// under xUnit's default parallel collections) without ALSO making MCPProxySingletonFixture a collection fixture
// made the ordering deterministic but wrong - MCPStoreHarness's own MCPStore construction (MCPStore.cs:38,
// MCPProxy.Shared.Current.Start()) now reliably ran before MCPProxyTests' own IClassFixture did, so it won the
// capture every time: filtering MCPProxyTests+MCPStoreTests together failed the same 3 OAuth tests
// (An_OAuth_route_adds_a_fresh_bearer_token, OAuth_bearer_token_is_dropped_on_a_cross_origin_redirect_but_kept_
// same_origin, OAuth_401_is_retried_once_with_a_refetched_token_and_then_given_up_on) in 8/8 runs, up from the
// pre-collection 3/5. Making MCPProxySingletonFixture an ICollectionFixture removes that race entirely: it is
// now guaranteed to run first regardless of which class's test xUnit happens to schedule first within the
// collection. ENGINEERING DECISION, test-only: no expectation or product code changed.
namespace Marrow.Services.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MCPProxyCollection : ICollectionFixture<MCPProxySingletonFixture>
{
    public const string Name = "MCPProxy singleton";
}
