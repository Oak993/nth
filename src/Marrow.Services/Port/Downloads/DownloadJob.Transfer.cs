// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): the bytes of one file (sections 3.2, 3.3, 3.9).
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace Marrow.Services.Port.Downloads;

public sealed partial class DownloadJob
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// One request chain for one file, from what its partial holds: resume with Range, or start over when the server cannot; hash while it
    /// streams; verify length and SHA-256; move into place. Throws <see cref="DownloadFailureException"/> for a failure and
    /// <see cref="OperationCanceledException"/> when paused or stopped.
    /// </summary>
    private async Task<DownloadFileResult> AttemptAsync(int index, DownloadRequest file, FileState fileState, CancellationToken token)
    {
        var part = PartPath(file);
        var restartsInChain = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var existing = ExistingPartialLength(part, file);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (existing > 0) await HashFileAsync(part, hasher, token);
            if (existing == file.Size && existing > 0)
            {
                // A complete partial needs no request: verify it where it lies (section 3.3).
                if (Matches(hasher, file)) return Finish(index, file, part, file.Url.Host, resumedFrom: existing, restartedFromZero: false);
                File.Delete(part); // wrong: thrown away, not a failure; the file is fetched whole
                fileState.RestartUsed = true;
                continue;
            }
            SetReceived(index, existing);

            var askedFrom = existing;
            var (response, finalHost) = await RequestAsync(file, existing, token);
            using (response)
            {
                var status = (int)response.StatusCode;
                var resumedFrom = existing;
                if (status == 416)
                {
                    // The server has fewer bytes than the partial: start over (once per chain, so it cannot loop).
                    if (existing == 0 || ++restartsInChain > 2) throw ServerError(file, status);
                    File.Delete(part);
                    continue;
                }
                if (status is 404 or 410) throw new DownloadFailureException(new DownloadFailure(DownloadFailureKind.NotFound, DownloadMessages.NotFound,
                    HttpStatus: status), transient: false);
                if (status == 206)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (existing == 0 || range?.From != existing)
                    {
                        if (++restartsInChain > 2) throw ServerError(file, status);
                        File.Delete(part); // an answer to a different range than was asked for: start over
                        continue;
                    }
                    if ((range.Length is { } total && total != file.Size) || (response.Content.Headers.ContentLength is { } tail && existing + tail != file.Size))
                        throw MismatchAndDelete(part);
                }
                else if (status == 200)
                {
                    if (response.Content.Headers.ContentLength is { } length && length != file.Size) throw MismatchAndDelete(part);
                    if (existing > 0)
                    {
                        // Range was ignored (no Accept-Ranges): the whole file comes, so the partial restarts from zero (section 3.2).
                        hasher.GetHashAndReset();
                        existing = 0;
                        resumedFrom = 0;
                        SetReceived(index, 0);
                    }
                }
                else throw ServerError(file, status);

                var fileMode = existing > 0 ? FileMode.Append : FileMode.Create;
                await StreamBodyAsync(index, file, response, part, fileMode, existing, hasher, token);
                if (!Matches(hasher, file))
                {
                    File.Delete(part);
                    if (resumedFrom > 0 && !fileState.RestartUsed)
                    {
                        fileState.RestartUsed = true; // the one silent restart: a resumed partial was bad
                        continue;
                    }
                    throw new DownloadFailureException(new DownloadFailure(DownloadFailureKind.Mismatch, DownloadMessages.Mismatch), transient: false);
                }
                return Finish(index, file, part, finalHost, resumedFrom, restartedFromZero: status == 200 && askedFrom > 0);
            }
        }
    }

    private static bool Matches(IncrementalHash hasher, DownloadRequest file) =>
        string.Equals(Convert.ToHexStringLower(hasher.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase);

    private static long ExistingPartialLength(string part, DownloadRequest file)
    {
        if (!File.Exists(part)) return 0;
        var length = new FileInfo(part).Length;
        if (length <= file.Size) return length;
        File.Delete(part); // longer than the pinned size: it cannot be a prefix of the file
        return 0;
    }

    private static async Task HashFileAsync(string path, IncrementalHash hasher, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), token)) > 0) hasher.AppendData(buffer, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task StreamBodyAsync(int index, DownloadRequest file, HttpResponseMessage response, string part, FileMode mode, long existing,
        IncrementalHash hasher, CancellationToken token)
    {
        using var stall = new CancellationTokenSource(options.StallTimeout, clock);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(token, stall.Token);
        FileStream output;
        try
        {
            output = new FileStream(part, mode, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw DiskError();
        }
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            Stream body;
            try
            {
                body = await response.Content.ReadAsStreamAsync(both.Token);
            }
            catch (Exception error) when (IsNetwork(error))
            {
                token.ThrowIfCancellationRequested(); // a read cut short by Pause or Cancel surfaces as an IOException: it is not a lost connection
                throw Unreachable(file, DownloadCause.Interrupted);
            }
            await using (body)
            {
                var have = existing;
                while (true)
                {
                    int read;
                    try
                    {
                        read = await body.ReadAsync(buffer.AsMemory(), both.Token);
                    }
                    catch (OperationCanceledException) when (stall.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        throw Unreachable(file, DownloadCause.Timeout);
                    }
                    catch (Exception error) when (IsNetwork(error))
                    {
                        token.ThrowIfCancellationRequested();
                        throw Unreachable(file, DownloadCause.Interrupted);
                    }
                    if (read == 0) break;
                    stall.CancelAfter(options.StallTimeout);
                    try
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                    }
                    catch (IOException)
                    {
                        throw DiskError();
                    }
                    hasher.AppendData(buffer, 0, read);
                    have += read;
                    AddReceived(index, read);
                    if (have > file.Size)
                    {
                        await output.DisposeAsync();
                        throw MismatchAndDelete(part); // more bytes than the pin says
                    }
                }
                if (have != file.Size)
                {
                    await output.DisposeAsync();
                    throw MismatchAndDelete(part);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await output.DisposeAsync(); // flushes: what arrived before a cut or a pause stays on disk
        }
    }

    private DownloadFileResult Finish(int index, DownloadRequest file, string part, string finalHost, long resumedFrom, bool restartedFromZero)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(file.DestinationPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Move(part, file.DestinationPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw DiskError();
        }
        SetReceived(index, file.Size);
        return new DownloadFileResult(file.DestinationPath, file.Size, file.Sha256.ToLowerInvariant(), finalHost, resumedFrom, restartedFromZero);
    }

    // ---- the request and its redirects --------------------------------------------------------------------------------------------

    private async Task<(HttpResponseMessage Response, string FinalHost)> RequestAsync(DownloadRequest file, long from, CancellationToken token)
    {
        Interlocked.Increment(ref attempts);
        var current = file.Url;
        var hops = 0;
        while (true)
        {
            using var headerTimeout = new CancellationTokenSource(options.ConnectTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, headerTimeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            }
            catch (OperationCanceledException) when (headerTimeout.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw Unreachable(file, DownloadCause.Timeout);
            }
            catch (HttpRequestException error)
            {
                token.ThrowIfCancellationRequested();
                throw Unreachable(file, error.HttpRequestError == HttpRequestError.SecureConnectionError || error.InnerException is AuthenticationException
                    ? DownloadCause.Tls
                    : DownloadCause.Offline);
            }
            var status = (int)response.StatusCode;
            if (status is not (301 or 302 or 303 or 307 or 308))
            {
                Downloader.Log.Info($"downloading from {current.Host}");
                return (response, current.Host);
            }
            var location = response.Headers.Location;
            response.Dispose();
            if (++hops > options.MaxRedirects) throw Refusal(file, DownloadCause.TooManyRedirects, $"Nth gave up following redirects from {file.Url.Host}.");
            if (location is null) throw Refusal(file, DownloadCause.BadRedirect, $"{current.Host} sent a redirect with nowhere to go.");
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme == Uri.UriSchemeHttp) throw Refusal(file, DownloadCause.InsecureRedirect, $"Nth won't follow a redirect to plain http ({next.Host}).");
            if (next.Scheme != Uri.UriSchemeHttps) throw Refusal(file, DownloadCause.BadRedirect, $"{current.Host} sent a redirect Nth can't follow.");
            current = next;
        }
    }

    // ---- failures -------------------------------------------------------------------------------------------------------------------

    private static bool IsNetwork(Exception error) => error is IOException or HttpRequestException or System.Net.Sockets.SocketException
        || error.InnerException is IOException or System.Net.Sockets.SocketException;

    private static DownloadFailureException Unreachable(DownloadRequest file, DownloadCause cause) =>
        new(new DownloadFailure(DownloadFailureKind.Unreachable, DownloadMessages.Unreachable(file.SourceName ?? file.Url.Host), cause), transient: true);

    private static DownloadFailureException ServerError(DownloadRequest file, int status) =>
        new(new DownloadFailure(DownloadFailureKind.ServerError, DownloadMessages.Unreachable(file.SourceName ?? file.Url.Host), HttpStatus: status),
            transient: status >= 500);

    private static DownloadFailureException Refusal(DownloadRequest file, DownloadCause cause, string message) =>
        new(new DownloadFailure(DownloadFailureKind.Refused, message, cause), transient: false);

    private static DownloadFailureException DiskError() =>
        new(new DownloadFailure(DownloadFailureKind.DiskError, DownloadMessages.DiskError), transient: false);

    private static DownloadFailureException MismatchAndDelete(string part)
    {
        try
        {
            File.Delete(part);
        }
        catch (IOException)
        {
            // already gone, or held: the next run replaces it
        }
        return new DownloadFailureException(new DownloadFailure(DownloadFailureKind.Mismatch, DownloadMessages.Mismatch), transient: false);
    }

    // ---- progress -------------------------------------------------------------------------------------------------------------------

    private void AddReceived(int index, long delta)
    {
        lock (gate) received[index] += delta;
        ReportProgress();
    }

    private void SetReceived(int index, long value)
    {
        lock (gate)
        {
            if (received[index] == value) return;
            received[index] = value;
        }
        ReportProgress();
    }

    private long Sum()
    {
        lock (gate) return received.Sum();
    }

    private void ReportProgress()
    {
        var emitNow = false;
        lock (gate)
        {
            var now = clock.GetTimestamp();
            var since = emittedOnce ? clock.GetElapsedTime(lastEmitStamp, now) : TimeSpan.MaxValue;
            if (since >= options.ProgressInterval) emitNow = true;
            else trailing ??= clock.CreateTimer(_ => Emit(final: false), null, options.ProgressInterval - since, Timeout.InfiniteTimeSpan);
        }
        if (emitNow) Emit(final: false);
        Interlocked.Exchange(ref published, Sum());
    }

    private void Emit(bool final)
    {
        lock (emitGate)
        {
            DownloadProgress snapshot;
            lock (gate)
            {
                lastEmitStamp = clock.GetTimestamp();
                emittedOnce = true;
                trailing?.Dispose();
                trailing = null;
                snapshot = new DownloadProgress(received.Sum(), totalBytes, filesDone, files.Count);
            }
            if (final) Interlocked.Exchange(ref published, snapshot.BytesDone);
            callback?.Invoke(snapshot);
            ProgressChanged?.Invoke(snapshot);
        }
    }
}
