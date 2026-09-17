using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TypeSafeAI.Extensions.AI;

[JsonSerializable(typeof(JsonObject))]
internal sealed partial class FunctionJsonContext : JsonSerializerContext;
