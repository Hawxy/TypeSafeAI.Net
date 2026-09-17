using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The models available to the account.</summary>
public sealed class ModelsResponse : TypeSafeResponse
{
    /// <summary>The available models.</summary>
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelInfo> Models { get; init; } = [];
}

/// <summary>Describes one model.</summary>
public sealed class ModelInfo
{
    /// <summary>The model id to pass as <c>model</c>, for example <c>jev-1.13.0</c> or an alias like <c>jev-latest</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A short description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The release date as returned by the API.</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }
}
