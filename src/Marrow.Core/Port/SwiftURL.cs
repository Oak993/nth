// PORT-ONLY: Foundation URL(string:) has no .NET equivalent; System.Uri parses, canonicalizes and fails differently (R-PATH-1).
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Port;

// Source: swift-foundation tag swift-6.2-RELEASE (not the B2 tree; Foundation is Apple's), the path URL(string:) takes on
// macOS 26 (URL.swift:611-614 routes URL to _SwiftURL; the transfer question stays with ED-086):
//   Sources/FoundationEssentials/URL/URL_Swift.swift:75-98 (init), :385-388 (scheme), :444-491 (host), :497-532 (path),
//     :574-624 (file-system path, POSIX style), :641-647 (pathComponents, ED-143), :282-284 and :339-341 (strings).
//   Sources/FoundationEssentials/URL/URL.swift:1371-1378 (hash and ==), :1412-1415 (the POSIX default path style).
//   Sources/FoundationEssentials/URL/URLParser.swift:18-198 (parse info, UIDNA hook), :200-958 (RFC3986Parser, the
//     non-compatibility parse with encodingInvalidCharacters: true and allowEmptyScheme: true), :963-1133 (hex and
//     percent coding), :1169-1270 (the allowed-set table).
//   Sources/FoundationEssentials/String/String+Path.swift:49-77 (pathComponents), :420-428 (_droppingTrailingSlashes).
//   Sources/FoundationInternationalization/URLParser+ICU.swift:28-221 (UIDNAHookICU), over Windows icu.dll (IcuNative).
// Not ported: the compatibility parse (URLParser.swift:1305-1618; URL(string:) never takes it, URL_Swift.swift:83-88),
// base URLs (URL(string:) passes none, URL_Swift.swift:95-98), and every #if os(Windows) line, which the macOS build
// does not compile: URLParser.swift:632-634 ("\" to "/"), String+Path.swift:31-34, URL_Swift.swift:597-603 (Oracle O-39).
// The Windows shape of a file:///C:/... path has no ruling and is out of scope; the path style is .posix (URL.swift:1412-1415).
// Platform boundary: a C# string reaches the parser as its UTF-8 bytes; a lone surrogate, which a Swift String cannot hold,
// becomes U+FFFD as it does when an NSString is bridged.

/// <summary>Foundation <c>URL</c> as <c>URL(string:)</c> builds it (R-PATH-1). Equality is textual.</summary>
[JsonConverter(typeof(SwiftURL.JsonConverter))]
public sealed class SwiftURL : IEquatable<SwiftURL>
{
    private readonly URLParseInfo _parseInfo; // URL_Swift.swift:90; _baseURL is always nil (:91) and _encoding .utf8 (:92)

    private SwiftURL(URLParseInfo parseInfo) => _parseInfo = parseInfo;

    // UNVERIFIED O-38: the macOS 26 binary parses and re-encodes as swift-foundation swift-6.2-RELEASE reads (ED-086).
    /// <summary><c>URL(string:)</c>: nil for "" or text the parser refuses. URL_Swift.swift:95-98, :83-93, :75-77.</summary>
    public static SwiftURL? Create(string text)
    {
        if (text.Length == 0) return null; // :96
        return CreateStringOrEmpty(text); // :97
    }

    /// <summary>
    /// <c>URL(stringOrEmpty:)</c> with no base, the init <c>URLComponents.url</c> takes (URLComponents.swift:765-773,
    /// URL.swift:682-685): as <see cref="Create"/> but "" makes a URL. URL_Swift.swift:83-93, :75-77.
    /// </summary>
    internal static SwiftURL? CreateStringOrEmpty(string text)
    {
        // :84-89 compatibility is false, so parse(string:) with encodingInvalidCharacters: true (:83, :75-77)
        var parseInfo = RFC3986Parser.Parse(text, encodingInvalidCharacters: true, allowEmptyScheme: true);
        return parseInfo is null ? null : new SwiftURL(parseInfo);
    }

    private URLParseInfo.EncodedComponentSet EncodedComponents => _parseInfo.EncodedComponents; // URL_Swift.swift:262-264

    /// <summary><c>url.relativeString</c>. URL_Swift.swift:282-284.</summary>
    public string RelativeString => _parseInfo.UrlString;

    /// <summary><c>url.absoluteString</c>: with no base it is the relative string. URL_Swift.swift:339-341, :286-289.</summary>
    public string AbsoluteString => RelativeString;

    /// <summary><c>url.scheme</c>, as written. URL_Swift.swift:385-388.</summary>
    public string? Scheme => _parseInfo.Scheme; // :386 baseURL is nil

    // URL_Swift.swift:390-394
    private bool IsFileURL => Scheme is { } scheme && SwiftString.Lowercased(scheme) == "file";

    // URL_Swift.swift:396-398
    private bool HasAuthority => _parseInfo.HasAuthority;

    /// <summary><c>url.host</c>: <c>host(percentEncoded: false)</c>. URL_Swift.swift:444-446.</summary>
    public string? Host => HostComponent(percentEncoded: false);

    // UNVERIFIED O-40: macOS 26 keeps host() percent-encoded, IDNA text in host, nil for a bare empty host.
    /// <summary><c>url.host(percentEncoded:)</c>; <c>host()</c> defaults to true (URL.swift:886). URL_Swift.swift:448-491.</summary>
    public string? HostComponent(bool percentEncoded = true)
    {
        if (!HasAuthority) return null; // :449 baseURL is nil
        if (_parseInfo.Host is not { } encodedHost) return null; // :450

        // :452-458 an empty host with no user, password or port is nil (bincompat)
        if (encodedHost.Length == 0 && _parseInfo.User is null && _parseInfo.Password is null && _parseInfo.PortRange is null)
            return null;

        // :460-479
        string? RequestedHost()
        {
            if (percentEncoded)
            {
                if (!EncodedComponents.HasFlag(URLParseInfo.EncodedComponentSet.Host) || _parseInfo.DidPercentEncodeHost)
                    return encodedHost; // :462-464
                // :465-469 IDNA-decode, then percent-encode
                if (RFC3986Parser.IDNADecodeHost(encodedHost) is not { } decoded) return encodedHost;
                return RFC3986Parser.PercentEncode(decoded, URLComponent.Host);
            }
            if (EncodedComponents.HasFlag(URLParseInfo.EncodedComponentSet.Host))
            {
                if (_parseInfo.DidPercentEncodeHost) return RFC3986Parser.PercentDecode(encodedHost); // :471-473
                return encodedHost; // :474-475 the IDNA-encoded host
            }
            return RFC3986Parser.PercentDecode(encodedHost); // :477 _encoding is .utf8
        }

        if (RequestedHost() is not { } requestedHost) return null; // :481-483

        if (_parseInfo.IsIPLiteral)
        {
            // :485-487 String(requestedHost.utf8.dropFirst().dropLast()), the failable UTF-8 view init
            var bytes = Encoding.UTF8.GetBytes(requestedHost);
            var inner = bytes.Length <= 1 ? ReadOnlySpan<byte>.Empty : bytes.AsSpan(1, bytes.Length - 2);
            return SwiftText.DecodeUtf8OrNil(inner);
        }
        return requestedHost; // :489
    }

    // URL_Swift.swift:511-522 absolutePath(percentEncoded:) with no base
    private string AbsolutePathPercentEncoded => _parseInfo.Path;

    // UNVERIFIED O-41: macOS 26 drops every trailing slash after decoding and keeps %2F and %00 in a file path.
    /// <summary><c>url.path</c>: decoded, every trailing slash dropped; a file URL gives its POSIX path. URL_Swift.swift:524-532.</summary>
    public string Path
    {
        get
        {
            if (IsFileURL) return FileSystemPath(); // :525
            var path = AbsolutePathPercentEncoded; // :526
            // :527-531 both branches decode UTF-8 here (_encoding is .utf8)
            return RFC3986Parser.PercentDecode(path) is { } decoded ? DroppingTrailingSlashes(decoded) : "";
        }
    }

    // UNVERIFIED O-44: macOS 26 splits the encoded path and decodes each component, as swift-foundation 6.2 reads.
    /// <summary><c>url.pathComponents</c> (ED-143). URL_Swift.swift:641-647.</summary>
    public ImmutableArray<string> PathComponents
    {
        get
        {
            // :642 the encoded path split by String+Path.swift:49-77, each component decoded ("" when it does not decode)
            var result = new List<string>();
            foreach (var component in StringPathComponents(AbsolutePathPercentEncoded))
                result.Add(RFC3986Parser.PercentDecode(component) ?? "");
            // :643-645
            if (result.Count > 1 && result[^1] == "/") result.RemoveAt(result.Count - 1);
            return result.ToImmutableArray();
        }
    }

    // URL_Swift.swift:574-578 "Don't decode "%2F" or "%00""
    private static readonly byte[] CharsToLeaveEncoded = [(byte)'/', 0];

    private static string DecodeFilePath(string path) =>
        RFC3986Parser.PercentDecode(path, excluding: CharsToLeaveEncoded) ?? "";

    // URL_Swift.swift:621-624 with :609-619, style .posix (URL.swift:1412-1415) and compatibility false (:612-614)
    private string FileSystemPath() => DecodeFilePath(DroppingTrailingSlashes(AbsolutePathPercentEncoded)); // :616

    /// <summary>
    /// String+Path.swift:420-428 <c>_droppingTrailingSlashes</c>. <c>String(self[...lastNonSlash])</c> ends after the
    /// Character that holds the last non-slash byte (a String index rounds down to its Character).
    /// </summary>
    private static string DroppingTrailingSlashes(string text)
    {
        if (text.Length == 0) return text; // :421-423
        var lastNonSlash = text.Length - 1;
        while (lastNonSlash >= 0 && text[lastNonSlash] == '/') lastNonSlash--;
        if (lastNonSlash < 0) return "/"; // :424-426 all slashes
        var end = 0;
        while (end <= lastNonSlash) end += StringInfo.GetNextTextElementLength(text.AsSpan(end));
        return text[..end]; // :427
    }

    /// <summary>String+Path.swift:49-77 <c>pathComponents</c>; no "\" conversion on macOS (:31-34 is #if os(Windows)).</summary>
    private static List<string> StringPathComponents(string text)
    {
        var result = new List<string>();
        if (text.Length == 0) return result; // :54-56
        var start = 0;
        if (text[0] == '/')
        {
            // :60-63
            result.Add("/");
            start = FirstNonSlash(text, 0);
        }
        var end = start;
        while (end != text.Length)
        {
            // :65-72
            var slash = text.IndexOf('/', end);
            end = slash < 0 ? text.Length : slash;
            if (start != end) result.Add(text[start..end]);
            start = FirstNonSlash(text, end);
            end = start;
        }
        // :73-75 utf8.count > 1; the encoded path is ASCII, so UTF-16 length is the byte count
        if (Encoding.UTF8.GetByteCount(text) > 1 && text[^1] == '/') result.Add("/");
        return result;
    }

    private static int FirstNonSlash(string text, int from)
    {
        var index = from;
        while (index < text.Length && text[index] == '/') index++;
        return index;
    }

    // URL.swift:1371-1378: hash(into:) combines relativeString; == compares relativeString and baseURL (always nil here).
    public bool Equals(SwiftURL? other) =>
        other is not null && (ReferenceEquals(this, other) || SwiftString.Equal(RelativeString, other.RelativeString));

    public override bool Equals(object? obj) => obj is SwiftURL other && Equals(other);

    public override int GetHashCode() => SwiftStringComparer.Instance.GetHashCode(RelativeString);

    public static bool operator ==(SwiftURL? left, SwiftURL? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SwiftURL? left, SwiftURL? right) => !(left == right);

    /// <summary>
    /// Foundation's URL Codable (Codable.swift): a keyed container with an optional "base" (encodeIfPresent /
    /// decodeIfPresent, omitted when there is no base) and a required "relative" string; the value is
    /// <c>URL(string: relative, relativeTo: base)</c>. Every <see cref="SwiftURL"/> this port builds has no base
    /// (Create never takes one), so "base" is never written and, if a stored document somehow carried one, it is
    /// read and then dropped: URL(string:relativeTo:) only resolves a relative reference against it, and every
    /// URL this port parses is written absolute.
    /// </summary>
    public sealed class JsonConverter : JsonConverter<SwiftURL>
    {
        public override SwiftURL Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Decode(document.RootElement);
        }

        internal static SwiftURL Decode(JsonElement element)
        {
            var relative = SwiftDecoding.Decode(element, "relative", SwiftDecoding.ReadString);
            return Create(relative) ?? throw new DecodingError("dataCorrupted", "Invalid URL string.");
        }

        public override void Write(Utf8JsonWriter writer, SwiftURL value, JsonSerializerOptions options) => WriteMembers(writer, value);

        internal static void WriteMembers(Utf8JsonWriter writer, SwiftURL value)
        {
            writer.WriteStartObject();
            writer.WriteString("relative", value.RelativeString);
            writer.WriteEndObject();
        }
    }

    // URLComponents.Component, the cases RFC3986Parser.percentEncode switches on (URLParser.swift:207-226)
    internal enum URLComponent { Scheme, User, Password, Host, Port, Path, Query, QueryItem, Fragment }

    /// <summary>URLParser.swift:18-166 <c>URLParseInfo</c>, over the UTF-8 bytes of the URL string.</summary>
    internal sealed class URLParseInfo
    {
        // :35-43
        [Flags]
        internal enum EncodedComponentSet : byte
        {
            None = 0,
            User = 1 << 0,
            Password = 1 << 1,
            Host = 1 << 2,
            Path = 1 << 3,
            Query = 1 << 4,
            Fragment = 1 << 5,
        }

        internal readonly record struct ByteRange(int Lower, int Upper)
        {
            public int Count => Upper - Lower;
        }

        internal URLParseInfo(string urlString, byte[] utf8, in URLBufferParseInfo buffer, EncodedComponentSet encodedComponents)
        {
            // :49-63
            UrlString = urlString;
            Utf8 = utf8;
            SchemeRange = buffer.SchemeRange;
            UserRange = buffer.UserRange;
            PasswordRange = buffer.PasswordRange;
            HostRange = buffer.HostRange;
            PortRange = buffer.PortRange;
            PathRange = buffer.PathRange;
            QueryRange = buffer.QueryRange;
            FragmentRange = buffer.FragmentRange;
            IsIPLiteral = buffer.IsIPLiteral;
            DidPercentEncodeHost = buffer.DidPercentEncodeHost;
            PathHasFileID = buffer.PathHasFileID;
            EncodedComponents = encodedComponents;
        }

        internal string UrlString { get; }
        internal byte[] Utf8 { get; }
        internal ByteRange? SchemeRange { get; }
        internal ByteRange? UserRange { get; }
        internal ByteRange? PasswordRange { get; }
        internal ByteRange? HostRange { get; }
        internal ByteRange? PortRange { get; }
        internal ByteRange? PathRange { get; }
        internal ByteRange? QueryRange { get; }
        internal ByteRange? FragmentRange { get; }
        internal bool IsIPLiteral { get; }
        internal bool DidPercentEncodeHost { get; }
        internal bool PathHasFileID { get; }
        internal EncodedComponentSet EncodedComponents { get; } // :45-47

        // :65-67
        internal bool HasAuthority => UserRange is not null || PasswordRange is not null || HostRange is not null || PortRange is not null;

        // Every range starts and ends at an ASCII delimiter or an end of the string, so each slice is whole UTF-8.
        internal string Text(ByteRange range) => Encoding.UTF8.GetString(Utf8, range.Lower, range.Count);

        internal ReadOnlySpan<byte> Bytes(ByteRange range) => Utf8.AsSpan(range.Lower, range.Count);

        internal string? Scheme => SchemeRange is { } r ? Text(r) : null; // :69-74
        internal string? User => UserRange is { } r ? Text(r) : null; // :111-116
        internal string? Password => PasswordRange is { } r ? Text(r) : null; // :118-123
        internal string? Host => HostRange is { } r ? Text(r) : null; // :125-130
        internal string? PortString => PortRange is { } r ? Text(r) : null; // :132-137
        internal long? Port => PortString is { } s ? SwiftNumber.ParseInt(s) : null; // :139-144 Int(portString)
        internal string Path => PathRange is { } r ? Text(r) : ""; // :146-151
        internal string? Query => QueryRange is { } r ? Text(r) : null; // :153-158
        internal string? Fragment => FragmentRange is { } r ? Text(r) : null; // :160-165
    }

    /// <summary>URLParser.swift:168-183 <c>URLBufferParseInfo</c>.</summary>
    internal struct URLBufferParseInfo
    {
        internal URLParseInfo.ByteRange? SchemeRange;
        internal URLParseInfo.ByteRange? UserRange;
        internal URLParseInfo.ByteRange? PasswordRange;
        internal URLParseInfo.ByteRange? HostRange;
        internal URLParseInfo.ByteRange? PortRange;
        internal URLParseInfo.ByteRange? PathRange;
        internal URLParseInfo.ByteRange? QueryRange;
        internal URLParseInfo.ByteRange? FragmentRange;
        internal bool IsIPLiteral;
        internal bool DidPercentEncodeHost;
        internal bool PathHasFileID;
    }

    /// <summary>
    /// URLParser.swift:185-198. The Darwin framework build returns UIDNAHookICU (:190-193), never nil, so every
    /// <c>_uidnaHook() != nil</c> test in the parser is true and the hook's calls go to <see cref="UIDNAHookICU"/>.
    /// </summary>
    internal static class RFC3986Parser
    {
        // ---- Encoding (URLParser.swift:202-337) ----

        // :204-227
        internal static string? PercentEncode(string? text, URLComponent component, bool skipAlreadyEncoded = false)
        {
            if (text is null) return null;
            if (text.Length == 0) return "";
            var bytes = Encoding.UTF8.GetBytes(text);
            return component switch
            {
                URLComponent.Scheme => throw SwiftTraps.Fatal("Scheme cannot be percent-encoded."),
                URLComponent.User => AddingPercentEncoding(bytes, URLComponentSet.User, skipAlreadyEncoded),
                URLComponent.Password => AddingPercentEncoding(bytes, URLComponentSet.Password, skipAlreadyEncoded),
                URLComponent.Host => PercentEncodeHost(bytes, skipAlreadyEncoded),
                URLComponent.Port => throw SwiftTraps.Fatal("Port cannot be percent-encoded."),
                URLComponent.Path => PercentEncodePath(bytes, skipAlreadyEncoded),
                URLComponent.Query => AddingPercentEncoding(bytes, URLComponentSet.Query, skipAlreadyEncoded),
                URLComponent.QueryItem => AddingPercentEncoding(bytes, URLComponentSet.QueryItem, skipAlreadyEncoded),
                URLComponent.Fragment => AddingPercentEncoding(bytes, URLComponentSet.Fragment, skipAlreadyEncoded),
                _ => throw new ArgumentOutOfRangeException(nameof(component)),
            };
        }

        // :229-233
        internal static string? PercentDecode(string? text, ReadOnlySpan<byte> excluding = default)
        {
            if (text is null) return null;
            if (text.Length == 0) return "";
            return RemovingURLPercentEncoding(Encoding.UTF8.GetBytes(text), excluding);
        }

        // :235-253
        private static readonly string[] SchemesToPercentEncodeHost =
        [
            "tel",
            "telemergencycall",
            "telprompt",
            "callto",
            "facetime",
            "facetime-prompt",
            "facetime-audio",
            "facetime-audio-prompt",
            "imap",
            "pop",
            "addressbook",
            "contact",
            "phasset",
            "http+unix",
            "https+unix",
            "ws+unix",
            "wss+unix",
        ];

        // :255-262
        private static bool LooksLikeIPLiteral(ReadOnlySpan<byte> host) =>
            host.Length > 0 && host[0] == (byte)'[' && host[^1] == (byte)']';

        // :264-278; :265-268 is never taken, the hook exists (:190-193)
        private static bool ShouldPercentEncodeHost(ReadOnlySpan<byte> host, string? scheme)
        {
            if (LooksLikeIPLiteral(host)) return true; // :269-272
            if (scheme is null) return false; // :273-275
            var lowercased = SwiftString.Lowercased(scheme); // :276
            foreach (var candidate in SchemesToPercentEncodeHost)
                if (candidate == lowercased) return true; // :277 elementsEqual over UTF-8
            return false;
        }

        // :280-298
        private static string? PercentEncodeIPLiteralHost(ReadOnlySpan<byte> host)
        {
            if (!LooksLikeIPLiteral(host)) throw SwiftTraps.Fatal("precondition failed"); // :281
            var percentIndex = host.IndexOf((byte)'%');
            if (percentIndex < 0)
            {
                // :284-290 the "%" delimiter and zone ID are the only parts to encode
                if (!ValidateHost(host, knownIPLiteral: true)) return null;
                return Encoding.UTF8.GetString(host);
            }
            var endBracketIndex = host.Length - 1; // :291
            // :293-297
            var zonePart = AddingPercentEncoding(host[percentIndex..endBracketIndex], URLComponentSet.HostZoneID);
            return Encoding.UTF8.GetString(host[..percentIndex]) + zonePart + "]";
        }

        // :300-311
        private static string? PercentEncodeHost(ReadOnlySpan<byte> host, bool skipAlreadyEncoded = false)
        {
            if (host.Length == 0) return "";
            if (LooksLikeIPLiteral(host))
            {
                if (skipAlreadyEncoded)
                {
                    // :304-307 String(decoding:as: UTF8.self) of the bytes between the brackets
                    var innerHost = SwiftText.DecodeUtf8Repairing(host[1..^1]);
                    return "[" + AddingPercentEncoding(Encoding.UTF8.GetBytes(innerHost), URLComponentSet.Host, skipAlreadyEncoded: true) + "]";
                }
                return PercentEncodeIPLiteralHost(host); // :308
            }
            return AddingPercentEncoding(host, URLComponentSet.Host, skipAlreadyEncoded); // :310
        }

        // :313-317
        private static string? IDNAEncodeHost(string? host)
        {
            if (host is null) return null;
            if (host.Length == 0) return "";
            return UIDNAHookICU.Encode(host);
        }

        // :319-324
        internal static string? IDNADecodeHost(string? host)
        {
            if (host is null) return null;
            if (host.Length == 0) return "";
            return UIDNAHookICU.Decode(host);
        }

        // :326-337
        private static string PercentEncodePath(ReadOnlySpan<byte> path, bool skipAlreadyEncoded = false)
        {
            if (path.Length == 0) return "";
            var slashIndex = path.IndexOf((byte)'/');
            if (slashIndex < 0) return AddingPercentEncoding(path, URLComponentSet.PathFirstSegment, skipAlreadyEncoded);
            if (slashIndex == 0) return AddingPercentEncoding(path, URLComponentSet.Path, skipAlreadyEncoded);
            var firstSegment = AddingPercentEncoding(path[..slashIndex], URLComponentSet.PathFirstSegment, skipAlreadyEncoded);
            var remaining = AddingPercentEncoding(path[slashIndex..], URLComponentSet.Path, skipAlreadyEncoded);
            return firstSegment + remaining;
        }

        // ---- Validation (URLParser.swift:339-585) ----

        /// <summary>
        /// URLParser.swift:341-363 <c>validate(_:component:percentEncodingAllowed:)</c>, the string entry URLComponents'
        /// setters call; nil is valid (:342). A native String's UTF-8 is contiguous (:366-368).
        /// </summary>
        internal static bool Validate(string? text, URLComponent component, bool percentEncodingAllowed = true)
        {
            if (text is null) return true; // :342
            var utf8 = Encoding.UTF8.GetBytes(text);
            return component switch
            {
                URLComponent.Scheme => ValidateSchemeBuffer(utf8), // :420-429 an ALPHA first, then the scheme set
                URLComponent.User => Validate(utf8, URLComponentSet.User, percentEncodingAllowed), // :431-433
                URLComponent.Password => Validate(utf8, URLComponentSet.Password, percentEncodingAllowed), // :435-437
                URLComponent.Host => ValidateHost(utf8), // :471-476
                URLComponent.Port => ValidatePortBuffer(utf8, ReadOnlySpan<byte>.Empty, hasScheme: false), // :506-510 digits only
                URLComponent.Path => Validate(utf8, URLComponentSet.Path, percentEncodingAllowed), // :512-514
                URLComponent.Query => Validate(utf8, URLComponentSet.Query, percentEncodingAllowed), // :516-518
                URLComponent.QueryItem => Validate(utf8, URLComponentSet.QueryItem, percentEncodingAllowed), // :520-522
                URLComponent.Fragment => Validate(utf8, URLComponentSet.Fragment, percentEncodingAllowed), // :524-526
                _ => throw new ArgumentOutOfRangeException(nameof(component)),
            };
        }

        // :365-406 over UTF-8 code units (withContiguousStorageIfAvailable on a native String)
        private static bool Validate(ReadOnlySpan<byte> buffer, byte component, bool percentEncodingAllowed = true)
        {
            if (!percentEncodingAllowed)
            {
                // :382-384
                foreach (var b in buffer)
                    if (b >= 128 || !IsAllowedIn(b, component)) return false;
                return true;
            }
            var hexDigitsRequired = 0;
            foreach (var v in buffer)
            {
                if (v >= 128) return false; // :387-389
                if (v == (byte)'%')
                {
                    if (hexDigitsRequired != 0) return false; // :391-393
                    hexDigitsRequired = 2;
                }
                else if (!IsAllowedIn(v, component))
                {
                    return false; // :395-396
                }
                else if (hexDigitsRequired > 0)
                {
                    if (!IsValidHexDigit(v)) return false; // :398-401 a "%" with zero or one hex digit
                    hexDigitsRequired--;
                }
            }
            return hexDigitsRequired == 0; // :405
        }

        // :409-418 the fast path used during the initial buffer parse
        private static bool ValidateSchemeBuffer(ReadOnlySpan<byte> schemeBuffer, bool allowEmptyScheme = false)
        {
            if (schemeBuffer.Length == 0) return allowEmptyScheme;
            return schemeBuffer[0] >= (byte)'A' && Validate(schemeBuffer, URLComponentSet.Scheme, percentEncodingAllowed: false);
        }

        // :431-437
        private static bool ValidateUser(ReadOnlySpan<byte> user) => Validate(user, URLComponentSet.User);

        private static bool ValidatePassword(ReadOnlySpan<byte> password) => Validate(password, URLComponentSet.Password);

        // :439-469
        private static bool ValidateIPLiteralHost(ReadOnlySpan<byte> host)
        {
            if (!LooksLikeIPLiteral(host)) throw SwiftTraps.Fatal("precondition failed"); // :443
            var innerHostEnd = host.Length - 1; // :446-448
            var percentIndex = host.IndexOf((byte)'%');
            if (percentIndex < 0)
            {
                // :450-453 no zone ID: the whole inner host is the address
                return Validate(host[1..innerHostEnd], URLComponentSet.HostIPvFuture, percentEncodingAllowed: false);
            }
            // :455-466 the first "%" must be the delimiter written "%25"
            var oneAfterIndex = percentIndex + 1;
            if (oneAfterIndex == host.Length || host[oneAfterIndex] != (byte)'2') return false;
            var twoAfterIndex = oneAfterIndex + 1;
            if (twoAfterIndex == host.Length || host[twoAfterIndex] != (byte)'5') return false;
            // :468
            return Validate(host[1..percentIndex], URLComponentSet.HostIPvFuture, percentEncodingAllowed: false)
                && Validate(host[(twoAfterIndex + 1)..innerHostEnd], URLComponentSet.HostZoneID);
        }

        // :471-476
        private static bool ValidateHost(ReadOnlySpan<byte> host, bool knownIPLiteral = false)
        {
            if (knownIPLiteral || LooksLikeIPLiteral(host)) return ValidateIPLiteralHost(host);
            return Validate(host, URLComponentSet.Host);
        }

        // :478-490
        private static bool ShouldIgnorePort(ReadOnlySpan<byte> schemeBuffer)
        {
            var schemeToIgnore = "addressbook"u8;
            if (schemeBuffer.Length != schemeToIgnore.Length) return false;
            for (var i = 0; i < schemeBuffer.Length; i++)
                if (AsciiLowercased(schemeBuffer[i]) != schemeToIgnore[i]) return false;
            return true;
        }

        // :492-504
        private static bool ValidatePortBuffer(ReadOnlySpan<byte> portBuffer, ReadOnlySpan<byte> schemeBuffer, bool hasScheme)
        {
            var isValid = true;
            foreach (var b in portBuffer)
                if (b is < (byte)'0' or > (byte)'9') { isValid = false; break; }
            if (isValid) return true;
            if (hasScheme) return ShouldIgnorePort(schemeBuffer);
            return false;
        }

        // :512-526
        private static bool ValidatePath(ReadOnlySpan<byte> path) => Validate(path, URLComponentSet.Path);

        private static bool ValidateQuery(ReadOnlySpan<byte> query) => Validate(query, URLComponentSet.Query);

        private static bool ValidateFragment(ReadOnlySpan<byte> fragment) => Validate(fragment, URLComponentSet.Fragment);

        // :562-585
        private static URLParseInfo.EncodedComponentSet InvalidComponents(URLParseInfo parseInfo)
        {
            var invalidComponents = URLParseInfo.EncodedComponentSet.None;
            if (parseInfo.UserRange is { } user && !ValidateUser(parseInfo.Bytes(user)))
                invalidComponents |= URLParseInfo.EncodedComponentSet.User;
            if (parseInfo.PasswordRange is { } password && !ValidatePassword(parseInfo.Bytes(password)))
                invalidComponents |= URLParseInfo.EncodedComponentSet.Password;
            if (parseInfo.HostRange is { } host && !ValidateHost(parseInfo.Bytes(host), knownIPLiteral: parseInfo.IsIPLiteral))
                invalidComponents |= URLParseInfo.EncodedComponentSet.Host;
            var path = parseInfo.PathRange is { } p ? parseInfo.Bytes(p) : ReadOnlySpan<byte>.Empty; // :574
            if (!ValidatePath(path))
                invalidComponents |= URLParseInfo.EncodedComponentSet.Path;
            if (parseInfo.QueryRange is { } query && !ValidateQuery(parseInfo.Bytes(query)))
                invalidComponents |= URLParseInfo.EncodedComponentSet.Query;
            if (parseInfo.FragmentRange is { } fragment && !ValidateFragment(parseInfo.Bytes(fragment)))
                invalidComponents |= URLParseInfo.EncodedComponentSet.Fragment;
            return invalidComponents;
        }

        // ---- Parsing (URLParser.swift:587-958) ----

        // URL.swift:675
        private static ReadOnlySpan<byte> FileIDPrefix => "/.file/id="u8;

        /// <summary>URLParser.swift:629-731 <c>parse(urlString:encodingInvalidCharacters:allowEmptyScheme:)</c>.</summary>
        internal static URLParseInfo? Parse(string urlString, bool encodingInvalidCharacters, bool allowEmptyScheme = false)
        {
            // :632-634 is #if os(Windows) and not in the macOS build: "\" is not rewritten (Oracle O-39).
            // UNVERIFIED O-39: the macOS 26 binary leaves "\" to be percent-encoded as %5C, as this tag reads.
            if (ParseString(urlString, allowEmptyScheme) is not { } parseInfo) return null; // :635-637

            if (!encodingInvalidCharacters)
            {
                // :639-644 (URL(string:) always encodes; kept for the shape of the source)
                return InvalidComponents(parseInfo) == URLParseInfo.EncodedComponentSet.None ? parseInfo : null;
            }

            var invalidComponents = InvalidComponents(parseInfo); // :646
            if (invalidComponents == URLParseInfo.EncodedComponentSet.None) return parseInfo; // :647-649

            // :651-653 one or more components were invalid: encode them
            var finalURLString = new StringBuilder();

            if (parseInfo.Scheme is { } scheme) finalURLString.Append(scheme).Append(':'); // :655-657

            if (parseInfo.HasAuthority) finalURLString.Append("//"); // :659-661

            if (parseInfo.User is { } user)
            {
                // :663-679
                finalURLString.Append(invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.User)
                    ? SwiftTraps.Unwrap(PercentEncode(user, URLComponent.User))
                    : user);
                if (parseInfo.Password is { } password)
                {
                    finalURLString.Append(':').Append(invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.Password)
                        ? SwiftTraps.Unwrap(PercentEncode(password, URLComponent.Password))
                        : password);
                }
                finalURLString.Append('@');
            }

            if (parseInfo.HostRange is { } hostRange)
            {
                // :681-701
                var host = parseInfo.Text(hostRange);
                var hostBytes = parseInfo.Bytes(hostRange);
                if (!invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.Host))
                {
                    finalURLString.Append(host);
                }
                else if (parseInfo.IsIPLiteral || ShouldPercentEncodeHost(hostBytes, parseInfo.Scheme))
                {
                    if (PercentEncode(host, URLComponent.Host) is not { } percentEncodedHost) return null; // :685-687
                    if (parseInfo.IsIPLiteral)
                    {
                        // :688-693 the IP literal may still be invalid after encoding the zone ID
                        if (!ValidateHost(Encoding.UTF8.GetBytes(percentEncodedHost), knownIPLiteral: true)) return null;
                    }
                    finalURLString.Append(percentEncodedHost);
                }
                else if (IDNAEncodeHost(host) is { } idnaEncoded
                         && ValidateHost(Encoding.UTF8.GetBytes(idnaEncoded), knownIPLiteral: false))
                {
                    finalURLString.Append(idnaEncoded); // :695-697
                }
                else
                {
                    return null; // :698-700
                }
            }

            if (parseInfo.Port is { } port) finalURLString.Append(':').Append(port.ToString(CultureInfo.InvariantCulture)); // :703-705

            var path = parseInfo.Path; // :707-712
            finalURLString.Append(invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.Path)
                ? SwiftTraps.Unwrap(PercentEncode(path, URLComponent.Path))
                : path);

            if (parseInfo.Query is { } query)
            {
                // :714-720
                finalURLString.Append('?').Append(invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.Query)
                    ? SwiftTraps.Unwrap(PercentEncode(query, URLComponent.Query))
                    : query);
            }

            if (parseInfo.Fragment is { } fragment)
            {
                // :722-728
                finalURLString.Append('#').Append(invalidComponents.HasFlag(URLParseInfo.EncodedComponentSet.Fragment)
                    ? SwiftTraps.Unwrap(PercentEncode(fragment, URLComponent.Fragment))
                    : fragment);
            }

            return ParseString(finalURLString.ToString(), allowEmptyScheme, invalidComponents); // :730
        }

        // :733-767
        private static URLParseInfo? ParseString(string urlString, bool allowEmptyScheme = false,
            URLParseInfo.EncodedComponentSet encodedComponents = URLParseInfo.EncodedComponentSet.None)
        {
            var utf8 = Encoding.UTF8.GetBytes(urlString);
            if (ParseBuffer(utf8, allowEmptyScheme) is not { } bufferParseInfo) return null;
            // A Swift String holds no lone surrogate: the text the ranges index is the UTF-8 decoded back.
            return new URLParseInfo(Encoding.UTF8.GetString(utf8), utf8, bufferParseInfo, encodedComponents);
        }

        // :769-909 parses on delimiters only; no encoding
        private static URLBufferParseInfo? ParseBuffer(ReadOnlySpan<byte> buffer, bool allowEmptyScheme = false)
        {
            var parseInfo = new URLBufferParseInfo();
            if (buffer.Length == 0)
            {
                // :777-781 the path always exists, even if empty
                parseInfo.PathRange = new URLParseInfo.ByteRange(0, 0);
                return parseInfo;
            }

            var currentIndex = 0;

            // :785-819 Scheme
            while (currentIndex != buffer.Length)
            {
                var v = buffer[currentIndex];
                if (v == (byte)':')
                {
                    // :790-803 a scheme needs one character unless an empty scheme is allowed
                    if (currentIndex != 0 || allowEmptyScheme)
                    {
                        parseInfo.SchemeRange = new URLParseInfo.ByteRange(0, currentIndex);
                        currentIndex++;
                        if (currentIndex == buffer.Length)
                        {
                            if (!ValidateSchemeBuffer(buffer[..(currentIndex - 1)], allowEmptyScheme)) return null;
                            parseInfo.PathRange = new URLParseInfo.ByteRange(buffer.Length, buffer.Length); // :799-801
                            return parseInfo;
                        }
                    }
                    break;
                }
                if (v is (byte)'/' or (byte)'?' or (byte)'#' or (byte)'[' or (byte)']' or (byte)'@')
                {
                    // :805-816 no scheme: a relative-ref ("[", "]" and "@" hint at an authority, rdar://94076763)
                    currentIndex = 0;
                    break;
                }
                currentIndex++;
            }

            if (parseInfo.SchemeRange is { } schemeRange
                && !ValidateSchemeBuffer(buffer[schemeRange.Lower..schemeRange.Upper], allowEmptyScheme))
                return null; // :821-825

            if (currentIndex == buffer.Length) currentIndex = 0; // :827-830 no scheme found

            // :832-871 Authority
            var doubleSlashExists = currentIndex + 1 != buffer.Length
                && buffer[currentIndex] == (byte)'/'
                && buffer[currentIndex + 1] == (byte)'/';
            if (doubleSlashExists)
            {
                currentIndex += 2;
                var authorityStartIndex = currentIndex;
                while (currentIndex != buffer.Length)
                {
                    var v = buffer[currentIndex];
                    if (v is (byte)'/' or (byte)'?' or (byte)'#') break;
                    currentIndex++;
                }
                var authorityRange = new URLParseInfo.ByteRange(authorityStartIndex, currentIndex);
                if (authorityRange.Count == 0)
                {
                    parseInfo.HostRange = authorityRange; // :852-854 the host exists but is empty
                }
                else
                {
                    if (!ParseAuthority(buffer, authorityRange, ref parseInfo, allowEmptyScheme)) return null; // :857-860
                    if (parseInfo.PortRange is { } portRange)
                    {
                        // :861-869
                        var hasScheme = parseInfo.SchemeRange is not null;
                        var schemeBuffer = parseInfo.SchemeRange is { } s ? buffer[s.Lower..s.Upper] : ReadOnlySpan<byte>.Empty;
                        if (!ValidatePortBuffer(buffer[portRange.Lower..portRange.Upper], schemeBuffer, hasScheme)) return null;
                    }
                }
            }

            // :873-887 Path
            var pathStartIndex = currentIndex;
            if (buffer[pathStartIndex..].StartsWith(FileIDPrefix))
            {
                parseInfo.PathHasFileID = true;
                currentIndex = pathStartIndex + FileIDPrefix.Length;
            }
            while (currentIndex != buffer.Length)
            {
                var v = buffer[currentIndex];
                if (v is (byte)'?' or (byte)'#') break;
                currentIndex++;
            }
            parseInfo.PathRange = new URLParseInfo.ByteRange(pathStartIndex, currentIndex);

            if (currentIndex == buffer.Length) return parseInfo; // :889-891

            // :893-906 Query and Fragment
            if (buffer[currentIndex] == (byte)'?')
            {
                var queryStartIndex = currentIndex + 1;
                var pound = buffer[queryStartIndex..].IndexOf((byte)'#');
                if (pound >= 0)
                {
                    var poundIndex = queryStartIndex + pound;
                    parseInfo.QueryRange = new URLParseInfo.ByteRange(queryStartIndex, poundIndex);
                    parseInfo.FragmentRange = new URLParseInfo.ByteRange(poundIndex + 1, buffer.Length);
                }
                else
                {
                    parseInfo.QueryRange = new URLParseInfo.ByteRange(currentIndex + 1, buffer.Length);
                }
            }
            else if (buffer[currentIndex] == (byte)'#')
            {
                parseInfo.FragmentRange = new URLParseInfo.ByteRange(currentIndex + 1, buffer.Length);
            }

            return parseInfo; // :908
        }

        // :911-958 user, password, host and port of a non-empty authority
        private static bool ParseAuthority(ReadOnlySpan<byte> buffer, URLParseInfo.ByteRange authorityRange,
            ref URLBufferParseInfo parseInfo, bool allowEmptyScheme)
        {
            var start = authorityRange.Lower;
            var end = authorityRange.Upper;
            var authority = buffer[start..end];
            var hostStartIndex = start;
            var hostEndIndex = end;

            // :917-930 the last "@", as WHATWG and other RFC 3986 parsers use
            var at = authority.LastIndexOf((byte)'@');
            if (at >= 0)
            {
                var atIndex = start + at;
                var colon = authority[..at].IndexOf((byte)':');
                if (colon >= 0)
                {
                    parseInfo.UserRange = new URLParseInfo.ByteRange(start, start + colon);
                    parseInfo.PasswordRange = new URLParseInfo.ByteRange(start + colon + 1, atIndex);
                }
                else
                {
                    parseInfo.UserRange = new URLParseInfo.ByteRange(start, atIndex);
                }
                hostStartIndex = atIndex + 1;
            }

            // :932-952 Host and Port
            var endBracket = hostStartIndex != end && buffer[hostStartIndex] == (byte)'['
                ? buffer[hostStartIndex..end].IndexOf((byte)']')
                : -1;
            if (endBracket >= 0)
            {
                parseInfo.IsIPLiteral = true;
                hostEndIndex = hostStartIndex + endBracket + 1;
                if (hostEndIndex != end)
                {
                    if (buffer[hostEndIndex] != (byte)':') return false; // :939-942 text after the IP literal
                    parseInfo.PortRange = new URLParseInfo.ByteRange(hostEndIndex + 1, end);
                }
            }
            else
            {
                var colon = buffer[hostStartIndex..end].IndexOf((byte)':');
                if (colon >= 0)
                {
                    var colonIndex = hostStartIndex + colon;
                    hostEndIndex = colonIndex;
                    // :947-951 a port exists if non-empty, or when an empty scheme is allowed
                    if (colonIndex + 1 != end || allowEmptyScheme)
                        parseInfo.PortRange = new URLParseInfo.ByteRange(colonIndex + 1, end);
                }
            }

            // :954-957 the host always exists with an authority
            parseInfo.HostRange = new URLParseInfo.ByteRange(hostStartIndex, hostEndIndex);
            parseInfo.DidPercentEncodeHost = buffer[hostStartIndex..hostEndIndex].Contains((byte)'%');
            return true;
        }

        // ---- Encoding extensions (URLParser.swift:961-1133) ----

        // :963-1000 upper-case hex
        private static byte HexToAscii(int hex) => (byte)"0123456789ABCDEF"[hex];

        // :1002-1039
        private static int AsciiToHex(byte ascii) => ascii switch
        {
            >= (byte)'0' and <= (byte)'9' => ascii - '0',
            >= (byte)'A' and <= (byte)'F' => ascii - 'A' + 10,
            >= (byte)'a' and <= (byte)'f' => ascii - 'a' + 10,
            _ => -1,
        };

        private static bool IsValidHexDigit(byte b) => AsciiToHex(b) >= 0;

        internal static byte AsciiLowercased(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;

        // :1043-1080
        private static string AddingPercentEncoding(ReadOnlySpan<byte> utf8Buffer, byte component, bool skipAlreadyEncoded = false)
        {
            var output = new byte[utf8Buffer.Length * 3];
            var i = 0;
            for (var index = 0; index < utf8Buffer.Length; index++)
            {
                var v = utf8Buffer[index];
                if (IsAllowedIn(v, component))
                {
                    output[i++] = v;
                }
                else if (skipAlreadyEncoded && v == (byte)'%'
                         && index + 1 != utf8Buffer.Length && IsValidHexDigit(utf8Buffer[index + 1])
                         && index + 2 != utf8Buffer.Length && IsValidHexDigit(utf8Buffer[index + 2]))
                {
                    // :1065-1072 keep an existing escape
                    output[i++] = utf8Buffer[index];
                    output[i++] = utf8Buffer[index + 1];
                    output[i++] = utf8Buffer[index + 2];
                    index += 2;
                }
                else
                {
                    // :1073-1074
                    output[i++] = (byte)'%';
                    output[i++] = HexToAscii(v >> 4);
                    output[i++] = HexToAscii(v & 0xF);
                }
            }
            return SwiftText.DecodeUtf8Repairing(output.AsSpan(0, i)); // :1078
        }

        // :1082-1132
        private static string? RemovingURLPercentEncoding(ReadOnlySpan<byte> utf8Buffer, ReadOnlySpan<byte> excluding)
        {
            var output = new byte[utf8Buffer.Length];
            var i = 0;
            byte value = 0;
            var hexDigitsRequired = 0;
            foreach (var v in utf8Buffer)
            {
                if (v == (byte)'%')
                {
                    if (hexDigitsRequired != 0) return null; // :1100-1102
                    hexDigitsRequired = 2;
                }
                else if (hexDigitsRequired > 0)
                {
                    var hex = AsciiToHex(v);
                    if (hex < 0) return null; // :1105-1107
                    if (hexDigitsRequired == 2)
                    {
                        value = (byte)(hex << 4);
                    }
                    else if (hexDigitsRequired == 1)
                    {
                        value = (byte)(value + hex);
                        if (excluding.Contains(value))
                        {
                            // :1112-1114 keep the original encoding: "%", the high digit upper-cased, the low digit as written
                            output[i++] = (byte)'%';
                            output[i++] = HexToAscii(value >> 4);
                            output[i++] = v;
                        }
                        else
                        {
                            output[i++] = value;
                            value = 0;
                        }
                    }
                    hexDigitsRequired--;
                }
                else
                {
                    output[i++] = v;
                }
            }
            if (hexDigitsRequired != 0) return null; // :1127-1129
            return SwiftText.DecodeUtf8OrNil(output.AsSpan(0, i)); // :1130 String(bytes:encoding: .utf8)
        }

        // ---- The allowed-set table (URLParser.swift:1167-1270) ----

        // :1169-1188
        private static class URLComponentSet
        {
            internal const byte Scheme = 1 << 0;
            internal const byte User = 1 << 1; // user, password and hostIPvFuture share a set
            internal const byte Password = 1 << 1;
            internal const byte HostIPvFuture = 1 << 1;
            internal const byte Host = 1 << 2;
            internal const byte HostZoneID = 1 << 3;
            internal const byte Path = 1 << 4;
            internal const byte PathFirstSegment = 1 << 5;
            internal const byte Query = 1 << 6; // query and fragment share a set
            internal const byte Fragment = 1 << 6;
            internal const byte QueryItem = 1 << 7;
        }

        // :1191-1193
        private static bool IsAllowedIn(byte b, byte component) => (AllowedURLComponents(b) & component) != 0;

        // :1221-1270, generated from the RFC 3986 grammar at :1195-1220
        private static byte AllowedURLComponents(byte b) => b switch
        {
            (byte)'!' => 0b11110110,
            (byte)'$' => 0b11110110,
            (byte)'&' => 0b01110110,
            (byte)'\'' => 0b11110110,
            (byte)'(' => 0b11110110,
            (byte)')' => 0b11110110,
            (byte)'*' => 0b11110110,
            (byte)'+' => 0b11110111,
            (byte)',' => 0b11110110,
            (byte)'-' => 0b11111111,
            (byte)'.' => 0b11111111,
            (byte)'/' => 0b11110000,
            >= (byte)'0' and <= (byte)'9' => 0b11111111,
            (byte)':' => 0b11010010,
            (byte)';' => 0b11110110,
            (byte)'=' => 0b01110110,
            (byte)'?' => 0b11000000,
            (byte)'@' => 0b11110000,
            >= (byte)'A' and <= (byte)'Z' => 0b11111111,
            (byte)'_' => 0b11111110,
            >= (byte)'a' and <= (byte)'z' => 0b11111111,
            (byte)'~' => 0b11111110,
            _ => 0,
        };
    }

    // DEVIATION ED-073: IDNA runs on the ICU in Windows' icu.dll, not Apple's; UTS #46 data may differ by ICU version (O-40).
    /// <summary>
    /// FoundationInternationalization URLParser+ICU.swift:28-221 <c>UIDNAHookICU</c>, the UTF-8 path a native String
    /// takes (:194-200). The ICU is Windows' icu.dll, not Apple's (Oracle O-40).
    /// </summary>
    internal static class UIDNAHookICU
    {
        // ICU uidna.h option and error bits
        private const uint UIDNA_CHECK_BIDI = 4;
        private const uint UIDNA_CHECK_CONTEXTJ = 8;
        private const uint UIDNA_NONTRANSITIONAL_TO_ASCII = 0x10;
        private const uint UIDNA_NONTRANSITIONAL_TO_UNICODE = 0x20;
        private const uint UIDNA_ERROR_EMPTY_LABEL = 1;
        private const uint UIDNA_ERROR_LABEL_TOO_LONG = 2;
        private const uint UIDNA_ERROR_DOMAIN_NAME_TOO_LONG = 4;
        private const uint UIDNA_ERROR_LEADING_HYPHEN = 8;
        private const uint UIDNA_ERROR_TRAILING_HYPHEN = 0x10;
        private const uint UIDNA_ERROR_HYPHEN_3_4 = 0x20;

        // :35-37 U_SUCCESS: warnings (negative codes) succeed
        private static bool USuccess(int x) => x <= 0;

        // :39-52 opened once and never closed
        private static readonly Lazy<IntPtr?> IdnaTranscoder = new(() =>
        {
            var status = 0;
            var options = UIDNA_CHECK_BIDI | UIDNA_CHECK_CONTEXTJ | UIDNA_NONTRANSITIONAL_TO_UNICODE | UIDNA_NONTRANSITIONAL_TO_ASCII;
            var encoder = IcuNative.UidnaOpenUTS46(options, ref status);
            if (!USuccess(status)) return null;
            return encoder;
        });

        // :54-69
        private static bool ShouldAllow(uint errors, bool encodeToASCII)
        {
            var allowedErrors = encodeToASCII
                ? 0u
                : UIDNA_ERROR_EMPTY_LABEL | UIDNA_ERROR_LABEL_TOO_LONG | UIDNA_ERROR_DOMAIN_NAME_TOO_LONG
                  | UIDNA_ERROR_LEADING_HYPHEN | UIDNA_ERROR_TRAILING_HYPHEN | UIDNA_ERROR_HYPHEN_3_4;
            return (errors & ~allowedErrors) == 0;
        }

        // :74-156 IDNACodedHost over UTF-8 (IDNACodedHostUTF8 with its createString)
        private static string? IDNACodedHostUTF8(byte[] utf8Buffer, bool encodeToASCII)
        {
            const int maxHostBufferLength = 2048; // :80
            if (utf8Buffer.Length > maxHostBufferLength) return null; // :81-83
            if (IdnaTranscoder.Value is not { } transcoder) return null; // :85-87

            var outBuffer = new byte[maxHostBufferLength]; // :89
            var processingDetails = new IcuNative.UIDNAInfo { Size = (short)IcuNative.UIDNAInfo.ByteSize }; // :90-97
            var error = 0; // :98
            var charsConverted = encodeToASCII // :103-111, :122-125
                ? IcuNative.UidnaNameToASCIIUTF8(transcoder, utf8Buffer, utf8Buffer.Length, outBuffer, outBuffer.Length, ref processingDetails, ref error)
                : IcuNative.UidnaNameToUnicodeUTF8(transcoder, utf8Buffer, utf8Buffer.Length, outBuffer, outBuffer.Length, ref processingDetails, ref error);

            if (!(USuccess(error) && ShouldAllow(processingDetails.Errors, encodeToASCII) && charsConverted > 0)) return null; // :113-116

            // :133-153 keep the input when the output only lower-cased ASCII letters
            var output = outBuffer.AsSpan(0, charsConverted);
            var hostsAreEqual = false;
            if (output.Length == utf8Buffer.Length)
            {
                hostsAreEqual = true;
                for (var i = 0; i < output.Length; i++)
                {
                    if (utf8Buffer[i] == output[i]) continue;
                    if (RFC3986Parser.AsciiLowercased(utf8Buffer[i]) != output[i])
                    {
                        hostsAreEqual = false;
                        break;
                    }
                }
            }
            return hostsAreEqual ? SwiftText.DecodeUtf8OrNil(utf8Buffer) : SwiftText.DecodeUtf8OrNil(output); // String._tryFromUTF8
        }

        // :213-215
        internal static string? Encode(string host) => IDNACodedHostUTF8(Encoding.UTF8.GetBytes(host), encodeToASCII: true);

        // :217-219
        internal static string? Decode(string host) => IDNACodedHostUTF8(Encoding.UTF8.GetBytes(host), encodeToASCII: false);
    }
}
