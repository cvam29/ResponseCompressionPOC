using Newtonsoft.Json;

namespace ResponseCompression.Functions;

public class CompressedJsonFunction(ILogger<CompressedJsonFunction> log)
{
    [FunctionName("CompressedJsonFunction")]
    [OpenApiOperation(operationId: "RunCompressedJson", tags: ["compression"])]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(object), Description = "Compressed JSON response")]
    public async Task<IActionResult> RunCompressedJson(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        log.LogInformation("Processing request with potential compression...");

        req.EnableResponseCompression();

        var response = req.HttpContext.Response;
        response.ContentType = "application/json";

        var payload = new
        {
            Topic = "Hello World... I'm Compressed!",
            Body = "If I was a big JSON payload, I’d be nicely packed for transmission!"
        };

        using (var writer = new StreamWriter(response.Body, leaveOpen: true))
        {
            var json = JsonConvert.SerializeObject(payload);
            await writer.WriteAsync(json);
            await writer.FlushAsync();
        }
        await req.FinalizeCompressionAsync();

        return new EmptyResult(); // Nothing more to return, already written to response body.
    }
}

