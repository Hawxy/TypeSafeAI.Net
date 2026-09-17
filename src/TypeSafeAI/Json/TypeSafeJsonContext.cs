using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TypeSafeAI.Json;

// Source-generated serializer metadata for the SDK's wire types.
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(SystemOneRequestPayload))]
[JsonSerializable(typeof(SystemOneResponse))]
[JsonSerializable(typeof(ModelsResponse))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class TypeSafeJsonContext : JsonSerializerContext;
