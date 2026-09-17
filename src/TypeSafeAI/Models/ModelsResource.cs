using TypeSafeAI.Json;

namespace TypeSafeAI;

internal sealed class ModelsResource(HttpPipeline pipeline) : IModelsResource
{
    private readonly Uri _uri = pipeline.Resolve("/v1/models");

    public Task<ModelsResponse> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        pipeline.SendAsync(HttpMethod.Get, _uri, null, TypeSafeJsonContext.Default.ModelsResponse, options, cancellationToken);
}
