namespace ResponseCompression.Functions;

public class CompressedJsonFunction(ILogger<CompressedJsonFunction> log)
{
    [FunctionName("CompressedJsonFunction")]
    [OpenApiOperation(operationId: "RunCompressedJson", tags: ["compression"])]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(object), Description = "Compressed JSON response")]
    public async Task<IActionResult> RunCompressedJson(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req,
        CancellationToken cancellationToken = default)
    {
        log.LogInformation("Processing request with potential compression...");

        // Enable compression with cancellation token support
        req.EnableResponseCompression(cancellationToken);

        var response = req.HttpContext.Response;
        response.ContentType = "application/json";

        var payload = new
        {
            Topic = "Hello World... I'm Compressed!",
            Body = "If I was a big JSON payload, I'd be nicely packed for transmission!",
            Timestamp = DateTimeOffset.UtcNow,
            Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId
        };

        var json = JsonConvert.SerializeObject(payload);
        
        // Use the optimized WriteCompressedTextAsync method
        await req.HttpContext.WriteCompressedTextAsync(json.AsMemory(), cancellationToken: cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        await req.FinalizeCompressionAsync();

        return new EmptyResult(); // Nothing more to return, already written to response body.
    }
}

