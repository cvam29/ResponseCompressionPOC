using System.Runtime.CompilerServices;

/// <summary>
/// BBernard / CajunCoding (MIT License)
/// Original Gist: https://gist.github.com/cajuncoding/a4a7b590986fd848b5040da83979796c
/// Extension method for the HttpContext & HttpRequest to enable response compression within Azure Functions using the In Process Model -- which
///     does not support custom Middleware so it cannot easily intercept & handle all requests.
/// There are many issues reported with response compression not always working, particularly with Linux deployments,
///     therefore this helps to easily enable this for any request by simply calling the extension method in the Function invocation.
/// It works by simply inspecting the AcceptEncoding header to determine which, if any, compression encodings are supported (Gzip, Brotli, Deflate)
///     and then wrapping the Response Body Stream with the correct implementation to encode the response while also setting the correct Response Header
///     for the Client to correctly decode the response.
///
/// This works great with GraphQL via Azure Functions (In Process Model) using HotChocolate GraphQL server allowing for compression of large
///     GraphQL Query results which can significantly improve performance in use cases with large query result sets.
/// </summary>


namespace ResponseCompression.Extensions;

internal static class AzureFunctionsInProcessCompressionExtensions
{
    private const string CompressionStreamKey = "CompressionStream";
    private const string CompressionCancellationTokenKey = "CompressionCancellationToken";

    private static readonly FrozenSet<string> SupportedEncodings = new[] { "br", "gzip", "deflate" }.ToFrozenSet();
    private static readonly string[] EncodingPriority = ["br", "gzip", "deflate"];
    private static readonly ConcurrentDictionary<string, string[]> EncodingCache = new();
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    public static HttpRequest EnableResponseCompression(this HttpRequest request, CancellationToken cancellationToken = default) =>
        request.HttpContext.EnableResponseCompression(CompressionLevel.Fastest, cancellationToken).Request;

    public static HttpContext EnableResponseCompression(this HttpContext context, CompressionLevel level = CompressionLevel.Fastest, CancellationToken cancellationToken = default)
    {
        context.Items[CompressionCancellationTokenKey] = cancellationToken;

        var acceptEncoding = context.Request.Headers["Accept-Encoding"];
        if (acceptEncoding.Count == 0) return context;

        var bestEncoding = GetBestSupportedEncoding(acceptEncoding);
        if (bestEncoding is null) return context;

        var response = context.Response;
        var originalBody = response.Body;

        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>()?.Let(f => f.AllowSynchronousIO = true);

        var compressedStream = CreateCompressionStream(originalBody, bestEncoding, level);
        if (compressedStream is not null)
        {
            response.Headers["Content-Encoding"] = new StringValues(bestEncoding);
            response.Body = compressedStream;
            context.Items[CompressionStreamKey] = compressedStream;
        }

        return context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestSupportedEncoding(StringValues headers)
    {
        return headers.Count == 1
            ? GetBestSupportedEncodingFromSingle(headers[0])
            : GetBestSupportedEncodingFromSingle(string.Join(",", headers));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestSupportedEncodingFromSingle(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (!EncodingCache.TryGetValue(value, out var parsed))
        {
            parsed = ParseAcceptEncodingHeader(value.AsSpan());
            EncodingCache.TryAdd(value, parsed);
        }

        foreach (var encoding in EncodingPriority)
            if (parsed.Contains(encoding)) return encoding;

        return null;
    }

    private static string[] ParseAcceptEncodingHeader(ReadOnlySpan<char> span)
    {
        var encodings = new List<string>(3);
        var buffer = CharPool.Rent(64);

        try
        {
            int start = 0;

            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == ',')
                {
                    if (i > start)
                    {
                        var slice = span.Slice(start, i - start);
                        var semicolon = slice.IndexOf(';');
                        if (semicolon >= 0) slice = slice.Slice(0, semicolon);

                        slice = slice.Trim();
                        if (slice.Length == 0 || slice.Length > buffer.Length)
                        {
                            start = i + 1;
                            continue;
                        }

                        slice.CopyTo(buffer);
                        var lowered = buffer.AsSpan(0, slice.Length);
                        for (int j = 0; j < lowered.Length; j++)
                            lowered[j] = char.ToLowerInvariant(lowered[j]);

                        var encoding = new string(lowered.Slice(0, slice.Length));

                        if (SupportedEncodings.Contains(encoding))
                            encodings.Add(encoding);
                    }
                    start = i + 1;
                }
            }
        }
        finally
        {
            CharPool.Return(buffer);
        }

        return encodings.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Stream CreateCompressionStream(Stream originalBody, string encoding, CompressionLevel level) =>
        encoding switch
        {
            "gzip" => new GZipStream(originalBody, level, leaveOpen: true),
            "br" => new BrotliStream(originalBody, level, leaveOpen: true),
            "deflate" => new DeflateStream(originalBody, level, leaveOpen: true),
            _ => null
        };

    public static ValueTask FinalizeCompressionAsync(this HttpRequest request) =>
        request.HttpContext.FinalizeCompressionAsync();

    public static async ValueTask FinalizeCompressionAsync(this HttpContext context)
    {
        if (context.Items.TryGetValue(CompressionStreamKey, out var streamObj) && streamObj is Stream compressionStream)
        {
            var cancellationToken = context.Items.TryGetValue(CompressionCancellationTokenKey, out var tokenObj)
                ? (CancellationToken)tokenObj
                : CancellationToken.None;

            try
            {
                await compressionStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await compressionStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Swallow disposal exceptions */ }
            finally
            {
                context.Items.Remove(CompressionStreamKey);
                context.Items.Remove(CompressionCancellationTokenKey);
            }
        }
    }

    public static async ValueTask WriteCompressedAsync(this HttpContext context, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var stream = context.Items.TryGetValue(CompressionStreamKey, out var obj) && obj is Stream s ? s : context.Response.Body;
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask WriteCompressedTextAsync(this HttpContext context, ReadOnlyMemory<char> text, Encoding encoding = null, CancellationToken cancellationToken = default)
    {
        encoding ??= Encoding.UTF8;
        var byteBuffer = ArrayPool<byte>.Shared.Rent(encoding.GetMaxByteCount(text.Length));

        try
        {
            var byteCount = encoding.GetBytes(text.Span, byteBuffer);
            await context.WriteCompressedAsync(byteBuffer.AsMemory(0, byteCount), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Let<T>(this T obj, Action<T> action) where T : class
    {
        if (obj is not null) action(obj);
    }
}

