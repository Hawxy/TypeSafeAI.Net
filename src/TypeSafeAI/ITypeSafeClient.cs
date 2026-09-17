namespace TypeSafeAI;

/// <summary>A client for the TypeSafe API. See <see cref="TypeSafeClientExtensions"/> for the convenience overloads.</summary>
public interface ITypeSafeClient
{
    /// <summary>Lists the models available to the account.</summary>
    IModelsResource Models { get; }

    /// <summary>Evaluates one state against named questions.</summary>
    /// <param name="request">The state and questions.</param>
    /// <param name="options">Per-request overrides.</param>
    /// <param name="cancellationToken">Cancels the request and any pending retries.</param>
    /// <exception cref="TypeSafeApiException">The API returned an error status.</exception>
    /// <exception cref="TypeSafeConnectionException">The API could not be reached or timed out on every attempt.</exception>
    /// <exception cref="TypeSafeResponseValidationException">The response body could not be understood.</exception>
    Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>The models endpoint.</summary>
public interface IModelsResource
{
    /// <summary>Lists the models available to the account.</summary>
    Task<ModelsResponse> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default);
}
