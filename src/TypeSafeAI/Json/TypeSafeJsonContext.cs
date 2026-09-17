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
[JsonSerializable(typeof(Question))]
[JsonSerializable(typeof(Answer))]
[JsonSerializable(typeof(Usage))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, Question>))]
[JsonSerializable(typeof(Dictionary<string, Answer>))]
internal sealed partial class TypeSafeJsonContext : JsonSerializerContext;
