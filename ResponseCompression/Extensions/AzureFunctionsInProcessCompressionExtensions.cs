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

    private static readonly string[] SupportedEncodings = { "br", "gzip", "deflate" };

    public static HttpRequest EnableResponseCompression(this HttpRequest request)
        => request.HttpContext.EnableResponseCompression().Request;

    public static HttpContext EnableResponseCompression(this HttpContext context, CompressionLevel level = CompressionLevel.Fastest)
    {
        var acceptedEncodings = context.Request.Headers["Accept-Encoding"]
            .SelectMany(v => v.Split(','))
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(SupportedEncodings.Contains)
            .ToList();

        if (!acceptedEncodings.Any()) return context;

        var response = context.Response;
        var originalBody = response.Body;

        foreach (var encoding in acceptedEncodings)
        {
            Stream compressedStream = encoding switch
            {
                "gzip" => new GZipStream(originalBody, level, leaveOpen: true),
                "br" => new BrotliStream(originalBody, level, leaveOpen: true),
                "deflate" => new DeflateStream(originalBody, level, leaveOpen: true),
                _ => null
            };

            if (compressedStream != null)
            {
                response.Headers["Content-Encoding"] = new StringValues(encoding);
                response.Body = compressedStream;
                context.Items[CompressionStreamKey] = compressedStream;
                break;
            }
        }

        return context;
    }

    public static async Task FinalizeCompressionAsync(this HttpRequest request)
        => await request.HttpContext.FinalizeCompressionAsync();

    public static async Task FinalizeCompressionAsync(this HttpContext context)
    {
        if (context.Items.TryGetValue(CompressionStreamKey, out var streamObj) && streamObj is Stream compressionStream)
        {
            await compressionStream.FlushAsync();
            await compressionStream.DisposeAsync();
            context.Items.Remove(CompressionStreamKey);
        }
    }
}
