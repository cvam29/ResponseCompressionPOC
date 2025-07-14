using System.Runtime.CompilerServices;

namespace ResponseCompression.Extensions;

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

internal static class AzureFunctionsInProcessCompressionExtensions
{
    private const string CompressionStreamKey = "CompressionStream";
    private const string CompressionCancellationTokenKey = "CompressionCancellationToken";
    
    // Use FrozenSet for O(1) lookups with immutable data - .NET 8 feature
    private static readonly FrozenSet<string> SupportedEncodings = new[] { "br", "gzip", "deflate" }.ToFrozenSet();
    
    // Priority ordered encodings for better compression efficiency
    private static readonly string[] EncodingPriority = { "br", "gzip", "deflate" };
    
    // Thread-safe cache for parsed encodings to avoid re-parsing
    private static readonly ConcurrentDictionary<string, string[]> EncodingCache = new();
    
    // Reusable buffer pool for string operations
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    public static HttpRequest EnableResponseCompression(this HttpRequest request, CancellationToken cancellationToken = default)
        => request.HttpContext.EnableResponseCompression(CompressionLevel.Fastest, cancellationToken).Request;

    public static HttpContext EnableResponseCompression(this HttpContext context, CompressionLevel level = CompressionLevel.Fastest, CancellationToken cancellationToken = default)
    {
        // Store cancellation token for async operations
        context.Items[CompressionCancellationTokenKey] = cancellationToken;
        
        var acceptEncodingHeader = context.Request.Headers["Accept-Encoding"];
        if (acceptEncodingHeader.Count == 0) return context;

        // Use efficient string processing with ReadOnlySpan
        var bestEncoding = GetBestSupportedEncoding(acceptEncodingHeader);
        if (bestEncoding == null) return context;

        var response = context.Response;
        var originalBody = response.Body;

        // Enable synchronous IO for compression streams
        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
        if (feature != null)
        {
            feature.AllowSynchronousIO = true;
        }

        var compressedStream = CreateCompressionStream(originalBody, bestEncoding, level);
        if (compressedStream != null)
        {
            response.Headers["Content-Encoding"] = new StringValues(bestEncoding);
            response.Body = compressedStream;
            context.Items[CompressionStreamKey] = compressedStream;
        }

        return context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestSupportedEncoding(StringValues acceptEncodingHeader)
    {
        if (acceptEncodingHeader.Count == 1)
        {
            return GetBestSupportedEncodingFromSingle(acceptEncodingHeader[0]);
        }

        // Handle multiple header values
        var allEncodings = string.Join(",", acceptEncodingHeader);
        return GetBestSupportedEncodingFromSingle(allEncodings);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestSupportedEncodingFromSingle(string headerValue)
    {
        if (string.IsNullOrEmpty(headerValue)) return null;

        // Use cache for frequently seen header values
        if (EncodingCache.TryGetValue(headerValue, out var cachedEncodings))
        {
            return GetBestEncodingFromArray(cachedEncodings);
        }

        // Parse encodings efficiently using Span<char>
        var parsedEncodings = ParseAcceptEncodingHeader(headerValue.AsSpan());
        
        // Cache the result for future use
        EncodingCache.TryAdd(headerValue, parsedEncodings);
        
        return GetBestEncodingFromArray(parsedEncodings);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestEncodingFromArray(string[] encodings)
    {
        // Return the highest priority encoding that's supported
        foreach (var priorityEncoding in EncodingPriority)
        {
            if (encodings.Contains(priorityEncoding))
            {
                return priorityEncoding;
            }
        }
        return null;
    }

    private static string[] ParseAcceptEncodingHeader(ReadOnlySpan<char> headerValue)
    {
        var encodings = new List<string>();
        var buffer = CharPool.Rent(64); // Rent buffer for temporary operations
        
        try
        {
            int start = 0;
            for (int i = 0; i <= headerValue.Length; i++)
            {
                if (i == headerValue.Length || headerValue[i] == ',')
                {
                    if (i > start)
                    {
                        var encodingSpan = headerValue.Slice(start, i - start);
                        
                        // Find semicolon (quality factor separator)
                        var semicolonIndex = encodingSpan.IndexOf(';');
                        if (semicolonIndex >= 0)
                        {
                            encodingSpan = encodingSpan.Slice(0, semicolonIndex);
                        }
                        
                        // Trim and convert to lowercase efficiently
                        var trimmedSpan = encodingSpan.Trim();
                        if (trimmedSpan.Length > 0 && trimmedSpan.Length < buffer.Length)
                        {
                            // Copy to buffer and convert to lowercase
                            trimmedSpan.CopyTo(buffer.AsSpan(0, trimmedSpan.Length));
                            var bufferSpan = buffer.AsSpan(0, trimmedSpan.Length);
                            
                            // Convert to lowercase in-place
                            for (int j = 0; j < bufferSpan.Length; j++)
                            {
                                bufferSpan[j] = char.ToLowerInvariant(bufferSpan[j]);
                            }
                            
                            var encoding = new string(bufferSpan);
                            
                            if (SupportedEncodings.Contains(encoding))
                            {
                                encodings.Add(encoding);
                            }
                        }
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
    private static Stream CreateCompressionStream(Stream originalBody, string encoding, CompressionLevel level)
    {
        return encoding switch
        {
            "gzip" => new GZipStream(originalBody, level, leaveOpen: true),
            "br" => new BrotliStream(originalBody, level, leaveOpen: true),
            "deflate" => new DeflateStream(originalBody, level, leaveOpen: true),
            _ => null
        };
    }

    public static ValueTask FinalizeCompressionAsync(this HttpRequest request)
        => request.HttpContext.FinalizeCompressionAsync();

    public static async ValueTask FinalizeCompressionAsync(this HttpContext context)
    {
        var cancellationToken = context.Items.TryGetValue(CompressionCancellationTokenKey, out var tokenObj) 
            ? (CancellationToken)tokenObj 
            : CancellationToken.None;

        if (context.Items.TryGetValue(CompressionStreamKey, out var streamObj) && streamObj is Stream compressionStream)
        {
            try
            {
                // Use cancellation token in async operations
                await compressionStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await compressionStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
                throw;
            }
            catch (Exception)
            {
                // Ignore disposal errors that might occur with compression streams
            }
            finally
            {
                context.Items.Remove(CompressionStreamKey);
                context.Items.Remove(CompressionCancellationTokenKey);
            }
        }
    }

    /// <summary>
    /// Optimized method for writing compressed data directly from byte arrays
    /// </summary>
    public static async ValueTask WriteCompressedAsync(this HttpContext context, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (context.Items.TryGetValue(CompressionStreamKey, out var streamObj) && streamObj is Stream compressionStream)
        {
            await compressionStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await context.Response.Body.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Optimized method for writing compressed text data
    /// </summary>
    public static async ValueTask WriteCompressedTextAsync(this HttpContext context, ReadOnlyMemory<char> text, Encoding encoding = null, CancellationToken cancellationToken = default)
    {
        encoding ??= Encoding.UTF8;
        
        // Use ArrayPool for temporary byte buffer
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
}
