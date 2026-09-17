using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>The models endpoint.</summary>
public sealed class ModelsResource : IModelsResource
{
    private readonly HttpPipeline _pipeline;

    internal ModelsResource(HttpPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task<ModelsResponse> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _pipeline.SendAsync(HttpMethod.Get, "/v1/models", null, TypeSafeJsonContext.Default.ModelsResponse, options, cancellationToken);
}
