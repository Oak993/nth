using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;

// Source: DroppyCode/Services/MCP/MCPOAuth.swift (B3 52bc792), :1-933. Changed from B2 1fbf81e (A12): postForm's body
// is now the fields joined by the file's own formEncode (only A-Za-z0-9-._~ left unescaped, ED-201 uppercase hex)
// instead of URLComponents' percentEncodedQuery; :768-930 shift -2 then +3 from B2's :768-930 (PR-MCP-159 b3_note).
// Keychain items are Windows Credential Manager items through MCPKeychain (OD-0008 B3). NWListener and NWConnection
// are the loopback sockets in Port/Network.cs (R-NET-2); NSWorkspace.shared.open is Port/Workspace.cs; URLSession is
// F.HTTP (ED-168), whose URLRequest takes the System.Uri Port/HttpAddress.cs makes from the parsed URL.

namespace Marrow.Services.MCP;

// :7-46. An error enum keeps its cases as a nested record union (R-ERR-3); each unlabeled String is named after the
// binding errorDescription gives it (`let detail`).
public sealed class MCPOAuthError(MCPOAuthError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record DiscoveryFailed(string Detail) : Case;    // :8
        public sealed record MetadataFailed(string Detail) : Case;     // :9
        public sealed record RegistrationUnsupported : Case;           // :10
        public sealed record RegistrationFailed(string Detail) : Case; // :11
        public sealed record TokenFailed(string Detail) : Case;        // :12
        public sealed record CallbackFailed(string Detail) : Case;     // :13
        public sealed record StateMismatch : Case;                     // :14
        public sealed record TimedOut : Case;                          // :15
        public sealed record Cancelled : Case;                         // :16
        public sealed record SignedOut : Case;                         // :17
        public sealed record Network(string Detail) : Case;            // :18
    }

    public Case ErrorCase { get; } = errorCase;

    // :20-45
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.DiscoveryFailed(var detail) =>
            detail.Length == 0 ? "Couldn't find the sign-in page." : "Couldn't find the sign-in page. " + detail, // :22-23
        Case.MetadataFailed(var detail) =>
            detail.Length == 0 ? "The server's sign-in details didn't make sense." : "The server's sign-in details didn't make sense. " + detail, // :24-25
        Case.RegistrationUnsupported => "This server doesn't let apps sign in on their own.", // :26-27
        Case.RegistrationFailed(var detail) =>
            detail.Length == 0 ? "Sign-in setup failed." : "Sign-in setup failed. " + detail, // :28-29
        Case.TokenFailed(var detail) =>
            detail.Length == 0 ? "Couldn't finish signing in." : "Couldn't finish signing in. " + detail, // :30-31
        Case.CallbackFailed(var detail) => detail.Length == 0 ? "Sign-in didn't finish." : detail, // :32-33
        Case.StateMismatch => "Sign-in didn't finish. Try again.", // :34-35
        Case.TimedOut => "Sign-in timed out. Try again and complete it in the browser.", // :36-37
        Case.Cancelled => "Sign-in was cancelled.", // :38-39
        Case.SignedOut => "You're signed out. Sign in again to continue.", // :40-41
        Case.Network(var detail) => detail.Length == 0 ? "The network didn't answer." : detail, // :42-43
        _ => throw new UnreachableException(),
    };
}

// :48-55
internal sealed record AuthServerMetadata(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string? RegistrationEndpoint,
    string? Resource,
    ValueList<string>? ResourceScopes);

// :57-60
internal sealed record RegisteredClient(string ClientID, string? ClientSecret);

// :62-74
internal sealed class OAuthRefreshGate : Actor
{
    private readonly Dictionary<string, TaskHandle<string>> flights = new(SwiftStringComparer.Instance);

    // :65-73
    public Task<string> Token(string @for, Func<Task<string>> refresh) => Isolated(async () =>
    {
        if (flights.TryGetValue(@for, out var flight))
        {
            return await flight.Value;
        }
        var started = TaskHandle.DetachedThrowing(refresh); // :69 Task.detached
        flights[@for] = started;
        try
        {
            return await started.Value;
        }
        finally
        {
            flights.Remove(@for); // :71 defer
        }
    });
}

// :76-80
internal sealed record CallbackResult(string Code, string RedirectURI, RegisteredClient Client);

// :82-100
internal sealed class CallbackState
{
    private readonly Mutex<(string RedirectURI, RegisteredClient? Client)> state = new(("", null)); // :83-88

    // :90-95
    public void Store(string redirectURI, RegisteredClient client) =>
        state.WithLock((ref (string RedirectURI, RegisteredClient? Client) s) => s = (redirectURI, client));

    // :97-99
    public (string RedirectURI, RegisteredClient? Client) Current() =>
        state.WithLock((ref (string RedirectURI, RegisteredClient? Client) s) => s);
}

// :102-126
internal sealed class CallbackWait(CheckedContinuation<CallbackResult> continuation, NWListener listener)
{
    private readonly Mutex<bool> finished = new(false);

    // :112-121 finish(.success(_:))
    public void Finish(CallbackResult result)
    {
        if (!ShouldResume()) return;
        listener.Cancel();
        continuation.Resume(result);
    }

    // :112-121 finish(.failure(_:))
    public void Finish(Exception error)
    {
        if (!ShouldResume()) return;
        listener.Cancel();
        continuation.Resume(error);
    }

    // :123-125
    public bool IsFinished => finished.WithLock((ref bool f) => f);

    // :113-118
    private bool ShouldResume() => finished.WithLock((ref bool f) =>
    {
        if (f) return false;
        f = true;
        return true;
    });
}

// :128-933
public static class MCPOAuth
{
    private static readonly OAuthRefreshGate refreshGate = new(); // :129
    private const double CallbackTimeout = 300; // :130

    // JSONSerialization.data(withJSONObject:options: [.sortedKeys]) (:394, :709): sorted, compact, "/" escaped.
    private static readonly SwiftJsonStyle SerializationSortedKeys = new(SortedKeys: true, PrettyPrinted: false, EscapeSlashes: true);

    // JSONSerialization.data(withJSONObject:options: []) (:746): unsorted, compact, "/" escaped.
    private static readonly SwiftJsonStyle SerializationPlain = new(SortedKeys: false, PrettyPrinted: false, EscapeSlashes: true);

    // ---- Sign in ----

    // :134-160
    public static async Task SignIn(string serverID, string serverName, string url)
    {
        var metadata = await Discover(url);
        var verifier = RandomToken(byteCount: 32);
        var challenge = PkceChallenge(verifier);
        var state = RandomToken(byteCount: 24);
        var callback = await RunCallbackServer(serverName, expectedState: state, onReady: async redirectURI =>
        {
            var client = await ClientRegistration(serverID, metadata, redirectURI);
            if (AuthorizationURL(metadata, clientID: client.ClientID, redirectURI: redirectURI, state: state, challenge: challenge)
                is not { } authorizationURL)
            {
                throw new MCPOAuthError(new MCPOAuthError.Case.MetadataFailed("The authorization address isn't valid.")); // :148
            }
            return (authorizationURL, client);
        });
        await ExchangeCode(
            serverID,
            metadata,
            client: callback.Client,
            code: callback.Code,
            redirectURI: callback.RedirectURI,
            verifier: verifier);
    }

    // ---- Tokens ----

    // :164-183
    public static async Task<string> AccessToken(string serverID)
    {
        if (MCPKeychain.Value(server: serverID, field: "oauth") is not { } stored
            || JSONValue.Parse(stored) is not { } json
            || json["access_token"]?.JSONValueString is not { } accessToken
            || accessToken.Length == 0)
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.SignedOut());
        }
        // :172-175. UPSTREAM DEFECT 18, reproduced: a session without expires_at never needs a refresh.
        bool needsRefresh;
        if (json["expires_at"]?.JSONValueDouble is not { } expiresAt) needsRefresh = false;
        else needsRefresh = SwiftDate.Seconds1970(SwiftDate.Now) >= expiresAt - 90;
        if (!needsRefresh) return accessToken; // :176
        if (json["refresh_token"]?.JSONValueString is not { } refreshToken || refreshToken.Length == 0) // :177-179
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.SignedOut());
        }
        return await refreshGate.Token(@for: serverID, refresh: () => PerformRefresh(serverID, stored: json, refreshToken: refreshToken)); // :180-182
    }

    // :185-187
    public static bool IsSignedIn(string serverID) => MCPKeychain.Value(server: serverID, field: "oauth") is not null;

    // :189-192. DEVIATION OD-0008 A12: upstream returns nothing and discards both deletion results; the port attempts
    // both deletions exactly as upstream and returns false when either was refused (a missing item is not a refusal,
    // MCPKeychain.Delete), so the disconnect path can report it (PR-MCP-061). Other callers ignore the result.
    public static bool SignOut(string serverID)
    {
        var session = MCPKeychain.Delete(server: serverID, field: "oauth");
        var client = MCPKeychain.Delete(server: serverID, field: "oauth-client");
        return session && client;
    }

    // :194-196
    public static Task RefreshIfNeeded(string serverID) =>
        SwiftTry.IgnoreAsync(() => AccessToken(serverID));

    // ---- Discovery ----

    // :200-248
    private static async Task<AuthServerMetadata> Discover(string url)
    {
        if (SwiftURL.Create(url) is null) // :201-203
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.DiscoveryFailed("That address doesn't look like a URL."));
        }
        string? issuer = null;
        string? resource = null;
        ValueList<string>? resourceScopes = null;
        var challenge = await ProtectedResourceMetadataURL(baseURL: url); // :207
        if (challenge.MetadataURL is { } metadataURL) // :208-216
        {
            if (await SwiftTry.OptionalAsync<JSONValue>(async () => await FetchJSON(metadataURL)) is { } json)
            {
                issuer = First(json["authorization_servers"]?.JSONValueArray)?.JSONValueString;
                resource = json["resource"]?.JSONValueString;
                if (json["scopes_supported"]?.JSONValueArray is { } listed)
                {
                    var names = NonEmptyStrings(listed);
                    resourceScopes = names.Count == 0 ? null : names;
                }
            }
        }
        else if (SwiftURL.Create(url) is { } requestURL) // :217-238
        {
            var origin = Origin(of: url);
            var path = requestURL.Path.Length == 0 ? "" : requestURL.Path;
            string[] candidates =
            [
                origin + "/.well-known/oauth-protected-resource" + path,
                origin + "/.well-known/oauth-protected-resource",
            ];
            foreach (var candidate in candidates)
            {
                if (await SwiftTry.OptionalAsync<JSONValue>(async () => await FetchJSON(candidate)) is { } json)
                {
                    var hasServers = json["authorization_servers"]?.JSONValueArray is not null;
                    var hasResource = json["resource"]?.JSONValueString is not null;
                    if (!(hasServers || hasResource)) continue;
                    issuer = First(json["authorization_servers"]?.JSONValueArray)?.JSONValueString;
                    resource = json["resource"]?.JSONValueString;
                    if (json["scopes_supported"]?.JSONValueArray is { } listed)
                    {
                        var names = NonEmptyStrings(listed);
                        resourceScopes = names.Count == 0 ? null : names;
                    }
                    break;
                }
            }
        }
        if (resourceScopes is null) // :239-241
        {
            resourceScopes = challenge.ChallengeScopes;
        }
        var fallbackIssuer = Origin(of: url); // :242
        var normalizedIssuer = (issuer ?? fallbackIssuer).Trim('/'); // :243 CharacterSet(charactersIn: "/") (R-STR-7)
        if (normalizedIssuer.Length == 0) // :244-246
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.DiscoveryFailed("That address doesn't look like a URL."));
        }
        return await FetchAuthorizationServerMetadata(issuer: normalizedIssuer, resource: resource, resourceScopes: resourceScopes);
    }

    // `array?.first` (R-COLL-3).
    private static JSONValue? First(IReadOnlyList<JSONValue>? array) => array is { Count: > 0 } ? array[0] : null;

    // `listed.compactMap(\.string).filter { !$0.isEmpty }` (:213, :232).
    private static ValueList<string> NonEmptyStrings(IReadOnlyList<JSONValue> listed)
    {
        var names = new List<string>();
        foreach (var element in listed)
        {
            if (element.JSONValueString is { Length: > 0 } name) names.Add(name);
        }
        return new ValueList<string>(names);
    }

    // :250-283
    private static async Task<(string? MetadataURL, ValueList<string>? ChallengeScopes)> ProtectedResourceMetadataURL(string baseURL)
    {
        if (SwiftURL.Create(baseURL) is not { } requestURL) return (null, null); // :251
        var answer = await SwiftTry.OptionalValueAsync(async () => // :255 try?
        {
            var request = new URLRequest(HttpAddress.For(requestURL), timeoutInterval: 30) { HttpMethod = "GET" } // :252-253
                .SetValue("application/json", forHTTPHeaderField: "Accept"); // :254
            return await URLSession.Shared.Data(request);
        });
        if (answer is not var (data, response) || response is not HTTPURLResponse http || http.StatusCode != 401) // :255-258
        {
            return (null, null);
        }
        var challenges = new List<string>(); // :259-269
        foreach (var (name, value) in http.AllHeaderFields)
        {
            if (SwiftString.CaseInsensitiveCompare(name, "WWW-Authenticate") != 0) continue;
            // :264-268 F.HTTP gives every field as one String (a repeated field joined, O-30), never an array.
            challenges.Add(value);
        }
        string? metadataURL = null;
        ValueList<string>? scopesFromChallenge = null;
        foreach (var challenge in challenges) // :272-280
        {
            if (metadataURL is null && ResourceMetadataURL(from: challenge) is { } foundURL)
            {
                metadataURL = foundURL;
            }
            if (scopesFromChallenge is null && ChallengeScopes(from: challenge) is { } foundScopes)
            {
                scopesFromChallenge = foundScopes;
            }
            if (metadataURL is not null && scopesFromChallenge is not null) break;
        }
        _ = data; // :281
        return (metadataURL, scopesFromChallenge);
    }

    // :285-287
    private static string? ResourceMetadataURL(string from) => ChallengeParameter("resource_metadata", @in: from);

    // :289-303. The value of one parameter of a WWW-Authenticate challenge, quoted or (as Stripe sends
    // resource_metadata) bare up to the next comma or space; nil when absent or empty.
    private static string? ChallengeParameter(string name, string @in)
    {
        // :292 header.range(of:): the first occurrence anywhere, with no token-boundary check.
        var index = @in.IndexOf(name + "=", StringComparison.Ordinal);
        if (index < 0) return null;
        var rest = FoundationText.Trim(@in[(index + name.Length + 1)..]); // :293 .whitespaces
        string value;
        if (SwiftString.HasPrefix(rest, "\"")) // :295-298
        {
            var inner = SwiftString.DropFirst(rest);
            var end = -1;
            var offset = 0;
            foreach (var character in SwiftString.Characters(inner))
            {
                if (SwiftString.Equal(character, "\""))
                {
                    end = offset;
                    break;
                }
                offset += character.Length;
            }
            if (end < 0) return null;
            value = inner[..end];
        }
        else // :299-301 rest.prefix { $0 != "," && $0 != " " }
        {
            var length = 0;
            foreach (var character in SwiftString.Characters(rest))
            {
                if (SwiftString.Equal(character, ",") || SwiftString.Equal(character, " ")) break;
                length += character.Length;
            }
            value = rest[..length];
        }
        return value.Length == 0 ? null : value; // :302
    }

    // :305-309
    private static ValueList<string>? ChallengeScopes(string from)
    {
        if (ChallengeParameter("scope", @in: from) is not { } value) return null;
        var names = SwiftString.Split(value, " ").Where(name => name.Length != 0).ToList();
        return names.Count == 0 ? null : new ValueList<string>(names);
    }

    // :311-352
    private static async Task<AuthServerMetadata> FetchAuthorizationServerMetadata(string issuer, string? resource, ValueList<string>? resourceScopes)
    {
        var @base = issuer.Trim('/'); // :312 CharacterSet(charactersIn: "/") (R-STR-7)
        var candidates = new List<string>();
        // :314-322 An issuer with a path (Stripe's is https://access.stripe.com/mcp) keeps its metadata where RFC 8414
        // puts it, the path after the well-known segment; the path-first forms below are what the rest answer to.
        if (URLComponents.Create(@base) is { } components
            && SwiftTry.Optional(() => Origin(of: @base)) is { } origin
            && components.Path.Length != 0
            && !SwiftString.Equal(components.Path, "/"))
        {
            candidates.Add(origin + "/.well-known/oauth-authorization-server" + components.Path);
            candidates.Add(origin + "/.well-known/openid-configuration" + components.Path);
        }
        candidates.Add(@base + "/.well-known/oauth-authorization-server"); // :323-326
        candidates.Add(@base + "/.well-known/openid-configuration");
        var lastError = ""; // :327
        foreach (var candidate in candidates) // :328-350
        {
            try
            {
                var json = await FetchJSON(candidate);
                if (json["authorization_endpoint"]?.JSONValueString is not { Length: > 0 } authorizationEndpoint
                    || json["token_endpoint"]?.JSONValueString is not { Length: > 0 } tokenEndpoint)
                {
                    lastError = "It's missing the addresses needed to sign in."; // :336
                    continue;
                }
                return new AuthServerMetadata( // :339-346
                    Issuer: @base,
                    AuthorizationEndpoint: authorizationEndpoint,
                    TokenEndpoint: tokenEndpoint,
                    RegistrationEndpoint: json["registration_endpoint"]?.JSONValueString,
                    Resource: resource,
                    ResourceScopes: resourceScopes);
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                // :348 the caught error's whole description, so a caught metadataFailed carries its own prefix into
                // the metadataFailed thrown at :351.
                lastError = SwiftErrors.LocalizedDescription(error);
            }
        }
        throw new MCPOAuthError(new MCPOAuthError.Case.MetadataFailed(lastError)); // :351
    }

    // ---- Client registration ----

    // :356-400
    private static async Task<RegisteredClient> ClientRegistration(string serverID, AuthServerMetadata metadata, string redirectURI)
    {
        if (MCPKeychain.Value(server: serverID, field: "oauth-client") is { } stored // :357-364
            && JSONValue.Parse(stored) is { } storedJSON
            && storedJSON["client_id"]?.JSONValueString is { Length: > 0 } storedClientID
            && storedJSON["issuer"]?.JSONValueString is { } storedIssuer
            && SwiftString.Equal(storedIssuer, metadata.Issuer))
        {
            return new RegisteredClient(storedClientID, ClientSecret: storedJSON["client_secret"]?.JSONValueString);
        }
        if (metadata.RegistrationEndpoint is not { Length: > 0 } registrationEndpoint) // :365-367
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.RegistrationUnsupported());
        }
        var scope = metadata.ResourceScopes is { Count: > 0 } resourceScopes ? string.Join(" ", resourceScopes) : null; // :375-377
        var body = Serialize(SerializationPlain, writer => // :368-377
        {
            // DEVIATION OD-0012 F1: upstream registers as "Droppy Code" (:369).
            writer.WriteString("client_name", "nth");
            writer.WriteStartArray("redirect_uris");
            writer.WriteStringValue(redirectURI);
            writer.WriteEndArray();
            writer.WriteStartArray("grant_types");
            writer.WriteStringValue("authorization_code");
            writer.WriteStringValue("refresh_token");
            writer.WriteEndArray();
            writer.WriteStartArray("response_types");
            writer.WriteStringValue("code");
            writer.WriteEndArray();
            writer.WriteString("token_endpoint_auth_method", "none");
            if (scope is not null) writer.WriteString("scope", scope);
        });
        var (data, response) = await Post(url: registrationEndpoint, body: body); // :378
        if (!(response.StatusCode >= 200 && response.StatusCode < 300) // :379-388
            || JSONValue.Parse(data) is not { } json
            || json["client_id"]?.JSONValueString is not { Length: > 0 } clientID)
        {
            var refusal = response.StatusCode == 403
                ? "The server only signs in apps it has approved (403), so this one can't yet."
                : "The server refused to set things up (" + response.StatusCode.ToString(CultureInfo.InvariantCulture) + ").";
            throw new MCPOAuthError(new MCPOAuthError.Case.RegistrationFailed(OauthErrorMessage(data) ?? refusal));
        }
        var clientSecret = json["client_secret"]?.JSONValueString; // :389
        var storedString = SwiftTry.Optional(() => SwiftText.DecodeUtf8OrNil(Serialize(SerializationSortedKeys, writer => // :390-398
        {
            writer.WriteString("client_id", clientID);
            writer.WriteString("issuer", metadata.Issuer);
            if (clientSecret is { Length: > 0 }) writer.WriteString("client_secret", clientSecret);
        })));
        if (storedString is not null)
        {
            MCPKeychain.Set(storedString, server: serverID, field: "oauth-client");
        }
        return new RegisteredClient(clientID, ClientSecret: clientSecret); // :399
    }

    // ---- Callback server ----

    // :404-459
    private static async Task<CallbackResult> RunCallbackServer(
        string serverName,
        string expectedState,
        Func<string, Task<(SwiftURL Url, RegisteredClient Client)>> onReady)
    {
        // :409-416 NWParameters.tcp with requiredLocalEndpoint 127.0.0.1 at any port, and no interface-type constraint.
        NWListener listener;
        try
        {
            listener = new NWListener(new IPEndPoint(IPAddress.Loopback, 0));
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network("Couldn't listen for the sign-in reply."));
        }
        var shared = new CallbackState();
        return await SwiftTask.WithCancellationHandler(
            () => CheckedContinuation<CallbackResult>.With(continuation => // :418-455
            {
                var wait = new CallbackWait(continuation, listener);
                var queue = new SerialQueue("MCPOAuth.callback");
                listener.StateUpdateHandler = state => // :422-446
                {
                    switch (state)
                    {
                        case NWListener.State.Ready:
                            if (listener.Port is not { } port) // :425-428
                            {
                                wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.Network("Couldn't listen for the sign-in reply.")));
                                return;
                            }
                            var redirectURI = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/callback"; // :429
                            TaskHandle.Detached(async () => // :430-438 Task { } in nonisolated code (R-TASK-1)
                            {
                                try
                                {
                                    var ready = await onReady(redirectURI);
                                    shared.Store(redirectURI, ready.Client);
                                    await MainActor.Run(() => { _ = Workspace.Open.Current(ready.Url); }); // :434
                                }
                                catch (Exception error) when (SwiftTry.IsThrownError(error))
                                {
                                    wait.Finish(error);
                                }
                            });
                            break;
                        case NWListener.State.Failed: // :439-440
                            wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.Network("The connection to the browser broke.")));
                            break;
                        case NWListener.State.Cancelled: // :441-442
                            wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.Cancelled()));
                            break;
                        default:
                            throw new UnreachableException();
                    }
                };
                listener.NewConnectionHandler = connection => // :447-450
                {
                    connection.Start(queue);
                    ReceiveRequest(wait, shared, serverName, expectedState, connection, buffer: [], queue);
                };
                listener.Start(queue); // :451
                queue.AsyncAfter(SwiftTask.Seconds(CallbackTimeout), () => // :452-454
                {
                    wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.TimedOut()));
                });
            }),
            () => listener.Cancel()); // :456-457
    }

    // :461-496
    private static void ReceiveRequest(
        CallbackWait wait,
        CallbackState shared,
        string serverName,
        string expectedState,
        NWConnection connection,
        byte[] buffer,
        SerialQueue queue)
    {
        connection.Receive(minimumIncompleteLength: 1, maximumLength: 65536, (data, isComplete, error) =>
        {
            if (wait.IsFinished) // :471-474
            {
                connection.Cancel();
                return;
            }
            var combined = buffer; // :475-478
            if (data is { Length: > 0 })
            {
                combined = [.. buffer, .. data];
            }
            var terminator = combined.AsSpan().IndexOf("\r\n\r\n"u8); // :479
            if (terminator < 0)
            {
                if (isComplete || error is not null || combined.Length > 1024 * 1024) // :480-483
                {
                    connection.Cancel();
                    return;
                }
                ReceiveRequest(wait, shared, serverName, expectedState, connection, buffer: combined, queue); // :484
                return;
            }
            HandleRequest(wait, shared, serverName, expectedState, connection, head: combined[..(terminator + 4)]); // :487-494
        });
    }

    // :498-562
    private static void HandleRequest(
        CallbackWait wait,
        CallbackState shared,
        string serverName,
        string expectedState,
        NWConnection connection,
        byte[] head)
    {
        // :506-511. UNVERIFIED O-13: components(separatedBy: "\r\n").first.
        if (SwiftText.DecodeUtf8OrNil(head) is not { } text)
        {
            connection.Cancel();
            return;
        }
        var requestLine = text.Split("\r\n")[0];
        var parts = SwiftString.Split(requestLine, " "); // :512
        if (!(parts.Count >= 2 && SwiftString.Equal(parts[0], "GET"))) // :513-518
        {
            SendHTML("<html><body>Method not allowed.</body></html>", status: 405, statusText: "Method Not Allowed", on: connection, done: () =>
            {
                connection.Cancel();
            });
            return;
        }
        var target = parts[1]; // :519
        if (!(SwiftString.Equal(target, "/callback") || SwiftString.HasPrefix(target, "/callback?"))) // :520-525
        {
            SendHTML("<html><body>Not found.</body></html>", status: 404, statusText: "Not Found", on: connection, done: () =>
            {
                connection.Cancel();
            });
            return;
        }
        var queryItems = URLComponents.Create("http://127.0.0.1" + target)?.QueryItems ?? ValueList<URLQueryItem>.Empty; // :526
        string? Value(string name) // :527-529
        {
            foreach (var item in queryItems)
            {
                if (SwiftString.Equal(item.Name, name)) return item.Value;
            }
            return null;
        }
        if (Value("error") is { Length: > 0 } providerError) // :530-538
        {
            var description = Value("error_description");
            var message = description is { Length: > 0 } ? description : "The sign-in was refused (" + providerError + ").";
            var page = FailurePage(serverName, message);
            SendHTML(page, status: 200, statusText: "OK", on: connection, done: () =>
            {
                wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.CallbackFailed(message)));
            });
            return;
        }
        if (Value("code") is not { Length: > 0 } code) // :539-545
        {
            var page = FailurePage(serverName, message: "The browser didn't send back a code.");
            SendHTML(page, status: 200, statusText: "OK", on: connection, done: () =>
            {
                wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.CallbackFailed("The browser didn't send back a code.")));
            });
            return;
        }
        if (Value("state") is not { } state || !SwiftString.Equal(state, expectedState)) // :546-552
        {
            var page = FailurePage(serverName, message: "Sign-in didn't finish. Try again.");
            SendHTML(page, status: 200, statusText: "OK", on: connection, done: () =>
            {
                wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.StateMismatch()));
            });
            return;
        }
        var successPage = SuccessPage(serverName); // :553-561
        SendHTML(successPage, status: 200, statusText: "OK", on: connection, done: () =>
        {
            var current = shared.Current();
            if (current.Client is not { } client || current.RedirectURI.Length == 0)
            {
                wait.Finish(new MCPOAuthError(new MCPOAuthError.Case.CallbackFailed("Sign-in didn't finish.")));
                return;
            }
            wait.Finish(new CallbackResult(code, current.RedirectURI, client));
        });
    }

    // :564-570
    private static void SendHTML(string html, int status, string statusText, NWConnection on, Action done)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + statusText
            + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length.ToString(CultureInfo.InvariantCulture)
            + "\r\nConnection: close\r\n\r\n";
        byte[] payload = [.. Encoding.UTF8.GetBytes(head), .. body];
        on.Send(payload, completion: _ => done()); // .contentProcessed
    }

    // ---- Authorization + token ----

    // :574-599
    private static SwiftURL? AuthorizationURL(
        AuthServerMetadata metadata,
        string clientID,
        string redirectURI,
        string state,
        string challenge)
    {
        if (URLComponents.Create(metadata.AuthorizationEndpoint) is not { } components) return null; // :581
        var items = new List<URLQueryItem>(components.QueryItems ?? ValueList<URLQueryItem>.Empty); // :582
        items.AddRange( // :583-590
        [
            new URLQueryItem(name: "response_type", value: "code"),
            new URLQueryItem(name: "client_id", value: clientID),
            new URLQueryItem(name: "redirect_uri", value: redirectURI),
            new URLQueryItem(name: "code_challenge", value: challenge),
            new URLQueryItem(name: "code_challenge_method", value: "S256"),
            new URLQueryItem(name: "state", value: state),
        ]);
        if (metadata.Resource is { Length: > 0 } resource) // :591-593
        {
            items.Add(new URLQueryItem(name: "resource", value: resource));
        }
        if (metadata.ResourceScopes is { Count: > 0 } resourceScopes) // :594-596
        {
            items.Add(new URLQueryItem(name: "scope", value: string.Join(" ", resourceScopes)));
        }
        components = components with { QueryItems = new ValueList<URLQueryItem>(items) }; // :597
        return components.Url; // :598
    }

    // :601-640
    private static async Task ExchangeCode(
        string serverID,
        AuthServerMetadata metadata,
        RegisteredClient client,
        string code,
        string redirectURI,
        string verifier)
    {
        var fields = new List<(string, string)> // :609-615
        {
            ("grant_type", "authorization_code"),
            ("code", code),
            ("redirect_uri", redirectURI),
            ("client_id", client.ClientID),
            ("code_verifier", verifier),
        };
        if (client.ClientSecret is { Length: > 0 } clientSecret) // :616-618
        {
            fields.Add(("client_secret", clientSecret));
        }
        if (metadata.Resource is { Length: > 0 } resource) // :619-621
        {
            fields.Add(("resource", resource));
        }
        var (data, response) = await PostForm(url: metadata.TokenEndpoint, fields: fields); // :622
        if (!(response.StatusCode >= 200 && response.StatusCode < 300) // :623-629
            || JSONValue.Parse(data) is not { } json
            || json["access_token"]?.JSONValueString is not { Length: > 0 } accessToken)
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.TokenFailed(OauthErrorMessage(data) ?? "The server didn't hand back a token."));
        }
        StoreSession( // :630-639
            serverID,
            accessToken: accessToken,
            refreshToken: json["refresh_token"]?.JSONValueString,
            expiresIn: json["expires_in"]?.JSONValueInt,
            tokenEndpoint: metadata.TokenEndpoint,
            clientID: client.ClientID,
            clientSecret: client.ClientSecret,
            resource: metadata.Resource);
    }

    // :642-680
    private static async Task<string> PerformRefresh(string serverID, JSONValue stored, string refreshToken)
    {
        if (stored["token_endpoint"]?.JSONValueString is not { Length: > 0 } tokenEndpoint // :643-649
            || stored["client_id"]?.JSONValueString is not { Length: > 0 } clientID)
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.SignedOut());
        }
        var fields = new List<(string, string)> // :650-654
        {
            ("grant_type", "refresh_token"),
            ("refresh_token", refreshToken),
            ("client_id", clientID),
        };
        if (stored["client_secret"]?.JSONValueString is { Length: > 0 } clientSecret) // :655-657
        {
            fields.Add(("client_secret", clientSecret));
        }
        if (stored["resource"]?.JSONValueString is { Length: > 0 } resource) // :658-660
        {
            fields.Add(("resource", resource));
        }
        var (data, response) = await PostForm(url: tokenEndpoint, fields: fields); // :661
        if (!(response.StatusCode >= 200 && response.StatusCode < 300) // :662-668
            || JSONValue.Parse(data) is not { } json
            || json["access_token"]?.JSONValueString is not { Length: > 0 } accessToken)
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.TokenFailed(OauthErrorMessage(data) ?? "The sign-in expired."));
        }
        StoreSession( // :669-678
            serverID,
            accessToken: accessToken,
            refreshToken: json["refresh_token"]?.JSONValueString ?? refreshToken,
            expiresIn: json["expires_in"]?.JSONValueInt,
            tokenEndpoint: tokenEndpoint,
            clientID: clientID,
            clientSecret: stored["client_secret"]?.JSONValueString,
            resource: stored["resource"]?.JSONValueString);
        return accessToken; // :679
    }

    // :682-713
    private static void StoreSession(
        string serverID,
        string accessToken,
        string? refreshToken,
        long? expiresIn,
        string tokenEndpoint,
        string clientID,
        string? clientSecret,
        string? resource)
    {
        // :701 Date().timeIntervalSince1970 + Double(expiresIn), read before the dictionary is serialized.
        double? expiresAt = expiresIn is { } seconds ? SwiftDate.Seconds1970(SwiftDate.Now) + seconds : null;
        var session = SwiftTry.Optional(() => SwiftText.DecodeUtf8OrNil(Serialize(SerializationSortedKeys, writer => // :692-711
        {
            writer.WriteString("access_token", accessToken);
            writer.WriteString("token_endpoint", tokenEndpoint);
            writer.WriteString("client_id", clientID);
            if (refreshToken is { Length: > 0 }) writer.WriteString("refresh_token", refreshToken); // :697-699
            if (expiresAt is { } value) SwiftJson.WriteDouble(writer, "expires_at", value); // :700-702
            if (clientSecret is { Length: > 0 }) writer.WriteString("client_secret", clientSecret); // :703-705
            if (resource is { Length: > 0 }) writer.WriteString("resource", resource); // :706-708
        })));
        if (session is null) return; // :709-711
        MCPKeychain.Set(session, server: serverID, field: "oauth"); // :712
    }

    // ---- HTTP helpers ----

    // :717-742
    private static async Task<JSONValue> FetchJSON(string url)
    {
        if (SwiftURL.Create(url) is not { } requestURL) // :718-720
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network("That address isn't valid."));
        }
        byte[] data;
        HTTPURLResponse response;
        try // :726-737
        {
            var request = new URLRequest(HttpAddress.For(requestURL), timeoutInterval: 30) { HttpMethod = "GET" } // :721-722
                .SetValue("application/json", forHTTPHeaderField: "Accept"); // :723
            var (body, urlResponse) = await URLSession.Shared.Data(request);
            data = body;
            if (urlResponse is not HTTPURLResponse http)
            {
                throw new MCPOAuthError(new MCPOAuthError.Case.Network("The server didn't answer properly."));
            }
            response = http;
        }
        catch (MCPOAuthError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network(""));
        }
        if (!(response.StatusCode >= 200 && response.StatusCode < 300) || JSONValue.Parse(data) is not { } json) // :738-740
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.MetadataFailed(
                "The server answered " + response.StatusCode.ToString(CultureInfo.InvariantCulture) + "."));
        }
        return json;
    }

    // :744-766. The body arrives already serialized (JSONSerialization with no options, :746), which cannot fail for
    // the string-only body :368-377 builds.
    private static async Task<(byte[] Data, HTTPURLResponse Response)> Post(string url, byte[] body)
    {
        if (SwiftURL.Create(url) is not { } requestURL) // :745-749
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network("That address isn't valid."));
        }
        try // :755-765
        {
            var request = new URLRequest(HttpAddress.For(requestURL), timeoutInterval: 30) { HttpMethod = "POST", HttpBody = body } // :750-751, :754
                .SetValue("application/json", forHTTPHeaderField: "Content-Type") // :752
                .SetValue("application/json", forHTTPHeaderField: "Accept"); // :753
            var (data, urlResponse) = await URLSession.Shared.Data(request);
            if (urlResponse is not HTTPURLResponse http)
            {
                throw new MCPOAuthError(new MCPOAuthError.Case.Network("The server didn't answer properly."));
            }
            return (data, http);
        }
        catch (MCPOAuthError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network(""));
        }
    }

    // :768-788. B3 (A12, PR-MCP-159 b3_note): the body is fields.map { formEncode($0.0)=formEncode($0.1) } joined by
    // "&", replacing B2's URLComponents(percentEncodedQuery ?? "") (B2 :771-772, :778).
    private static async Task<(byte[] Data, HTTPURLResponse Response)> PostForm(string url, IReadOnlyList<(string, string)> fields)
    {
        if (SwiftURL.Create(url) is not { } requestURL) // :769-771
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network("That address isn't valid."));
        }
        try // :777-787
        {
            var request = new URLRequest(HttpAddress.For(requestURL), timeoutInterval: 30) // :772
            {
                HttpMethod = "POST", // :773
                // :776 fields.map { "\(formEncode($0.0))=\(formEncode($0.1))" }.joined(separator: "&").utf8
                HttpBody = Encoding.UTF8.GetBytes(string.Join("&", fields.Select(field => FormEncode(field.Item1) + "=" + FormEncode(field.Item2)))),
            }
                .SetValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type") // :774
                .SetValue("application/json", forHTTPHeaderField: "Accept"); // :775
            var (data, urlResponse) = await URLSession.Shared.Data(request);
            if (urlResponse is not HTTPURLResponse http)
            {
                throw new MCPOAuthError(new MCPOAuthError.Case.Network("The server didn't answer properly."));
            }
            return (data, http);
        }
        catch (MCPOAuthError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.Network(""));
        }
    }

    // :790-793 (NEW at B3, A12, PR-MCP-159 b3_note). addingPercentEncoding(withAllowedCharacters:) percent-encodes the
    // UTF-8 bytes not in the given CharacterSet and returns nil only when the string cannot be represented as valid
    // Unicode, which is unreachable for a .NET string, so the "?? value" fallback (:792) has no reachable path here,
    // exactly as on the reference runtime.
    private static string FormEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var output = new byte[bytes.Length * 3];
        var i = 0;
        foreach (var b in bytes)
        {
            if (IsFormUnreserved(b))
            {
                output[i++] = b;
            }
            else
            {
                output[i++] = (byte)'%';
                output[i++] = HexDigitUppercase(b >> 4); // ED-201: uppercase hex escapes.
                output[i++] = HexDigitUppercase(b & 0xF);
            }
        }
        return SwiftText.DecodeUtf8Repairing(output.AsSpan(0, i));
    }

    // :791 CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~"): only
    // these are left unescaped, so ":", "/", "+", "=", "&" and a space are all escaped (PR-MCP-159 expected_result).
    private static bool IsFormUnreserved(byte b) =>
        (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'0' && b <= (byte)'9')
        || b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'~';

    private static byte HexDigitUppercase(int nibble) => (byte)"0123456789ABCDEF"[nibble];

    // :795-801
    private static string? OauthErrorMessage(byte[] data)
    {
        if (JSONValue.Parse(data) is not { } json) return null;
        if (json["error_description"]?.JSONValueString is { Length: > 0 } description)
        {
            return description;
        }
        return json["error"]?.JSONValueString;
    }

    // :803-815
    private static string Origin(string of)
    {
        if (URLComponents.Create(of) is not { } components
            || components.Scheme is not { Length: > 0 } scheme
            || components.Host is not { Length: > 0 } host)
        {
            throw new MCPOAuthError(new MCPOAuthError.Case.DiscoveryFailed("That address doesn't look like a URL."));
        }
        var origin = scheme + "://" + host;
        if (components.Port is { } port)
        {
            origin += ":" + port.ToString(CultureInfo.InvariantCulture);
        }
        return origin;
    }

    // JSONSerialization.data(withJSONObject:options:) of a [String: Any] (:394, :709, :746): the members are written
    // into a JSON tree, and SwiftJson renders its bytes in the given style (R-CODE-3, R-CODE-4).
    private static byte[] Serialize(SwiftJsonStyle style, Action<Utf8JsonWriter> writeMembers)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeMembers(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory, SwiftJson.DocumentOptions);
        return SwiftJson.Write(document.RootElement, style);
    }

    // ---- PKCE + randomness ----

    // :819-825. ENGINEERING DECISION (PR-MCP-161 windows_path; upstream_defects.md entry 22 as corrected): Swift's
    // UInt8.random(in:) draws from SystemRandomNumberGenerator, the platform CSPRNG, so the port draws the same byte
    // count from the Windows CSPRNG. The byte counts and the encoding are unchanged.
    private static string RandomToken(int byteCount)
    {
        var bytes = new byte[byteCount];
        RandomNumberGenerator.Fill(bytes);
        return Base64url(bytes);
    }

    // :827-830
    private static string PkceChallenge(string @for) => Base64url(SHA256.HashData(Encoding.UTF8.GetBytes(@for)));

    // :832-837
    private static string Base64url(byte[] data) =>
        Convert.ToBase64String(data)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("=", "", StringComparison.Ordinal);

    // ---- Branded callback pages ----

    // :841-847
    public static string SuccessPage(string serverName) =>
        Page(
            headTitle: "Signed in to " + EscapeHTML(serverName),
            heading: "Signed in to " + EscapeHTML(serverName),
            message: "You can close this tab and head back to nth."); // DEVIATION OD-0012 F1: upstream "head back to Droppy Code." (:845)

    // :849-855
    public static string FailurePage(string serverName, string message) =>
        Page(
            headTitle: "Sign-in didn't finish \U00002014 " + EscapeHTML(serverName), // U+2014 EM DASH
            heading: "Sign-in didn't finish",
            message: EscapeHTML(message));

    // :857-916. The document is PR-MCP-163's. Its logo (:907) is Droppy's mark; PR-MCP-163 records an open question on
    // what, if anything, replaces it, and the line stays as the source has it until that is answered.
    // DEVIATION OD-0012 F1: the wordmark (:910) reads "Marrow" where upstream reads "Droppy Code".
    // The raw literal below is the Swift literal (:858-915) line for line: this file is stored with LF line endings
    // (.gitattributes "* -text"), so the lines join with a line feed as in Swift, and the string ends at "</html>" with no
    // newline after it.
    private static string Page(string headTitle, string heading, string message) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="color-scheme" content="dark">
        <title>{{headTitle}}</title>
        <style>
        * { box-sizing: border-box; }
        html, body { margin: 0; padding: 0; }
        body {
          background: #0B0B0F;
          color: #F5F5F7;
          font-family: -apple-system, "SF Pro Text", "Helvetica Neue", Helvetica, sans-serif;
          min-height: 100vh;
          display: flex;
          align-items: center;
          justify-content: center;
        }
        .card {
          width: 420px;
          max-width: calc(100vw - 48px);
          padding: 40px;
          border-radius: 22px;
          background: rgba(255, 255, 255, 0.06);
          text-align: center;
        }
        h1 {
          font-size: 22px;
          font-weight: 600;
          color: #F5F5F7;
          margin: 20px 0 8px;
        }
        p {
          font-size: 14px;
          color: rgba(255, 255, 255, 0.62);
          line-height: 1.5;
          margin: 0 0 24px;
        }
        .wordmark {
          font-size: 12px;
          color: rgba(255, 255, 255, 0.4);
          letter-spacing: 0.04em;
        }
        </style>
        </head>
        <body>
        <div class="card">
        <svg width="56" height="56" viewBox="0 0 100 100" opacity="0.9" xmlns="http://www.w3.org/2000/svg"><path fill="#FFFFFF" fill-rule="nonzero" d="M16.00 94.00 C16.00 94.00 16.17 80.33 17.00 74.00 C17.83 67.67 19.50 61.00 21.00 56.00 C22.50 51.00 25.50 47.33 26.00 44.00 C26.50 40.67 26.17 38.83 24.00 36.00 C21.83 33.17 15.83 30.17 13.00 27.00 C10.17 23.83 7.00 17.00 7.00 17.00 C7.00 17.00 16.50 24.50 20.00 27.00 C23.50 29.50 27.50 33.17 28.00 32.00 C28.50 30.83 24.33 24.50 23.00 20.00 C21.67 15.50 20.00 5.00 20.00 5.00 C20.00 5.00 27.50 12.67 31.00 16.00 C34.50 19.33 37.17 23.67 41.00 25.00 C44.83 26.33 50.17 23.33 54.00 24.00 C57.83 24.67 61.00 26.67 64.00 29.00 C67.00 31.33 68.67 34.50 72.00 38.00 C75.33 41.50 80.33 46.33 84.00 50.00 C87.67 53.67 92.17 57.33 94.00 60.00 C95.83 62.67 95.33 64.33 95.00 66.00 C94.67 67.67 93.83 69.00 92.00 70.00 C90.17 71.00 87.33 71.67 84.00 72.00 C80.67 72.33 75.33 71.50 72.00 72.00 C68.67 72.50 67.33 72.67 64.00 75.00 C60.67 77.33 55.33 83.17 52.00 86.00 C48.67 88.83 46.00 90.67 44.00 92.00 C42.00 93.33 40.00 94.00 40.00 94.00 C40.00 94.00 16.00 94.00 16.00 94.00 Z M63.00 38.00 C60.24 38.00 58.00 40.24 58.00 43.00 C58.00 45.76 60.24 48.00 63.00 48.00 C65.76 48.00 68.00 45.76 68.00 43.00 C68.00 40.24 65.76 38.00 63.00 38.00 Z M86.00 61.20 C85.01 61.20 84.20 62.01 84.20 63.00 C84.20 63.99 85.01 64.80 86.00 64.80 C86.99 64.80 87.80 63.99 87.80 63.00 C87.80 62.01 86.99 61.20 86.00 61.20 Z"/></svg>
        <h1>{{heading}}</h1>
        <p>{{message}}</p>
        <div class="wordmark">nth</div>
        </div>
        <script>setTimeout(() => window.close(), 1500);</script>
        </body>
        </html>
        """;

    // :918-932 (PR-MCP-164). Swift iterates Characters (extended grapheme clusters) and compares each with the five
    // one-scalar Characters, so a cluster such as "<" plus a combining mark is copied unchanged; SwiftString.Characters
    // and SwiftString.Equal keep that.
    private static string EscapeHTML(string @string)
    {
        var escaped = new StringBuilder(@string.Length);
        foreach (var character in SwiftString.Characters(@string))
        {
            if (SwiftString.Equal(character, "&")) escaped.Append("&amp;");
            else if (SwiftString.Equal(character, "<")) escaped.Append("&lt;");
            else if (SwiftString.Equal(character, ">")) escaped.Append("&gt;");
            else if (SwiftString.Equal(character, "\"")) escaped.Append("&quot;");
            else if (SwiftString.Equal(character, "'")) escaped.Append("&#39;");
            else escaped.Append(character);
        }
        return escaped.ToString();
    }
}
