using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafeAI.Json;

/// <summary>Shared serializer settings for user-supplied content.</summary>
public static class TypeSafeJsonDefaults
{
    /// <summary>
    /// Options used by the reflection-based <see cref="TypeSafeContent.FromObject{T}(T, JsonSerializerOptions?)"/> overload:
    /// web defaults (camelCase, case-insensitive) with nulls omitted.
    /// </summary>
    public static JsonSerializerOptions ContentSerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
