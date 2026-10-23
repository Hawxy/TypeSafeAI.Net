using System.Net.Http.Headers;

namespace TypeSafeAI;

// A JSON body, sent as multipart/form-data with one file part per image when the request has images,
// plus the headers its content calls for.
internal sealed class RequestBody(
    byte[] json,
    IReadOnlyDictionary<string, AssetPayload>? assets = null,
    IReadOnlyList<KeyValuePair<string, string>>? headers = null)
{
    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };

    public byte[] Json { get; } = json;

    // Applied before the configured headers so those can override them.
    public IReadOnlyList<KeyValuePair<string, string>>? Headers { get; } = headers;

    // Built once per attempt so retries resend the full body.
    public HttpContent CreateContent()
    {
        var json = new ByteArrayContent(Json);
        json.Headers.ContentType = JsonContentType;
        if (assets is null)
        {
            return json;
        }

        var form = new MultipartFormDataContent { { json, "request" } };
        foreach (var asset in assets.Values)
        {
            var content = new ReadOnlyMemoryContent(asset.Image.Data);
            content.Headers.ContentType = new MediaTypeHeaderValue(asset.Image.MediaType);

            // A file name marks the part as a file upload rather than a form field.
            form.Add(content, asset.Part, asset.Format is { } format ? asset.Part + "." + format : asset.Part);
        }

        return form;
    }
}
